using BinanceBot.Domain.MarketData;

namespace BinanceBot.Application.Strategies.Patterns;

/// <summary>
/// ADR-0014 §14.2: detector contract — pure synchronous calculation over closed bars.
/// One detector per pattern (open/closed: new pattern = new class + 1 DI line).
/// Implementations live in <c>Infrastructure/Strategies/Patterns/Detectors/</c>.
/// </summary>
public interface IPatternDetector
{
    PatternType Type { get; }

    /// <summary>
    /// Static (Loop 16) Bulkowski/altFINS/Luxalgo win-rate weight; consumed by the
    /// evaluator for weighted vote. Loop 17+ may swap this for adaptive weights
    /// pulled from a stats provider.
    /// </summary>
    decimal Weight { get; }

    /// <summary>
    /// Examine the latest closed bars and decide whether the pattern is present.
    /// Returns <c>null</c> when the pattern is absent or any of the per-pattern
    /// filters (volume, RSI, S/R proximity, ...) reject the candidate.
    /// </summary>
    PatternResult? Detect(IReadOnlyList<Kline> closedBars);
}

/// <summary>
/// Single-pattern detection outcome (ADR-0014 §14.2). Carries the pattern's
/// own stop / TP / max-hold proposal — the evaluator picks the leader and
/// forwards these into the order/position pipeline.
/// </summary>
public sealed record PatternResult(
    PatternType Type,
    PatternDirection Direction,
    decimal Confidence,
    decimal EntryPrice,
    decimal StopPrice,
    decimal TakeProfit,
    int MaxHoldBars,
    string ContextJson);

public enum PatternDirection
{
    Long = 1,
    Short = 2,
}

/// <summary>
/// 14 statically-weighted patterns implemented in Loop 16 (ADR-0014 §14.3).
/// Numbers stable for storage compatibility (currently transient — written into
/// <c>StrategySignal.ContextJson</c> only).
/// </summary>
public enum PatternType
{
    DoubleBottom = 1,
    DoubleTop = 2,
    ThreeWhiteSoldiers = 3,
    ThreeBlackCrows = 4,
    MorningStar = 5,
    EveningStar = 6,
    BullFlag = 7,
    BearFlag = 8,
    AscendingTriangle = 9,
    DescendingTriangle = 10,
    Hammer = 11,
    ShootingStar = 12,
    BullishEngulfing = 13,
    BearishEngulfing = 14,
}
