namespace BinanceBot.Infrastructure.Strategies;

/// <summary>
/// ADR-0015 §15.4. Pure helper for the "snowball" minimum notional rule the user
/// mandated for Loop 19: each entry risks at most max(equity × 20%, $20). The
/// fan-out handler forwards the result as <c>MinNotional</c> into the sizing
/// service so the cap/risk branches clamp against it.
///
/// Split out for unit-test reach — `StrategySignalToOrderHandler` is an
/// integration-style orchestrator; the invariant itself is a one-liner.
/// </summary>
internal static class SnowballSizing
{
    /// <summary>Fixed floor in USDT — applies when equity × 20% drops under $20.</summary>
    public const decimal FloorUsd = 20.0m;

    /// <summary>User-chosen equity fraction (20%).</summary>
    public const decimal EquityFraction = 0.20m;

    /// <summary>
    /// Returns the user-level minimum notional for the current equity snapshot.
    /// Non-positive equity collapses to the <see cref="FloorUsd"/> floor —
    /// callers still apply the exchange NOTIONAL filter on top.
    /// </summary>
    public static decimal CalcMinNotional(decimal equity)
    {
        if (equity <= 0m)
        {
            return FloorUsd;
        }
        var pct = equity * EquityFraction;
        return pct >= FloorUsd ? pct : FloorUsd;
    }
}
