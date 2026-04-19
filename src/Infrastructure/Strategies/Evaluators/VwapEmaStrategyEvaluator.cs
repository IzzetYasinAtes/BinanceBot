using BinanceBot.Application.Strategies.Evaluation;
using BinanceBot.Application.Strategies.Indicators;
using BinanceBot.Domain.MarketData;
using BinanceBot.Domain.Strategies;
using Microsoft.Extensions.Logging;

namespace BinanceBot.Infrastructure.Strategies.Evaluators;

/// <summary>
/// ADR-0016 §16.2 + §16.3. VWAP-reclaim + EMA21(1h) trend-gate hybrid scalper — V2
/// high-frequency tuning. Supersedes the ADR-0015 formulas.
///
/// Entry emits <see cref="StrategySignalDirection.Long"/> on the closed 1m bar when
/// all four conditions hold simultaneously:
///   1. Direction gate — 1h EMA21 slope is above tolerance:
///        <c>nowEma &gt;= prevEma × (1 + SlopeTolerance)</c>.
///      Default <c>SlopeTolerance = -0.0005</c> (slope ≥ −%0.05) — net bear
///      trends remain blocked but hair-line consolidations re-open the gate.
///   2. VWAP context  — the previous bar closed below VWAP (pullback, unchanged).
///   3. VWAP reclaim  — last bar is above VWAP OR within the VWAP zone:
///        <c>last &gt; VWAP OR Math.Abs(last − VWAP) / VWAP &lt; VwapTolerancePct</c>.
///      Default <c>VwapTolerancePct = 0.0015</c> (±%0.15).
///   4. Volume confirm — last-bar volume ≥ SMA20 × VolumeMultiplier
///      (default multiplier relaxed from 1.2 → 1.05).
///
/// Exits (encoded in the emitted <see cref="StrategyEvaluation"/>):
///   - Take-profit: <c>entryPrice × (1 + TpGrossPct)</c>, default 0.007 (%0.7 gross).
///   - Stop-loss:   <c>entryPrice × (1 − StopPct)</c>, per-seed override
///                  (BTC/BNB 0.003, XRP 0.004).
///   - Time-stop:   <c>MaxHoldMinutes = 12</c> (downstream monitor reads via ContextJson).
///
/// ADR-0015 swingHigh × 0.95 TP path is deprecated — fixed-percent TP is deterministic
/// and aligns with Binance 1m volatility (ADR-0016 §16.3, research §3.4). The
/// <c>SwingLookback</c>/<c>TpSafetyFactor</c> fields remain for backward compatibility
/// with existing persisted JSON but are no longer consulted.
///
/// The evaluator never opens a DB connection, never allocates a rolling buffer —
/// it depends on <see cref="IMarketIndicatorService"/> for pre-computed primitives.
/// </summary>
public sealed class VwapEmaStrategyEvaluator : IStrategyEvaluator
{
    public StrategyType Type => StrategyType.VwapEmaHybrid;

    private readonly IMarketIndicatorService _indicators;
    private readonly ILogger<VwapEmaStrategyEvaluator> _logger;

    public VwapEmaStrategyEvaluator(
        IMarketIndicatorService indicators,
        ILogger<VwapEmaStrategyEvaluator> logger)
    {
        _indicators = indicators;
        _logger = logger;
    }

