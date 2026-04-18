namespace BinanceBot.Infrastructure.Trading.Paper;

/// <summary>
/// Configuration for the deterministic Paper fill simulator.
/// Bound from configuration section "PaperFill". See ADR-0011 §11.5 + decision-sizing.md Commit 1.
///
/// Slippage is applied **only** to Paper fills (Live testnet/mainnet use real exchange prices).
///
/// Loop 14 (research-paper-live-and-sizing.md §A2): live BTC/BNB/XRP L1 spread
/// measured at 0.01-1.58 bps and the academic TCA benchmark (Anboto, 60K orders)
/// shows -0.58 bps mean slippage, so the previous 5 bps default was 5x reality.
/// Default lowered to 1 bps (0.0001) — still conservative but no longer punitive
/// against the real venue.
/// </summary>
public sealed record PaperFillOptions
{
    /// <summary>
    /// Fixed proportional slippage applied to each MARKET-fill leg.
    /// BUY pays <c>price * (1 + pct)</c>, SELL receives <c>price * (1 - pct)</c>.
    /// Default 1 bp (0.0001) — see Loop 14 research-paper-live-and-sizing.md §A2.
    /// </summary>
    public decimal FixedSlippagePct { get; init; } = 0.0001m;

    /// <summary>
    /// Synthetic fill latency in milliseconds applied at the head of <c>SimulateAsync</c>
    /// (ADR-0012 §12.9). Approximates the round-trip + matching delay of a mainnet MARKET
    /// (~80-120ms). Tests should set this to 0 to keep suite times tight.
    /// </summary>
    public int SimulatedLatencyMs { get; init; } = 100;

    /// <summary>
    /// Loop 14 (research-paper-live-and-sizing.md §A4): when true, taker fees are charged
    /// at the BNB-discounted VIP0 rate (0.075% vs 0.10% standard). Defaults to <c>false</c>
    /// in V1 — Paper stays on the conservative non-discounted schedule until the live
    /// account confirms BNB is held + the discount toggle is on. Flag-only for now;
    /// fee computation wiring is downstream.
    /// </summary>
    public bool UseBnbFeeDiscount { get; init; } = false;
}
