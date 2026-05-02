namespace BinanceBot.Application.Strategies.Indicators;

/// <summary>
/// Loop 67 KMS pivot — evaluator-facing port for pre-computed market
/// indicators. Infrastructure impl owns rolling per-(symbol, interval)
/// buffers and exposes a snapshot per strategy. Evaluator is snapshot-
/// consumer only: it does not own the buffer, the WS subscription, or the
/// REST backfill wiring.
///
/// Surface reset (Loop 67): all legacy <c>TryGet*</c> methods of the prior
/// 7-strategy era (VwapEma / MicroScalper / AtrScalper / Donchian / BbMeanRev
/// / EmaScalper / HybridMomentum) were deleted in favour of the single KMS
/// snapshot below. Backward-compat is intentionally NOT preserved — the
/// migration <c>Loop67KmsReset</c> wipes all legacy strategy/seed state in
/// the same release.
/// </summary>
public interface IMarketIndicatorService
{
    /// <summary>
    /// Loop 67 KMS — KlineMomentumSpread5m snapshot from the 5m rolling
    /// buffer. Computes RSI(period) at <c>Count-1</c> and <c>Count-2</c>
    /// (oversold-recovery cross), EMA(emaPeriod) now/prev (slope), ATR
    /// (atrPeriod), and <c>TradeCountAvg(tradeCountWindow)</c> + current
    /// bar trade count.
    ///
    /// Warmup threshold: <c>max(rsiPeriod + 2, emaPeriod + 1, atrPeriod + 1, tradeCountWindow)</c>
    /// — the +2 on RSI is required because <c>Rsi14Prev</c> is computed by
    /// shifting the window one bar back. Returns <c>null</c> when warmup is
    /// incomplete, the symbol is not tracked, or any parameter &lt;= 0.
    /// </summary>
    KmsMomentumSnapshot? TryGetKmsMomentumSnapshot(
        string symbol,
        int rsiPeriod,
        int emaPeriod,
        int atrPeriod,
        int tradeCountWindow);

    /// <summary>
    /// Loop 79 — BollingerBandReversal5m snapshot. KMS ile aynı 5m rolling
    /// buffer üzerinden hesaplanır; additive surface (KMS'i etkilemez).
    /// Hesaplananlar:
    ///   - RSI(<paramref name="rsiPeriod"/>) curr (<c>Count-1</c>) ve prev
    ///     (<c>Count-2</c>) — Wilder; pencere bir bar geriye kaydırılır.
    ///   - BB(<paramref name="bbPeriod"/>, <paramref name="bbStdDev"/>) — Lower,
    ///     Middle, BBW = (Upper - Lower) / Middle.
    ///   - ATR(<paramref name="atrPeriod"/>) — bilgi amaçlı (audit + future varyant).
    ///   - Loop 80 — TradeCountAvg(<paramref name="tradeCountWindow"/>) +
    ///     <c>CurrentTradeCount</c>: BBR volume-surge gate referansı.
    ///
    /// Warmup eşiği: <c>max(rsiPeriod + 2, bbPeriod, atrPeriod + 1, tradeCountWindow)</c>.
    /// Returns <c>null</c> when warmup incomplete, symbol untracked, or any
    /// parameter ≤ 0.
    /// </summary>
    BbReversalSnapshot? TryGetBbReversalSnapshot(
        string symbol,
        int rsiPeriod,
        int bbPeriod,
        decimal bbStdDev,
        int atrPeriod,
        int tradeCountWindow);
}
