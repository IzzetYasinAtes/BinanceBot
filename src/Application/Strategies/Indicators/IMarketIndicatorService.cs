namespace BinanceBot.Application.Strategies.Indicators;

/// <summary>
/// ADR-0015 §15.6 + ADR-0018 §18.11. Evaluator-facing port for pre-computed market
/// indicators. Infrastructure impl maintains rolling 1m + 1h + 30s buffers and
/// exposes a snapshot per-symbol when warmup completes. Evaluator is snapshot-consumer
/// only — it does not own the rolling buffer, the Channel subscription, or the REST
/// backfill wiring.
/// </summary>
public interface IMarketIndicatorService
{
    /// <summary>
    /// ADR-0015 §15.6. Returns the most recent closed-bar indicator snapshot
    /// (1m VWAP + 1h EMA21 + SwingHigh20) for <paramref name="symbol"/>, or
    /// <c>null</c> when the warmup (1440 × 1m + 21 × 1h) is incomplete.
    /// Eski VwapEma path'i; Loop 23+ seed reset sonrası çağırılmaz.
    /// </summary>
    MarketIndicatorSnapshot? TryGetSnapshot(string symbol);

    /// <summary>
    /// ADR-0018 §18.11. Returns the 30sn bar bazında micro-scalper snapshot
    /// (rolling 15-bar VWAP + EMA20 now/prev + VolumeSMA20) for
    /// <paramref name="symbol"/>, or <c>null</c> when the 30s warmup (en az 21 bar)
    /// tamamlanmadı. EMA20 ve VolumeSMA20 için 20 bar gerekli; 15-bar VWAP + slope
    /// için 21 bar emniyet eşiği.
    /// </summary>
    MicroScalperIndicatorSnapshot? TryGetMicroScalperSnapshot(string symbol);
}
