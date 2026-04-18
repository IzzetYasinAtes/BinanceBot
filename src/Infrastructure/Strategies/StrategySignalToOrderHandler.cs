using BinanceBot.Application.Abstractions;
using BinanceBot.Application.Abstractions.Trading;
using BinanceBot.Application.Orders.Commands.PlaceOrder;
using BinanceBot.Application.Positions.Commands.ClosePosition;
using BinanceBot.Domain.Common;
using BinanceBot.Domain.Orders;
using BinanceBot.Domain.Positions;
using BinanceBot.Domain.RiskProfiles;
using BinanceBot.Domain.Strategies;
using BinanceBot.Domain.Strategies.Events;
using BinanceBot.Domain.ValueObjects;
using BinanceBot.Infrastructure.Trading.Paper;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BinanceBot.Infrastructure.Strategies;

/// <summary>
/// Fan-out one strategy signal to three trading modes (Paper / LiveTestnet / LiveMainnet).
/// Each mode gets a distinct ClientOrderId via mode suffix (ADR-0008 §8.2).
/// Failures in one mode must not block the others.
///
/// ADR-0011 §11.4 / decision-sizing.md Commit 6: BUG-B fix — quantity now comes from
/// <see cref="IPositionSizingService"/> (was hardcoded 0.001). Exit signals route to
/// <see cref="CloseSignalPositionCommand"/> per mode.
/// </summary>
public sealed class StrategySignalToOrderHandler : INotificationHandler<StrategySignalEmittedEvent>
{
    private static readonly TradingMode[] AllModes =
    {
        TradingMode.Paper,
        TradingMode.LiveTestnet,
        TradingMode.LiveMainnet,
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StrategySignalToOrderHandler> _logger;

    public StrategySignalToOrderHandler(
        IServiceScopeFactory scopeFactory,
        ILogger<StrategySignalToOrderHandler> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task Handle(StrategySignalEmittedEvent notification, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var mediator = sp.GetRequiredService<IMediator>();
        var db = sp.GetRequiredService<IApplicationDbContext>();
        var sizing = sp.GetRequiredService<IPositionSizingService>();
        var equityProvider = sp.GetRequiredService<IEquitySnapshotProvider>();
        var paperOpts = sp.GetRequiredService<IOptions<PaperFillOptions>>().Value;

        var barUnix = notification.BarOpenTime.ToUnixTimeSeconds();
        var cidPrefix = $"sig-{notification.StrategyId}-{barUnix}";

        // Branch 1: Exit signals close the open position per mode.
        if (notification.Direction == StrategySignalDirection.Exit)
        {
            await HandleExit(mediator, notification, cidPrefix, ct);
            return;
        }

        // Branch 2: Entry — Long → Buy, Short → Sell.
        var side = notification.Direction == StrategySignalDirection.Long
            ? OrderSide.Buy
            : OrderSide.Sell;

        // EF Core cannot translate the `Symbol.Value` property accessor in WHERE predicates
        // (Symbol is a value object mapped via HasConversion). Compare against the VO directly.
        var symbolVo = Symbol.From(notification.Symbol);
        var instrument = await db.Instruments.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Symbol == symbolVo, ct);
        if (instrument is null)
        {
            _logger.LogWarning("Instrument not registered: {Symbol}", notification.Symbol);
            return;
        }

        var ticker = await db.BookTickers.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Symbol == symbolVo, ct);
        if (ticker is null)
        {
            _logger.LogWarning("BookTicker missing: {Symbol}", notification.Symbol);
            return;
        }

        var entry = side == OrderSide.Buy ? ticker.AskPrice : ticker.BidPrice;
        if (entry <= 0m)
        {
            _logger.LogWarning("Invalid book price for {Symbol} side={Side}", notification.Symbol, side);
            return;
        }

        // SuggestedStopPrice now travels in the event payload (ADR-0011 §11.4 + Commit 6.1).
        // When unknown (older evaluators or tests), stopDistance falls back to 0 — sizing then
        // collapses to the position-cap branch.
        var stopDistance = notification.SuggestedStopPrice is decimal stop
            ? Math.Abs(entry - stop)
            : 0m;

        foreach (var mode in AllModes)
        {
            var cid = $"{cidPrefix}-{mode.ToCidSuffix()}";

            var risk = await db.RiskProfiles.AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == RiskProfile.IdFor(mode), ct);
            if (risk is null)
            {
                _logger.LogWarning("Risk profile missing for mode {Mode}", mode);
                continue;
            }
            if (risk.CircuitBreakerStatus == CircuitBreakerStatus.Tripped)
            {
                _logger.LogInformation("CB tripped mode={Mode}, signal skipped {Cid}", mode, cid);
                continue;
            }

            // Loop 14 (research-paper-live-and-sizing.md §C5): cap concurrently-open
            // positions per mode. Without this throttle a strategy storm on a
            // $100 paper account can stack multiple $40 notionals across symbols and
            // exhaust available equity before the CB has a chance to react.
            var openCount = await db.Positions.AsNoTracking()
                .CountAsync(p => p.Status == PositionStatus.Open && p.Mode == mode, ct);
            if (openCount >= risk.MaxOpenPositions)
            {
                _logger.LogInformation(
                    "Max open positions reached mode={Mode} count={Count}/{Max} skip {Cid}",
                    mode, openCount, risk.MaxOpenPositions, cid);
                continue;
            }

            var equity = await equityProvider.GetEquityAsync(mode, ct);
            if (equity <= 0m)
            {
                _logger.LogInformation("Equity <= 0 mode={Mode}, signal skipped {Cid}", mode, cid);
                continue;
            }

            // Slippage is Paper-only (live exchanges report real fills).
            var slip = mode == TradingMode.Paper ? paperOpts.FixedSlippagePct : 0m;

            var sizingResult = sizing.Calculate(new PositionSizingInput(
                Equity: equity,
                EntryPrice: entry,
                StopDistance: stopDistance,
                RiskPct: risk.RiskPerTradePct,
                MaxPositionPct: risk.MaxPositionSizePct,
                MinNotional: instrument.MinNotional,
                StepSize: instrument.StepSize,
                MinQty: instrument.MinQty,
                SlippagePct: slip));

            if (sizingResult.Quantity <= 0m)
            {
                _logger.LogInformation(
                    "Sizing skipped mode={Mode} {Symbol} reason={Reason} notional={Notional}",
                    mode, notification.Symbol, sizingResult.SkipReason, sizingResult.NotionalEstimate);
                continue;
            }

            // ADR-0012 §12.4: forward evaluator-suggested stop into the Order so the eventual
            // Position carries it. The parameter is ignored for MARKET execution itself but
            // preserved on the Order aggregate for downstream stop-monitor wiring.
            // Loop 10 take-profit fix: SuggestedTakeProfit travels via the same channel so the
            // resulting Position carries a TP for TakeProfitMonitorService.
            var cmd = new PlaceOrderCommand(
                cid,
                notification.Symbol,
                side.ToString(),
                OrderType.Market.ToString(),
                TimeInForce.Ioc.ToString(),
                sizingResult.Quantity,
                null,
                notification.SuggestedStopPrice,
                notification.StrategyId,
                mode,
                TakeProfit: notification.SuggestedTakeProfit);

            try
            {
                var result = await mediator.Send(cmd, ct);
                if (!result.IsSuccess)
                {
                    _logger.LogWarning(
                        "Fan-out order rejected mode={Mode} {Cid}: {Errors}",
                        mode, cid, string.Join(";", result.Errors));
                }
                else
                {
                    _logger.LogInformation(
                        "Fan-out order placed mode={Mode} {Cid} qty={Qty} status={Status}",
                        mode, cid, sizingResult.Quantity, result.Value.Status);
                }
            }
            catch (Exception ex)
            {
                // One mode's failure must never cascade to the others.
                _logger.LogError(ex, "Fan-out order exception mode={Mode} {Cid}", mode, cid);
            }
        }
    }

    private async Task HandleExit(
        IMediator mediator,
        StrategySignalEmittedEvent notification,
        string cidPrefix,
        CancellationToken ct)
    {
        foreach (var mode in AllModes)
        {
            try
            {
                var result = await mediator.Send(
                    new CloseSignalPositionCommand(
                        notification.Symbol,
                        notification.StrategyId,
                        mode,
                        "exit_signal",
                        cidPrefix), ct);

                if (!result.IsSuccess && result.Status != Ardalis.Result.ResultStatus.NotFound)
                {
                    _logger.LogWarning(
                        "Close rejected mode={Mode} {Symbol}: {Errors}",
                        mode, notification.Symbol, string.Join(";", result.Errors));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Close exception mode={Mode} {Symbol}", mode, notification.Symbol);
            }
        }
    }
}
