using Ardalis.Result;
using BinanceBot.Application.Abstractions;
using BinanceBot.Application.Positions.Commands.ClosePosition;
using BinanceBot.Domain.Common;
using BinanceBot.Domain.MarketData;
using BinanceBot.Domain.Positions;
using BinanceBot.Domain.ValueObjects;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BinanceBot.Infrastructure.Positions;

public sealed class MarkToMarketWorker : BackgroundService
{
    private static readonly TimeSpan Cycle = TimeSpan.FromSeconds(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MarkToMarketWorker> _logger;
    private readonly IOptionsMonitor<BreakEvenOptions> _breakEvenOptions;
    private readonly IOptionsMonitor<TrailingStopOptions> _trailingStopOptions;

    public MarkToMarketWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<MarkToMarketWorker> logger,
        IOptionsMonitor<BreakEvenOptions> breakEvenOptions,
        IOptionsMonitor<TrailingStopOptions> trailingStopOptions)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _breakEvenOptions = breakEvenOptions;
        _trailingStopOptions = trailingStopOptions;
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
    /// ardından (Loop 75) BE move ve (Loop 76) trailing-stop hook'larını sırayla
    /// değerlendir. **Sıra kritik**: BE move ÖNCE çalışır (trailing'i "arm" eder),
    /// trailing SONRA aynı tick içinde peak update / exit dispatch yapar. Aynı
    /// transaction'da kaydedilir.
    /// </summary>
    private async Task TickAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        // Trailing exit dispatch CloseSignalPositionCommand → MARKET reverse leg.
        // Mediator scope-bound; lazily resolve içinde sadece exit oluşunca çağırılır.
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

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
        var trailOpts = _trailingStopOptions.CurrentValue;

        var dirty = 0;
        // Trailing exit candidates aynı tick içinde toplanır; SaveChanges sonrası
        // mediator dispatch edilir (handler kendi scope'unda yeni DbContext açar,
        // mark/peak güncellemeleri çakışmasın).
        var trailingExits = new List<(Position pos, decimal markPrice, decimal peakPrice, decimal trailPct)>();

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

