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
    private readonly IOptionsMonitor<PositionSafetyOptions> _safetyOptions;

    public MarkToMarketWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<MarkToMarketWorker> logger,
        IOptionsMonitor<BreakEvenOptions> breakEvenOptions,
        IOptionsMonitor<TrailingStopOptions> trailingStopOptions,
        IOptionsMonitor<PositionSafetyOptions> safetyOptions)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _breakEvenOptions = breakEvenOptions;
        _trailingStopOptions = trailingStopOptions;
        _safetyOptions = safetyOptions;
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
        var safetyOpts = _safetyOptions.CurrentValue;

        var dirty = 0;
        // Trailing exit candidates aynı tick içinde toplanır; SaveChanges sonrası
        // mediator dispatch edilir (handler kendi scope'unda yeni DbContext açar,
        // mark/peak güncellemeleri çakışmasın).
        var trailingExits = new List<(Position pos, decimal markPrice, decimal peakPrice, decimal trailPct)>();

        var liquidationExits = new List<(Position pos, decimal markPrice, decimal marginRatio)>();

        // Loop 109 — defansif safety net. Primary monitor'lar (StopLossMonitor /
        // TakeProfitMonitor) tetiklemediği durumda ikinci hat. Üç sebep:
        //   (a) SL/TP redundancy — primary monitor 5sn cycle, race window olabilir,
        //   (b) MaxHoldDuration null pos'lar (signal ContextJson freshness window
        //       dışında doldu) HardMaxHoldMinutes ile zorla kapatılır,
        //   (c) StopPrice/TakeProfit set olmayan pos'larda HardMaxHold tek koruma.
        // CloseSignalPositionCommand idempotenttir (NotFound döner kapalı pos için).
        var safetyExits = new List<(Position pos, decimal markPrice, string reason, string cidPrefix)>();

        foreach (var position in openPositions)
        {
            var ticker = tickers.FirstOrDefault(t => t.Symbol == position.Symbol);
            if (ticker is null) continue;

            var mid = (ticker.BidPrice + ticker.AskPrice) / 2m;
            if (mid <= 0m) continue;

            position.MarkToMarket(mid, clock.UtcNow);
            dirty++;

            // Loop 92 — Futures liquidation guard (binance-expert spec §10).
            // marginRatio = |UnrealizedPnl| / margin (entry notional / leverage).
            // %80 üstünde olursa force close (CloseSignalPositionCommand) — Binance
            // gerçek likidasyondan önce devreye girer, ADL riskini azaltır.
            // MVP: leverage 1x varsayımı → margin = entry × qty (full notional).
            // Loop 14'te leverage RiskProfile'dan okunacak.
            if (TryDetectLiquidationRisk(position, mid, out var marginRatio))
            {
                liquidationExits.Add((position, mid, marginRatio));
            }

            // Loop 75 — break-even SL move. MarkToMarket'ten hemen sonra,
            // aynı tracked aggregate üzerinde tetikleniyor → tek SaveChanges.
            // Loop 92 Long+Short symmetry: BE move worker'ında zaten Direction-aware.
            TryApplyBreakEvenMove(position, mid, beOpts, clock.UtcNow);

            // Loop 76 — trailing-stop. BE move'dan SONRA çağrılır (sıra kritik):
            // BE bu tick'te uygulandıysa BreakEvenAppliedAt set olur ve trailing
            // burada hemen peak başlangıcını yakalar. BE applied değilse domain
            // method NotEligible döner (no-op).
            TryApplyTrailingStop(position, mid, trailOpts, clock.UtcNow, trailingExits);

            // Loop 109 — defansif safety net (SL hit redundancy, TP hit redundancy,
            // hard max-hold). Primary path'lar yetmediği durumda ikinci hat.
            TryQueueSafetyExit(position, mid, safetyOpts, clock.UtcNow, safetyExits);
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

        // Loop 92 — Futures liquidation guard exit dispatch (yüksek öncelikli kapanış).
        foreach (var (pos, mark, ratio) in liquidationExits)
        {
            await DispatchLiquidationExitAsync(mediator, pos, mark, ratio, clock, ct);
        }

        // Loop 109 — defansif safety net dispatch. Primary monitor'lar veya
        // trailing/liquidation tetiklemediyse buradan kapanır.
        foreach (var (pos, mark, reason, cidPrefix) in safetyExits)
        {
            await DispatchSafetyExitAsync(mediator, pos, mark, reason, cidPrefix, ct);
        }
    }

    /// <summary>
    /// Loop 92 — Futures liquidation risk detector. marginRatio = |UnrealizedPnl| /
    /// margin. Margin (1x leverage MVP) = AverageEntryPrice × Quantity. Loss eşiği
    /// %80 (binance-expert spec §10) — Binance gerçek likidasyondan önce force close.
    /// Long: UnrealizedPnl &lt; 0; Short: UnrealizedPnl &lt; 0 (markup pozisyonu için).
    /// </summary>
    private bool TryDetectLiquidationRisk(Position position, decimal mid, out decimal marginRatio)
    {
        marginRatio = 0m;

        // Sadece zarar pozisyonları kontrol edilir.
        if (position.UnrealizedPnl >= 0m) return false;

        var margin = position.AverageEntryPrice * position.Quantity;
        if (margin <= 0m) return false;

        marginRatio = Math.Abs(position.UnrealizedPnl) / margin;
        const decimal LiquidationThreshold = 0.80m;
        return marginRatio >= LiquidationThreshold;
    }

    private async Task DispatchLiquidationExitAsync(
        IMediator mediator,
        Position pos,
        decimal markPrice,
        decimal marginRatio,
        IClock clock,
        CancellationToken ct)
    {
        if (pos.Mode == TradingMode.LiveMainnet)
        {
            _logger.LogDebug(
                "LIQUIDATION-GUARD skipped (mainnet blocked) pos={PosId} mark={Mark} ratio={Ratio}",
                pos.Id, markPrice, marginRatio);
            return;
        }

        var cidPrefix = $"liq-{pos.Id}-{clock.UtcNow.ToUnixTimeSeconds()}";
        var reason = $"liquidation_guard@{markPrice:F4}_marginRatio={marginRatio:P2}";

        _logger.LogCritical(
            "LIQUIDATION-GUARD triggered pos={PosId} symbol={Symbol} direction={Direction} mark={Mark} ratio={Ratio:P2}",
            pos.Id, pos.Symbol.Value, pos.Direction, markPrice, marginRatio);

        var result = await mediator.Send(
            new CloseSignalPositionCommand(
                pos.Symbol.Value,
                pos.StrategyId,
                pos.Mode,
                reason,
                cidPrefix),
            ct);

        if (!result.IsSuccess && result.Status != ResultStatus.NotFound)
        {
            _logger.LogError(
                "LIQUIDATION-GUARD close failed pos={PosId} mode={Mode}: {Errors}",
                pos.Id, pos.Mode, string.Join(";", result.Errors));
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
    /// Loop 76 trailing-stop evaluator / Loop 92 Long+Short / Loop 94 always-on peak
    /// tracking. Pozisyon açıldığı andan itibaren her tick peak/trough günceller —
    /// önceki "BE applied yoksa skip" kuralı kaldırıldı (Loop 93 t60 regresyon: BE
    /// eşiğine ulaşamayan pozisyonlarda ExtremeMarkPrice 0 kalıyordu, sonra BE
    /// applied olduğunda trail yanlış noktadan başlatılıyordu).
    ///
    /// PeakUpdated path'i sessiz log (debug). ExitTriggered candidate listesine
    /// eklenir; gerçek dispatch <see cref="DispatchTrailingExitAsync"/>'da
    /// SaveChanges sonrası yapılır. Trailing exit *karar*ı yine BE applied
    /// sonrası — domain method beArmed kontrolünü içerir.
    /// </summary>
    private void TryApplyTrailingStop(
        Position position,
        decimal markPrice,
        TrailingStopOptions opts,
        DateTimeOffset asOf,
        List<(Position pos, decimal markPrice, decimal peakPrice, decimal trailPct)> exits)
    {
        if (!opts.Enabled) return;

        // Loop 94: BE applied early-return KALDIRILDI — peak tracking artık her zaman aktif.
        // Domain method (UpdatePeakAndCheckTrailing) beArmed bayrağını kendisi kontrol eder.

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

    /// <summary>
    /// Loop 109 — defansif safety net. Primary <c>StopLossMonitorService</c>,
    /// <c>TakeProfitMonitorService</c> veya time-stop dalı tetiklemediyse mark-to-mark
    /// her tick koşulları yeniden değerlendirir. Tetik sırası (ilk match kazanır):
    /// <list type="number">
    ///   <item>SL hit redundancy — Long: <c>mark &lt;= StopPrice</c>; Short: <c>mark &gt;= StopPrice</c>.</item>
    ///   <item>TP hit redundancy — Long: <c>mark &gt;= TakeProfit</c>; Short: <c>mark &lt;= TakeProfit</c>.</item>
    ///   <item>Hard max-hold — <c>now - OpenedAt &gt;= HardMaxHoldMinutes</c>; <c>MaxHoldDuration</c> null
    ///         olsa bile devreye girer (signal ContextJson freshness window dışı senaryoları).</item>
    /// </list>
    /// Primary monitor zaten kapatmışsa <see cref="Application.Positions.Commands.ClosePosition.CloseSignalPositionCommand"/>
    /// NotFound döner — çift dispatch zararsız (idempotency).
    /// </summary>
    private void TryQueueSafetyExit(
        Position position,
        decimal markPrice,
        PositionSafetyOptions opts,
        DateTimeOffset asOf,
        List<(Position pos, decimal markPrice, string reason, string cidPrefix)> exits)
    {
        if (!opts.Enabled) return;

        // 1) SL hit redundancy — primary StopLossMonitorService ile aynı koşul.
        if (opts.StopLossRedundancyEnabled && position.StopPrice is decimal stop && stop > 0m)
        {
            var slHit = position.Direction == TradeDirection.Long
                ? markPrice <= stop
                : markPrice >= stop;
            if (slHit)
            {
                var cidPrefix = $"safe-sl-{position.Id}-{asOf.ToUnixTimeSeconds()}";
                var reason = $"safety_stop_loss@{markPrice:F4}_stop={stop:F4}";
                exits.Add((position, markPrice, reason, cidPrefix));
                return;
            }
        }

        // 2) TP hit redundancy — primary TakeProfitMonitorService ile aynı koşul.
        if (opts.TakeProfitRedundancyEnabled && position.TakeProfit is decimal tp && tp > 0m)
        {
            var tpHit = position.Direction == TradeDirection.Long
                ? markPrice >= tp
                : markPrice <= tp;
            if (tpHit)
            {
                var cidPrefix = $"safe-tp-{position.Id}-{asOf.ToUnixTimeSeconds()}";
                var reason = $"safety_take_profit@{markPrice:F4}_tp={tp:F4}";
                exits.Add((position, markPrice, reason, cidPrefix));
                return;
            }
        }

        // 3) Hard max-hold — MaxHoldDuration null olsa bile pos'u zorla kapat.
        if (opts.HardMaxHoldMinutes > 0)
        {
            var age = asOf - position.OpenedAt;
            var hardCap = TimeSpan.FromMinutes(opts.HardMaxHoldMinutes);
            if (age >= hardCap)
            {
                var cidPrefix = $"safe-mh-{position.Id}-{asOf.ToUnixTimeSeconds()}";
                var reason = $"safety_hard_max_hold@{(int)age.TotalMinutes}min_cap={opts.HardMaxHoldMinutes}min";
                exits.Add((position, markPrice, reason, cidPrefix));
            }
        }
    }

    /// <summary>
    /// Loop 109 — safety net exit dispatch. <see cref="DispatchTrailingExitAsync"/>
    /// pattern referans alındı: ADR-0006 mainnet defensive guard, NotFound silent
    /// skip, hata logu structured. CID prefix safety neden tipini taşır
    /// ("safe-sl-", "safe-tp-", "safe-mh-").
    /// </summary>
    private async Task DispatchSafetyExitAsync(
        IMediator mediator,
        Position pos,
        decimal markPrice,
        string reason,
        string cidPrefix,
        CancellationToken ct)
    {
        if (pos.Mode == TradingMode.LiveMainnet)
        {
            _logger.LogDebug(
                "SAFETY-EXIT skipped (mainnet blocked) pos={PosId} mark={Mark} reason={Reason}",
                pos.Id, markPrice, reason);
            return;
        }

        _logger.LogWarning(
            "SAFETY-EXIT triggered pos={PosId} symbol={Symbol} direction={Direction} mark={Mark} reason={Reason}",
            pos.Id, pos.Symbol.Value, pos.Direction, markPrice, reason);

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
                "SAFETY-EXIT close pos={PosId} mode={Mode} mark={Mark} reason={Reason} cid={Cid}",
                pos.Id, pos.Mode, markPrice, reason, result.Value.CloseClientOrderId);
        }
        else if (result.Status != ResultStatus.NotFound)
        {
            _logger.LogError(
                "SAFETY-EXIT close failed pos={PosId} mode={Mode}: {Errors}",
                pos.Id, pos.Mode, string.Join(";", result.Errors));
        }
        // NotFound = primary monitor zaten kapattı; sessizce geç.
    }
}
