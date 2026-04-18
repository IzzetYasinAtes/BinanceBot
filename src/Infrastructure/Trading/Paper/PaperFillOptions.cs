namespace BinanceBot.Infrastructure.Trading.Paper;

/// <summary>
/// Configuration for the deterministic Paper fill simulator.
/// Bound from configuration section "PaperFill". See ADR-0011 §11.5 + decision-sizing.md Commit 1.
///
/// Slippage is applied **only** to Paper fills (Live testnet/mainnet use real exchange prices).
/// </summary>
public sealed record PaperFillOptions
{
    /// <summary>
    /// Fixed proportional slippage applied to each MARKET-fill leg.
    /// BUY pays <c>price * (1 + pct)</c>, SELL receives <c>price * (1 - pct)</c>.
    /// Default 5 bps (0.0005) — see paper-fill-research.md.
    /// </summary>
    public decimal FixedSlippagePct { get; init; } = 0.0005m;
}
