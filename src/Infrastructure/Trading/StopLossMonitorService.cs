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
/// Soft client-side stop-loss monitor (ADR-0012 §12.3).
///
/// Every 30 seconds, walks every open <see cref="Position"/> (mode-agnostic — Paper,
/// LiveTestnet, LiveMainnet — defensive guards in <c>CloseSignalPositionCommand</c>
/// and <c>PlaceOrderCommand</c> stop a real submission for blocked modes) and, when the
/// mark price has crossed the persisted <see cref="Position.StopPrice"/>, dispatches
/// <see cref="CloseSignalPositionCommand"/> via MediatR. The reverse-side MARKET order
/// then exits the position through the standard fan-out pipeline.
///
/// This is the temporary stand-in for proper server-side OCO/STOP_LOSS_LIMIT (deferred
/// to ADR-0013). 30s tick is a known latency vs an event-driven trigger; spec accepts it
/// as a Loop 5 trade-off.
/// </summary>
public sealed class StopLossMonitorService : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StopLossMonitorService> _logger;

    public StopLossMonitorService(
        IServiceScopeFactory scopeFactory,
        ILogger<StopLossMonitorService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("StopLossMonitor started, tick={Sec}s", TickInterval.TotalSeconds);

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
                _logger.LogError(ex, "StopLossMonitor tick failed");
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
            .Where(p => p.Status == PositionStatus.Open && p.StopPrice != null)
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
            if (pos.StopPrice is not decimal stop)
            {
                continue;
            }

            var bt = tickers.FirstOrDefault(t => t.Symbol == pos.Symbol);
            if (bt is null)
            {
                continue;
            }

            // Long positions exit on a falling bid; short positions exit on a rising ask.
            var markPrice = pos.Side == PositionSide.Long ? bt.BidPrice : bt.AskPrice;
            if (markPrice <= 0m)
            {
                continue;
            }

            var triggered = pos.Side == PositionSide.Long
                ? markPrice <= stop
                : markPrice >= stop;
            if (!triggered)
            {
                continue;
            }

            // ADR-0006 defensive guard: even though CloseSignalPositionCommand → PlaceOrderCommand
            // already short-circuits LiveMainnet (no equity, mainnet blocker), skip the round-trip
            // here so the audit log isn't polluted with mode=LiveMainnet mainnet_blocked rejects.
            if (pos.Mode == TradingMode.LiveMainnet)
            {
                _logger.LogDebug(
                    "STOP-LOSS skipped (mainnet blocked) pos={PosId} mark={Mark} stop={Stop}",
                    pos.Id, markPrice, stop);
                continue;
            }

            var cidPrefix = $"stop-{pos.Id}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
            var reason = $"stop_loss_triggered@{markPrice:F4}_stop={stop:F4}";

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
                _logger.LogWarning(
                    "STOP-LOSS triggered pos={PosId} mode={Mode} mark={Mark} stop={Stop} cid={Cid}",
                    pos.Id, pos.Mode, markPrice, stop, result.Value.CloseClientOrderId);
            }
            else if (result.Status != ResultStatus.NotFound)
            {
                _logger.LogError(
                    "STOP-LOSS close failed pos={PosId} mode={Mode}: {Errors}",
                    pos.Id, pos.Mode, string.Join(";", result.Errors));
            }
        }
    }
}
