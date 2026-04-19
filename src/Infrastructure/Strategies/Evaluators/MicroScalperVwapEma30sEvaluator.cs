using BinanceBot.Application.Strategies.Evaluation;
using BinanceBot.Application.Strategies.Indicators;
using BinanceBot.Domain.MarketData;
using BinanceBot.Domain.Strategies;
using Microsoft.Extensions.Logging;

namespace BinanceBot.Infrastructure.Strategies.Evaluators;

/// <summary>
/// ADR-0018 §18.3 + §18.7 + §18.8 — 30sn kline VWAP reclaim + EMA20 slope +
/// volume spike micro-scalper. BNB fee discount ve 2 dk TimeStop ile +0.60% TP
/// / −0.35% SL geometrisi. Loop 23 ile <see cref="VwapEmaStrategyEvaluator"/>'ı
/// strateji katmanında supersede eder; eski evaluator deprecated durumda ama
/// backward-compat için silinmez (enum ordinal korunur).
///
/// Giriş (<see cref="StrategySignalDirection.Long"/>, kapalı 30sn bar sonrası):
///   1. Direction gate  — <c>emaNow &gt; emaPrev × (1 + SlopeTolerance)</c>.
///      Default <c>SlopeTolerance = 0</c> → strict positive slope.
///   2. VWAP context    — önceki bar kapanışı VWAP altında (pullback).
///   3. VWAP reclaim    — son bar kapanışı VWAP üstünde (strict reclaim;
///      <c>VwapTolerancePct = 0</c> → zone yok).
///   4. Volume confirm  — <c>lastVolume &gt;= volumeSma20 × VolumeMultiplier</c>.
///      Default multiplier 1.5 (spam azaltma).
///
/// Çıkış:
///   - Take-Profit: <c>entry × (1 + TpGrossPct)</c> (default 0.006 → +0.60%).
///   - Stop-Loss  : <c>entry × (1 − StopPct)</c>     (default 0.0035 → −0.35%).
///   - Time-Stop  : <c>MaxHoldMinutes = 2</c> — ContextJson <c>maxHoldMinutes</c>
///     anahtarına yazılır (ADR-0017 §17.7 handler ile tam uyumlu).
///
/// Evaluator hiçbir DB bağlantısı açmaz, rolling buffer yönetmez — tüm primitive
/// verileri <see cref="IMarketIndicatorService.TryGetMicroScalperSnapshot"/>
/// üzerinden alır.
/// </summary>
public sealed class MicroScalperVwapEma30sEvaluator : IStrategyEvaluator
{
    public StrategyType Type => StrategyType.MicroScalperVwapEma30s;

    private readonly IMarketIndicatorService _indicators;
    private readonly ILogger<MicroScalperVwapEma30sEvaluator> _logger;

    public MicroScalperVwapEma30sEvaluator(
        IMarketIndicatorService indicators,
        ILogger<MicroScalperVwapEma30sEvaluator> logger)
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

