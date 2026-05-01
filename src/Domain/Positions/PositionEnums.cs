namespace BinanceBot.Domain.Positions;

public enum PositionStatus
{
    Open = 1,
    Closed = 2,
}

public enum PositionSide
{
    Long = 1,
    Short = 2,
}

/// <summary>
/// Loop 75 — break-even SL move outcome. Result-pattern enum so the caller
/// stays exception-free for expected idempotency / no-improve paths
/// (CLAUDE.md root rule #5: no throwing for control flow).
/// </summary>
public enum MoveStopResult
{
    /// <summary>BE move applied this call — StopPrice updated, audit event raised.</summary>
    Applied = 1,
    /// <summary>Already applied earlier (BreakEvenAppliedAt is set) — no-op.</summary>
    AlreadyApplied = 2,
    /// <summary>Requested stop is not strictly better than current — no-op (would degrade risk).</summary>
    NotImproving = 3,
}
