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

/// <summary>
/// Loop 76 — trailing-stop tick outcome. Returned by
/// <see cref="Position.UpdatePeakAndCheckTrailing"/> so the caller
/// (MarkToMarketWorker) stays exception-free for the expected three branches
/// of trailing flow (BE not yet applied → not eligible, mark made a new high
/// → peak refreshed, mark dropped past trail → exit dispatch). Combo with
/// <see cref="MoveStopResult"/>: BE move runs first and "arms" trailing;
/// once <see cref="Position.BreakEvenAppliedAt"/> is non-null this method
/// can transition out of <see cref="NotEligible"/>.
/// </summary>
public enum TrailingResult
{
    /// <summary>BE move not yet applied — trailing dormant, no peak update either.</summary>
    NotEligible = 1,
    /// <summary>Mark made a new high — <see cref="Position.PeakMarkPrice"/> updated, no exit.</summary>
    PeakUpdated = 2,
    /// <summary>Mark fell below <c>peak × (1 - trailPct)</c> — caller dispatches close.</summary>
    ExitTriggered = 3,
}
