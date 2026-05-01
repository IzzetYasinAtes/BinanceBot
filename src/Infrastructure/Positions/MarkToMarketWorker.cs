using BinanceBot.Application.Abstractions;
using BinanceBot.Domain.MarketData;
using BinanceBot.Domain.Positions;
using BinanceBot.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BinanceBot.Infrastructure.Positions;

public sealed class MarkToMarketWorker : BackgroundService
{
    private static readonly TimeSpan Cycle = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MarkToMarketWorker> _logger;
    private readonly IOptionsMonitor<BreakEvenOptions> _breakEvenOptions;

    public MarkToMarketWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<MarkToMarketWorker> logger,
        IOptionsMonitor<BreakEvenOptions> breakEvenOptions)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _breakEvenOptions = breakEvenOptions;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MarkToMarketWorker tick failed");
            }

            try
            {
                await Task.Delay(Cycle, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Tek tick: open pozisyonları çek, mid-price ile <c>MarkToMarket</c> uygula,
    /// ardından (Loop 75) BE move trigger'ını değerlendir. Aynı transaction'da
    /// kaydedilir — BE move sonrası StopPrice update <see cref="Trading.StopLossMonitorService"/>
    /// için bir sonraki tick'te (≤30s) görünür olur.
    /// </summary>
    private async Task TickAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var openPositions = await db.Positions
            .Where(p => p.Status == PositionStatus.Open)
            .ToListAsync(ct);

        if (openPositions.Count == 0) return;

        var symbols = openPositions.Select(p => p.Symbol).Distinct().ToList();
        var tickers = await db.BookTickers
            .AsNoTracking()
            .Where(b => symbols.Contains(b.Symbol))
            .ToListAsync(ct);

        var beOpts = _breakEvenOptions.CurrentValue;

        var dirty = 0;
        foreach (var position in openPositions)
        {
            var ticker = tickers.FirstOrDefault(t => t.Symbol == position.Symbol);
            if (ticker is null) continue;

            var mid = (ticker.BidPrice + ticker.AskPrice) / 2m;
            if (mid <= 0m) continue;

            position.MarkToMarket(mid, clock.UtcNow);
            dirty++;

            // Loop 75 — break-even SL move. MarkToMarket'ten hemen sonra,
            // aynı tracked aggregate üzerinde tetikleniyor → tek SaveChanges.
            // Long-only kontrat (KMS Long-only spot); Short eklenirse simetri
            // burada genişler (mark <= entry × (1 - TriggerPct)).
            TryApplyBreakEvenMove(position, mid, beOpts, clock.UtcNow);
        }

        if (dirty > 0)
        {
            await db.SaveChangesAsync(ct);
            _logger.LogDebug("MarkToMarket cycle: updated {Count} positions", dirty);
        }
    }

    /// <summary>
    /// Loop 75 BE move evaluator. Side-effect: pozisyon eligible ise
    /// <see cref="Position.MoveStopToBreakEven"/> çağrılır (idempotency aggregate
    /// içinde garanti). No-throw: invariant ihlalleri bile defensive log + skip.
    /// </summary>
    private void TryApplyBreakEvenMove(
        Position position,
        decimal markPrice,
        BreakEvenOptions opts,
        DateTimeOffset asOf)
    {
        if (!opts.Enabled) return;

        // Aggregate-side idempotency: BreakEvenAppliedAt set ise ileri filtre — DB row'u
        // boş yere event raise etmesin. Domain method da AlreadyApplied döner; bu
        // erken return kurşun-yele.
        if (position.BreakEvenAppliedAt is not null) return;

        // MVP: Long-only path. KMS Long-only kontratıyla hizalı; Short eklenirse
        // burada simetrik else dalı eklenir. Short geldiğinde "early skip" sessiz
        // değil, explicit log.
        if (position.Side != PositionSide.Long)
        {
            _logger.LogDebug(
                "BE move skip non-Long pos={PosId} side={Side} (Loop 75 MVP Long-only)",
                position.Id, position.Side);
            return;
        }

        if (opts.TriggerPct <= 0m || opts.OffsetPct <= 0m)
        {
            _logger.LogWarning(
                "BE move skip invalid options trigger={Trigger} offset={Offset}",
                opts.TriggerPct, opts.OffsetPct);
            return;
        }

        var entry = position.AverageEntryPrice;
        if (entry <= 0m) return;

        var triggerPrice = entry * (1m + opts.TriggerPct);
        if (markPrice < triggerPrice) return;  // henüz UPnl eşiğin altında

        var newStop = entry * (1m + opts.OffsetPct);

        var result = position.MoveStopToBreakEven(newStop, asOf);
        switch (result)
        {
            case MoveStopResult.Applied:
                _logger.LogInformation(
                    "BE-MOVE applied pos={PosId} symbol={Symbol} entry={Entry} mark={Mark} " +
                    "newStop={NewStop} triggerPct={TriggerPct} offsetPct={OffsetPct}",
                    position.Id, position.Symbol.Value, entry, markPrice,
                    newStop, opts.TriggerPct, opts.OffsetPct);
                break;
            case MoveStopResult.NotImproving:
                _logger.LogDebug(
                    "BE-MOVE no-improve pos={PosId} symbol={Symbol} currentStop={Current} newStop={NewStop}",
                    position.Id, position.Symbol.Value, position.StopPrice, newStop);
                break;
            case MoveStopResult.AlreadyApplied:
                // Defensive — early return zaten yakalamıştı, ama race-safety için.
                break;
        }
    }
}
