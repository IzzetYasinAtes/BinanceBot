using BinanceBot.Application.Strategies.Evaluation;
using BinanceBot.Application.Strategies.Indicators;
using BinanceBot.Domain.MarketData;
using BinanceBot.Domain.Strategies;
using Microsoft.Extensions.Logging;

namespace BinanceBot.Infrastructure.Strategies.Evaluators;

/// <summary>
/// ADR-0015 §15.2 + §15.3. VWAP-reclaim + EMA21(1h) trend-gate hybrid scalper.
///
/// Entry emits <see cref="StrategySignalDirection.Long"/> on the closed 1m bar when
/// all four conditions hold simultaneously:
///   1. Direction gate — 1h EMA21 is rising (EMA[now] &gt; EMA[prev]).
///   2. VWAP context  — the previous bar closed below VWAP (pullback).
///   3. VWAP reclaim  — the latest closed bar closed above VWAP (bullish reclaim).
///   4. Volume confirm — last-bar volume ≥ SMA20 × <c>VolumeMultiplier</c>.
///
/// Emitted signal carries: stop = entry × (1 − stopPct), TP = swingHigh20 × 0.95,
/// maxHold = 15 minutes (all encoded in the parameters JSON). Short side is
/// intentionally absent — ADR-0006 + user rule: spot long-only.
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

        // Guard against pre-warmup snapshots that slipped through.
        if (snapshot.Vwap <= 0m
            || snapshot.VolumeSma20 <= 0m
            || snapshot.SwingHigh20 <= 0m
            || snapshot.Ema1h21Now <= 0m
            || snapshot.Ema1h21Prev <= 0m)
        {
            _logger.LogDebug(
                "VwapEma snapshot incomplete symbol={Symbol} vwap={Vwap} volSma={Vol} swingHigh={SwingHigh}",
                symbol, snapshot.Vwap, snapshot.VolumeSma20, snapshot.SwingHigh20);
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        var directionGate = snapshot.Ema1h21Now > snapshot.Ema1h21Prev;
        var vwapContext = snapshot.PrevBarClose < snapshot.Vwap;
        var vwapReclaim = snapshot.LastBarClose > snapshot.Vwap;
        var volumeRatio = snapshot.VolumeSma20 > 0m
            ? snapshot.LastBarVolume / snapshot.VolumeSma20
            : 0m;
        var volumeConfirm = volumeRatio >= p.VolumeMultiplier;

        if (!directionGate || !vwapContext || !vwapReclaim || !volumeConfirm)
        {
            _logger.LogDebug(
                "VwapEma skip symbol={Symbol} directionGate={Up} vwapContext={Below} reclaim={Reclaim} " +
                "volumeRatio={Ratio} decision=Skip",
                symbol, directionGate, vwapContext, vwapReclaim, volumeRatio);
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        // ADR-0015 §15.3. Stop = entry × (1 − stopPct); TP = swingHigh20 × tpSafetyFactor.
        // Entry price is left null here — the fan-out handler re-reads BookTicker ask/bid
        // and forwards the effective entry to the sizing service (so mode-dependent slip
        // propagation still works).
        var entryPrice = snapshot.LastBarClose;
        var stopPrice = entryPrice * (1m - p.StopPct);
        var takeProfit = snapshot.SwingHigh20 * p.TpSafetyFactor;

        // Guard rail: TP must be above entry and stop must be below entry. Either
        // violation means the snapshot contradicts itself (e.g. swingHigh20 fell below
        // entry after a flash move) — skip the signal.
        if (takeProfit <= entryPrice || stopPrice >= entryPrice)
        {
            _logger.LogDebug(
                "VwapEma geometry invalid symbol={Symbol} entry={Entry} stop={Stop} tp={Tp}",
                symbol, entryPrice, stopPrice, takeProfit);
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        var ctx = EvaluatorParameterHelper.SerializeContext(new
        {
            type = "vwap-ema-hybrid",
            vwap = snapshot.Vwap,
            ema1h21Now = snapshot.Ema1h21Now,
            ema1h21Prev = snapshot.Ema1h21Prev,
            prevBarClose = snapshot.PrevBarClose,
            lastBarClose = snapshot.LastBarClose,
            volumeRatio,
            swingHigh20 = snapshot.SwingHigh20,
            stopPct = p.StopPct,
            maxHoldMinutes = p.MaxHoldMinutes,
        });

        _logger.LogInformation(
            "VwapEma emit symbol={Symbol} entry={Entry} stop={Stop} tp={Tp} volumeRatio={Ratio}",
            symbol, entryPrice, stopPrice, takeProfit, volumeRatio);

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

    /// <summary>
    /// Parameters consumed from <c>Strategies.Seed[].ParametersJson</c> (ADR-0015 §15.1).
    /// Per-symbol overrides (e.g. XRP tighter volume multiplier) are encoded in the seed;
    /// defaults below cover the happy path for BTC/BNB/XRP spot.
    /// </summary>
    private sealed class Parameters
    {
        public decimal StopPct { get; set; } = 0.008m;
        public decimal VolumeMultiplier { get; set; } = 1.2m;
        public int SwingLookback { get; set; } = 20;
        public decimal TpSafetyFactor { get; set; } = 0.95m;
        public int MaxHoldMinutes { get; set; } = 15;
        public int EmaPeriod { get; set; } = 21;
        public string EmaTimeframe { get; set; } = "1h";
        public int VwapWindowBars { get; set; } = 1440;
    }
}
