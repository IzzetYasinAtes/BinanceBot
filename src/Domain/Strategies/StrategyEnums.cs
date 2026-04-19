namespace BinanceBot.Domain.Strategies;

public enum StrategyStatus
{
    Draft = 1,
    Paused = 2,
    Active = 3,
}

/// <summary>
/// ADR-0015 Loop 19: strategy surface reset. ADR-0014 <c>PatternScalping</c> is
/// superseded after Loop 16-19 produced a -$0.39 net realised PnL across 16 trades
/// (100% time-stop exit rate, 0% TP hit rate). The 14 pattern-detector family and
/// its orchestrator <c>PatternScalpingEvaluator</c> are deleted; <c>VwapEmaHybrid</c>
/// re-uses enum value <c>1</c> and is implemented by <c>VwapEmaStrategyEvaluator</c>
/// — a single-strategy 1m VWAP reclaim + 1h EMA21 trend-gate. DB wipe is performed by
/// migration <c>Loop19VwapEmaHybridReset</c> so the reused numeric value is safe.
///
/// ADR-0018 §18.6 Loop 23: <see cref="MicroScalperVwapEma30s"/> value <c>2</c> added.
/// 30sn kline tabanlı micro-scalper (TP 0.60% / SL 0.35% / 2dk TimeStop, BNB fee
/// discount). <see cref="VwapEmaHybrid"/> enum ordinali korunur (Loop 19 pattern) —
/// DB reseed ile eski seed'ler silinir, kod yüzeyinde <c>VwapEmaStrategyEvaluator</c>
/// deprecated olarak kalır (backward-compat ParametersJson).
/// </summary>
public enum StrategyType
{
    VwapEmaHybrid = 1,

    /// <summary>
    /// ADR-0018 §18.6 — 30sn kline VWAP reclaim + EMA20 slope + volume 1.5× filtresi.
    /// Evaluator: <c>MicroScalperVwapEma30sEvaluator</c>.
    /// </summary>
    MicroScalperVwapEma30s = 2,
}

public enum StrategySignalDirection
{
    Long = 1,
    Short = 2,
    Exit = 3,
}
