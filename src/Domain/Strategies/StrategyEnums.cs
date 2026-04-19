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
/// </summary>
public enum StrategyType
{
    VwapEmaHybrid = 1,
}

public enum StrategySignalDirection
{
    Long = 1,
    Short = 2,
    Exit = 3,
}