            // Loop 76 — trailing-stop. BE move'dan SONRA çağrılır (sıra kritik):
            // BE bu tick'te uygulandıysa BreakEvenAppliedAt set olur ve trailing
            // burada hemen peak başlangıcını yakalar. BE applied değilse domain
            // method NotEligible döner (no-op).
            TryApplyTrailingStop(position, mid, trailOpts, clock.UtcNow, trailingExits);
        }

        if (dirty > 0)
        {
            await db.SaveChangesAsync(ct);
            _logger.LogDebug("MarkToMarket cycle: updated {Count} positions", dirty);
        }

        // Trailing exit dispatch — SaveChanges'den sonra ki peak update'leri
        // commitlensin, sonra reverse MARKET leg gitsin.
        foreach (var (pos, mark, peak, trailPct) in trailingExits)
        {
            await DispatchTrailingExitAsync(mediator, pos, mark, peak, trailPct, clock, ct);
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

        // Loop 92 — Long+Short symmetry. Long: mark >= entry × (1+trigger), newStop = entry × (1+offset).
        // Short: mark <= entry × (1-trigger), newStop = entry × (1-offset).
        if (opts.TriggerPct <= 0m || opts.OffsetPct <= 0m)
        {
            _logger.LogWarning(
                "BE move skip invalid options trigger={Trigger} offset={Offset}",
                opts.TriggerPct, opts.OffsetPct);
            return;
        }

        var entry = position.AverageEntryPrice;
        if (entry <= 0m) return;

        decimal newStop;
        if (position.Direction == TradeDirection.Long)
        {
            var triggerPrice = entry * (1m + opts.TriggerPct);
            if (markPrice < triggerPrice) return;  // henüz UPnl eşiğin altında
            newStop = entry * (1m + opts.OffsetPct);
        }
        else
        {
            var triggerPrice = entry * (1m - opts.TriggerPct);
            if (markPrice > triggerPrice) return;  // Short için mark düşmedi henüz
            newStop = entry * (1m - opts.OffsetPct);
        }

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

    /// <summary>
    /// Loop 76 trailing-stop evaluator. BE move applied sonra aktif olur. Long-only.
    /// PeakUpdated path'i sessiz log (debug). ExitTriggered candidate listesine
    /// eklenir; gerçek dispatch <see cref="DispatchTrailingExitAsync"/>'da
    /// SaveChanges sonrası yapılır.
    /// </summary>
    private void TryApplyTrailingStop(
        Position position,
        decimal markPrice,
        TrailingStopOptions opts,
        DateTimeOffset asOf,
        List<(Position pos, decimal markPrice, decimal peakPrice, decimal trailPct)> exits)
    {
        if (!opts.Enabled) return;

        // Aggregate kendisi NotEligible döner ama early check log gürültüsünü azaltır.
        if (position.BreakEvenAppliedAt is null) return;

        // Loop 92 — Long+Short trailing aktif. Long: peak high; Short: trough low.
        if (opts.TrailPct <= 0m)
        {
            _logger.LogWarning(
                "TRAILING skip invalid options trailPct={TrailPct}",
                opts.TrailPct);
            return;
        }

        // Capture extreme BEFORE call so logging shows the source value of the decision.
        var prevExtreme = position.ExtremeMarkPrice;

        TrailingResult result;
        try
        {
            result = position.UpdatePeakAndCheckTrailing(markPrice, opts.TrailPct, asOf);
        }
        catch (DomainException ex)
        {
            _logger.LogWarning(ex,
                "TRAILING domain-invariant skip pos={PosId} mark={Mark} trailPct={TrailPct}",
                position.Id, markPrice, opts.TrailPct);
            return;
        }

        switch (result)
        {
            case TrailingResult.NotEligible:
                // BE applied null — defensive (early return zaten yakalamıştı).
                break;
            case TrailingResult.PeakUpdated:
                if (position.ExtremeMarkPrice != prevExtreme)
                {
                    _logger.LogInformation(
                        "TRAILING extreme-update pos={PosId} symbol={Symbol} direction={Direction} prev={Prev} new={New} trailPct={TrailPct}",
                        position.Id, position.Symbol.Value, position.Direction, prevExtreme, position.ExtremeMarkPrice, opts.TrailPct);
                }
                break;
            case TrailingResult.ExitTriggered:
                _logger.LogWarning(
                    "TRAILING-EXIT trigger pos={PosId} symbol={Symbol} direction={Direction} extreme={Extreme} mark={Mark} trailPct={TrailPct}",
                    position.Id, position.Symbol.Value, position.Direction, position.ExtremeMarkPrice, markPrice, opts.TrailPct);
                exits.Add((position, markPrice, position.ExtremeMarkPrice, opts.TrailPct));
                break;
        }
    }

    /// <summary>
    /// Trailing exit MARKET reverse leg dispatch. StopLossMonitorService pattern
    /// referans alındı: ADR-0006 mainnet defensive guard, NotFound silent skip,
    /// hata logu structured. CID prefix "trail-{posId}-{unix}" — audit log'da
    /// stop-loss / time-stop / take-profit'ten ayrılır.
    /// </summary>
    private async Task DispatchTrailingExitAsync(
        IMediator mediator,
        Position pos,
        decimal markPrice,
        decimal peakPrice,
        decimal trailPct,
        IClock clock,
        CancellationToken ct)
    {
        // ADR-0006 defensive guard — mainnet round-trip'i atla, audit pollution olmasın.
        if (pos.Mode == TradingMode.LiveMainnet)
        {
            _logger.LogDebug(
                "TRAILING-EXIT skipped (mainnet blocked) pos={PosId} mark={Mark} peak={Peak}",
                pos.Id, markPrice, peakPrice);
            return;
        }

        var cidPrefix = $"trail-{pos.Id}-{clock.UtcNow.ToUnixTimeSeconds()}";
        var reason = $"trailing_stop_triggered@{markPrice:F4}_peak={peakPrice:F4}_trail={trailPct:F4}";

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
                "TRAILING-EXIT close pos={PosId} mode={Mode} mark={Mark} peak={Peak} cid={Cid}",
                pos.Id, pos.Mode, markPrice, peakPrice, result.Value.CloseClientOrderId);
        }
        else if (result.Status != ResultStatus.NotFound)
        {
            _logger.LogError(
                "TRAILING-EXIT close failed pos={PosId} mode={Mode}: {Errors}",
                pos.Id, pos.Mode, string.Join(";", result.Errors));
        }
    }
}
