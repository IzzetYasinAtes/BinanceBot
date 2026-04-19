namespace BinanceBot.Application.Strategies.Indicators;

/// <summary>
/// ADR-0015 §15.6. Evaluator-facing port for pre-computed market indicators.
/// Infrastructure impl maintains rolling 1m + 1h buffers and exposes a snapshot
/// per-symbol when warmup completes. Evaluator is snapshot-consumer only — it
/// does not own the rolling buffer, the Channel subscription, or the REST backfill
/// wiring.
/// </summary>
public interface IMarketIndicatorService
{
    /// <summary>
    /// Returns the most recent closed-bar indicator snapshot for <paramref name="symbol"/>,
    /// or <c>null</c> when the warmup (1440 × 1m + 21 × 1h) is incomplete.
    /// </summary>
    MarketIndicatorSnapshot? TryGetSnapshot(string symbol);
}