    public Task<StrategyEvaluation?> EvaluateAsync(
        long strategyId,
        string parametersJson,
        string symbol,
        IReadOnlyList<Kline> closedBars,
        CancellationToken cancellationToken)
    {
        var p = EvaluatorParameterHelper.TryParse<Parameters>(parametersJson) ?? new Parameters();

        var snapshot = _indicators.TryGetSnapshot(symbol);
        if (snapshot is null)
        {
            _logger.LogDebug(
                "VwapEma snapshot not ready symbol={Symbol} strategyId={StrategyId}",
                symbol, strategyId);
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        // Guard against pre-warmup snapshots that slipped through. SwingHigh20 still
        // participates in the guard because the indicator buffer treats it as a
        // warmup completeness signal, even though TP no longer consumes it.
        if (snapshot.Vwap <= 0m
            || snapshot.VolumeSma20 <= 0m
            || snapshot.Ema1h21Now <= 0m
            || snapshot.Ema1h21Prev <= 0m)
        {
            _logger.LogDebug(
                "VwapEma snapshot incomplete symbol={Symbol} vwap={Vwap} volSma={Vol}",
                symbol, snapshot.Vwap, snapshot.VolumeSma20);
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        // ADR-0016 §16.2 (1): slope >= SlopeTolerance relative to prev EMA. Using
        // (1 + tol) lets a negative tol allow small downward slopes while a positive
        // tol tightens the gate.
        var slopeGateLevel = snapshot.Ema1h21Prev * (1m + p.SlopeTolerance);
        var directionGate = snapshot.Ema1h21Now >= slopeGateLevel;

        var vwapContext = snapshot.PrevBarClose < snapshot.Vwap;

        // ADR-0016 §16.2 (3): reclaim OR zone. Zone uses strict-less-than so tests
        // can size exact boundary values predictably.
        var vwapAboveReclaim = snapshot.LastBarClose > snapshot.Vwap;
        var vwapDistance = snapshot.Vwap > 0m
            ? Math.Abs(snapshot.LastBarClose - snapshot.Vwap) / snapshot.Vwap
            : decimal.MaxValue;
        var vwapZoneOk = vwapDistance < p.VwapTolerancePct;
        var vwapReclaim = vwapAboveReclaim || vwapZoneOk;

        var volumeRatio = snapshot.VolumeSma20 > 0m
            ? snapshot.LastBarVolume / snapshot.VolumeSma20
            : 0m;
        var volumeConfirm = volumeRatio > p.VolumeMultiplier;

        if (!directionGate || !vwapContext || !vwapReclaim || !volumeConfirm)
        {
            var slope = snapshot.Ema1h21Prev > 0m
                ? (snapshot.Ema1h21Now - snapshot.Ema1h21Prev) / snapshot.Ema1h21Prev
                : 0m;
            _logger.LogDebug(
                "VwapEma V2 skip symbol={Symbol} slope={Slope} slopeTol={SlopeTol} " +
                "vwapCtx={Below} reclaim={Reclaim} vwapZoneOk={ZoneOk} volRatio={Ratio} decision=Skip",
                symbol, slope, p.SlopeTolerance,
                vwapContext, vwapAboveReclaim, vwapZoneOk, volumeRatio);
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        // ADR-0016 §16.3: TP = entry × (1 + TpGrossPct); SL = entry × (1 − StopPct).
        // Per-symbol StopPct override (opt-in sembol → decimal map) honoured before
        // the seed-level default — Loop 21 seeds encode per-seed StopPct directly,
        // but we leave the dict path open for future tek-seed multi-symbol designs.
        var entryPrice = snapshot.LastBarClose;
        var stopPct = ResolveStopPct(p, symbol);
        var stopPrice = entryPrice * (1m - stopPct);
        var takeProfit = entryPrice * (1m + p.TpGrossPct);

        if (takeProfit <= entryPrice || stopPrice >= entryPrice)
        {
            _logger.LogDebug(
                "VwapEma geometry invalid symbol={Symbol} entry={Entry} stop={Stop} tp={Tp}",
                symbol, entryPrice, stopPrice, takeProfit);
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        var slopeRatio = snapshot.Ema1h21Prev > 0m
            ? (snapshot.Ema1h21Now - snapshot.Ema1h21Prev) / snapshot.Ema1h21Prev
            : 0m;

        var ctx = EvaluatorParameterHelper.SerializeContext(new
        {
            type = "vwap-ema-hybrid-v2",
            vwap = snapshot.Vwap,
            ema1h21Now = snapshot.Ema1h21Now,
            ema1h21Prev = snapshot.Ema1h21Prev,
            slope = slopeRatio,
            prevBarClose = snapshot.PrevBarClose,
            lastBarClose = snapshot.LastBarClose,
            vwapDistance,
            volumeRatio,
            stopPct,
            tpGrossPct = p.TpGrossPct,
            maxHoldMinutes = p.MaxHoldMinutes,
        });

        _logger.LogInformation(
            "VwapEma V2 emit symbol={Symbol} entry={Entry} stop={Stop} tp={Tp} " +
            "slope={Slope} vwapZone={ZoneOk} volumeRatio={Ratio}",
            symbol, entryPrice, stopPrice, takeProfit, slopeRatio, vwapZoneOk, volumeRatio);

        // SuggestedQuantity is a placeholder — fan-out handler overrides via sizing service.
        // A positive-but-tiny value satisfies the StrategySignal.Emit domain invariant
        // (quantity > 0); the real trade quantity is computed at fan-out time using
        // equity × 0.20 floor-$20 rule plus LOT_SIZE/NOTIONAL filters.
        return Task.FromResult<StrategyEvaluation?>(new StrategyEvaluation(
            Direction: StrategySignalDirection.Long,
            SuggestedQuantity: 0.00000001m,
            SuggestedPrice: entryPrice,
            SuggestedStopPrice: stopPrice,
            ContextJson: ctx,
            SuggestedTakeProfit: takeProfit));
    }

    private static decimal ResolveStopPct(Parameters p, string symbol)
    {
        if (p.StopPctPerSymbol is { Count: > 0 } map
            && !string.IsNullOrWhiteSpace(symbol)
            && map.TryGetValue(symbol, out var perSymbol)
            && perSymbol > 0m)
        {
            return perSymbol;
        }
        return p.StopPct;
    }

    /// <summary>
    /// ADR-0016 §16.5 parameter contract. Serialised as <c>Strategies.Seed[].ParametersJson</c>.
    /// Per-symbol overrides for BTC/BNB (0.003) vs XRP (0.004) live in each seed's JSON.
    /// </summary>
    private sealed class Parameters
    {
        // ADR-0015 fields (new defaults per ADR-0016).
        public decimal StopPct { get; set; } = 0.003m;
        public decimal VolumeMultiplier { get; set; } = 1.05m;
        public int SwingLookback { get; set; } = 20;            // deprecated, kept for JSON back-compat
        public decimal TpSafetyFactor { get; set; } = 0.95m;    // deprecated, kept for JSON back-compat
        public int MaxHoldMinutes { get; set; } = 12;
        public int EmaPeriod { get; set; } = 21;
        public string EmaTimeframe { get; set; } = "1h";
        public int VwapWindowBars { get; set; } = 1440;

        // ADR-0016 new fields.
        public decimal VwapTolerancePct { get; set; } = 0.0015m;
        public decimal SlopeTolerance { get; set; } = -0.0005m;
        public decimal TpGrossPct { get; set; } = 0.007m;

        // Optional per-symbol StopPct override (future tek-seed multi-symbol
        // designs); Loop 21 seeds leave this null.
        public Dictionary<string, decimal>? StopPctPerSymbol { get; set; }
    }
}