        var snapshot = _indicators.TryGetMicroScalperSnapshot(symbol);
        if (snapshot is null)
        {
            _logger.LogDebug(
                "MicroScalper snapshot not ready symbol={Symbol} strategyId={StrategyId}",
                symbol, strategyId);
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        // Pre-warmup safety — service returns null already, but we guard against
        // degenerate zero values from empty-volume windows (Indicators.Vwap => 0
        // when cumulative volume is 0).
        if (snapshot.Vwap <= 0m
            || snapshot.VolumeSma20 <= 0m
            || snapshot.Ema20Now <= 0m
            || snapshot.Ema20Prev <= 0m)
        {
            _logger.LogDebug(
                "MicroScalper snapshot incomplete symbol={Symbol} vwap={Vwap} volSma={Vol} ema={Ema}",
                symbol, snapshot.Vwap, snapshot.VolumeSma20, snapshot.Ema20Now);
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        // ADR-0018 §18.8 — direction gate: EMA20 slope strictly positive when
        // tolerance = 0. Non-zero tolerance allows a defined slack window without
        // changing the default behaviour.
        var slopeGateLevel = snapshot.Ema20Prev * (1m + p.SlopeTolerance);
        var directionGate = snapshot.Ema20Now > slopeGateLevel;

        // ADR-0018 §18.8 — prev bar below VWAP (pullback context).
        var vwapContext = snapshot.PrevBarClose < snapshot.Vwap;

        // ADR-0018 §18.8 — strict reclaim (VwapTolerancePct = 0 default);
        // non-zero tolerance turns this into a zone check matching the legacy
        // VwapEma evaluator semantics.
        var vwapAboveReclaim = snapshot.LastBarClose > snapshot.Vwap;
        var vwapDistance = snapshot.Vwap > 0m
            ? Math.Abs(snapshot.LastBarClose - snapshot.Vwap) / snapshot.Vwap
            : decimal.MaxValue;
        var vwapZoneOk = p.VwapTolerancePct > 0m && vwapDistance < p.VwapTolerancePct;
        var vwapReclaim = vwapAboveReclaim || vwapZoneOk;

        // ADR-0018 §18.8 — volume spike confirmation (SMA × 1.5 default).
        var volumeRatio = snapshot.VolumeSma20 > 0m
            ? snapshot.LastBarVolume / snapshot.VolumeSma20
            : 0m;
        var volumeConfirm = volumeRatio >= p.VolumeMultiplier;

        if (!directionGate || !vwapContext || !vwapReclaim || !volumeConfirm)
        {
            var slope = snapshot.Ema20Prev > 0m
                ? (snapshot.Ema20Now - snapshot.Ema20Prev) / snapshot.Ema20Prev
                : 0m;
            _logger.LogDebug(
                "MicroScalper skip symbol={Symbol} slope={Slope} slopeGate={GateLevel} " +
                "vwapCtx={Below} reclaim={Reclaim} vwapZoneOk={ZoneOk} volRatio={Ratio} decision=Skip",
                symbol, slope, slopeGateLevel,
                vwapContext, vwapAboveReclaim, vwapZoneOk, volumeRatio);
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        // ADR-0018 §18.9 — TP/SL geometry.
        var entryPrice = snapshot.LastBarClose;
        var stopPrice = entryPrice * (1m - p.StopPct);
        var takeProfit = entryPrice * (1m + p.TpGrossPct);

        if (takeProfit <= entryPrice || stopPrice >= entryPrice)
        {
            _logger.LogDebug(
                "MicroScalper geometry invalid symbol={Symbol} entry={Entry} stop={Stop} tp={Tp}",
                symbol, entryPrice, stopPrice, takeProfit);
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        var slopeRatio = snapshot.Ema20Prev > 0m
            ? (snapshot.Ema20Now - snapshot.Ema20Prev) / snapshot.Ema20Prev
            : 0m;

        // ADR-0018 §18.16 — ContextJson payload. `maxHoldMinutes` key ADR-0017 §17.7
        // handler ile tam uyumlu — TimeStop pipeline dokunulmaz.
        var ctx = EvaluatorParameterHelper.SerializeContext(new
        {
            type = "micro-scalper-vwap-ema-30s",
            vwap = snapshot.Vwap,
            ema20_30sNow = snapshot.Ema20Now,
            ema20_30sPrev = snapshot.Ema20Prev,
            slope = slopeRatio,
            prevBarClose = snapshot.PrevBarClose,
            lastBarClose = snapshot.LastBarClose,
            vwapDistance,
            volumeRatio,
            stopPct = p.StopPct,
            tpGrossPct = p.TpGrossPct,
            maxHoldMinutes = p.MaxHoldMinutes,
        });

        _logger.LogInformation(
            "MicroScalper emit symbol={Symbol} entry={Entry} stop={Stop} tp={Tp} " +
            "slope={Slope} vwap={Vwap} volRatio={Ratio} decision=Emit",
            symbol, entryPrice, stopPrice, takeProfit, slopeRatio, snapshot.Vwap, volumeRatio);

        // SuggestedQuantity is a placeholder — fan-out handler overrides via sizing
        // service. A positive-but-tiny value satisfies the StrategySignal.Emit
        // domain invariant (quantity > 0); the real trade quantity is computed at
        // fan-out time using equity × 1% floor-$5.10 rule plus LOT_SIZE/NOTIONAL
        // filters (ADR-0018 §18.10).
        return Task.FromResult<StrategyEvaluation?>(new StrategyEvaluation(
            Direction: StrategySignalDirection.Long,
            SuggestedQuantity: 0.00000001m,
            SuggestedPrice: entryPrice,
            SuggestedStopPrice: stopPrice,
            ContextJson: ctx,
            SuggestedTakeProfit: takeProfit));
    }

    /// <summary>
    /// ADR-0018 §18.7 parameter contract. Serialised as
    /// <c>Strategies.Seed[].ParametersJson</c>. All 4 MicroScalper seeds
    /// (BTC/ETH/BNB/XRP) carry the same payload; per-symbol override fields
    /// are intentionally absent — research §5.2 shows the parameter set is
    /// symbol-agnostic on 30s bars.
    /// </summary>
    private sealed class Parameters
    {
        public string KlineInterval { get; set; } = "30s";
        public string EmaTimeframe { get; set; } = "30s";
        public int EmaPeriod { get; set; } = 20;

        public int VwapWindowBars { get; set; } = 15;
        public decimal VwapTolerancePct { get; set; } = 0m;

        public int VolumeSmaBars { get; set; } = 20;
        public decimal VolumeMultiplier { get; set; } = 1.5m;

        public decimal SlopeTolerance { get; set; } = 0m;

        public decimal TpGrossPct { get; set; } = 0.006m;
        public decimal StopPct { get; set; } = 0.0035m;
        public int MaxHoldMinutes { get; set; } = 2;
    }
}
