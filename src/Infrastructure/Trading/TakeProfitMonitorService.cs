using Ardalis.Result;
using BinanceBot.Application.Abstractions;
using BinanceBot.Application.Positions.Commands.ClosePosition;
using BinanceBot.Domain.Common;
using BinanceBot.Domain.Positions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BinanceBot.Infrastructure.Trading;

/// <summary>
/// Soft client-side take-profit monitor (Loop 10 fix — realize gains).
///
/// Symmetric counterpart of <see cref="StopLossMonitorService"/>: every 30 seconds
/// walks every open <see cref="Position"/> with a non-null <see cref="Position.TakeProfit"/>
/// and dispatches <see cref="CloseSignalPositionCommand"/> via MediatR when the mark price
/// reaches the target. Long positions trigger when bid &gt;= TakeProfit; short when ask &lt;=.
///
/// Loop 4-9 root cause: stop-loss alone realised losses but never gains, so equity peaked
/// during open positions and bled back through stops. This service closes the asymmetry —
/// peak equity now becomes realised PnL when TP fires.
///
/// **Strategy-status agnostic** (same contract as StopLossMonitor): a paused strategy still
/// has its open positions protected by take-profit. Pausing only halts new signal evaluation.
///
/// Mainnet positions are skipped defensively to avoid polluting the audit log with
/// mainnet_blocked rejects from the downstream PlaceOrderCommand path (ADR-0006 still in force).
/// </summary>
public sealed class TakeProfitMonitorService : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TakeProfitMonitorService> _logger;

    public TakeProfitMonitorService(
        IServiceScopeFactory scopeFactory,
        ILogger<TakeProfitMonitorService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TakeProfitMonitor started, tick={Sec}s", TickInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TakeProfitMonitor tick failed");
            }

            try
            {
                await Task.Delay(TickInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task TickOnceAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var openPositions = await db.Positions
            .AsNoTracking()
            .Where(p => p.Status == PositionStatus.Open && p.TakeProfit != null)
            .ToListAsync(ct);

        if (openPositions.Count == 0)
        {
            return;
        }

        // Loop 4 öğrenmesi: EF Symbol VO HasConversion → Contains(VO) bazı providerlarda translate edilemez.
        // GetMarketSummary §60-65 pattern'iyle hizala: string list + Symbol.Value karşılaştırması.
        var symbols = openPositions.Select(p => p.Symbol.Value).Distinct().ToList();
        var allTickers = await db.BookTickers
            .AsNoTracking()
            .ToListAsync(ct);
        var tickers = allTickers.Where(b => symbols.Contains(b.Symbol.Value)).ToList();

        foreach (var pos in openPositions)
        {
            if (pos.TakeProfit is not decimal tp)
            {
                continue;
            }

            var bt = tickers.FirstOrDefault(t => t.Symbol == pos.Symbol);
            if (bt is null)
            {
                continue;
            }

            // Long realises into the bid (the price someone will pay us); short realises
            // into the ask (the price we must pay to cover). Same convention as StopLossMonitor.
            var markPrice = pos.Side == PositionSide.Long ? bt.BidPrice : bt.AskPrice;
            if (markPrice <= 0m)
            {
                continue;
            }

            var triggered = pos.Side == PositionSide.Long
                ? markPrice >= tp
                : markPrice <= tp;
            if (!triggered)
            {
                continue;
            }

            // ADR-0006 defensive guard (same as StopLossMonitor): skip mainnet so the audit
            // log stays clean of mainnet_blocked rejects.
            if (pos.Mode == TradingMode.LiveMainnet)
            {
                _logger.LogDebug(
                    "TAKE-PROFIT skipped (mainnet blocked) pos={PosId} mark={Mark} tp={Tp}",
                    pos.Id, markPrice, tp);
                continue;
            }

            var cidPrefix = $"tp-{pos.Id}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
            var reason = $"take_profit_triggered@{markPrice:F4}_tp={tp:F4}";

            var result = await mediator.Send(
                new CloseSignalPositionCommand(
                    pos.Symbol.Value,
                    pos.StrategyId,
                    pos.Mode,
                    reason,
                    cidPrefix),
                ct);

            if (result.IsSuccess)
            {
                _logger.LogInformation(
                    "TAKE-PROFIT triggered pos={PosId} mode={Mode} mark={Mark} tp={Tp} cid={Cid}",
                    pos.Id, pos.Mode, markPrice, tp, result.Value.CloseClientOrderId);
            }
            else if (result.Status != ResultStatus.NotFound)
            {
                _logger.LogError(
                    "TAKE-PROFIT close failed pos={PosId} mode={Mode}: {Errors}",
                    pos.Id, pos.Mode, string.Join(";", result.Errors));
            }
        }
    }
}
