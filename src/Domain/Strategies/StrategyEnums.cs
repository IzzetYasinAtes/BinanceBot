namespace BinanceBot.Domain.Strategies;

public enum StrategyStatus
{
    Draft = 1,
    Paused = 2,
    Active = 3,
}

/// <summary>
/// ADR-0014 Loop 16: strategy surface reset. The historical Grid / TrendFollowing /
/// MeanReversion trio is removed (ADR-0012 §12.5/§12.6/§12.7 superseded). Only
/// <see cref="PatternScalping"/> remains; it is the umbrella for the 14
/// <see cref="Application.Strategies.Patterns.IPatternDetector"/> implementations
/// orchestrated by <c>PatternScalpingEvaluator</c>.
/// </summary>
public enum StrategyType
{
    PatternScalping = 1,
}

public enum StrategySignalDirection
{
    Long = 1,
    Short = 2,
    Exit = 3,
}
