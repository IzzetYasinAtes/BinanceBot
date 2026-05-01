namespace BinanceBot.Domain.Strategies;

public enum StrategyStatus
{
    Draft = 1,
    Paused = 2,
    Active = 3,
}

/// <summary>
/// Loop 67 KMS pivot — strateji yüzeyi tek değere indirgendi
/// (<see cref="KlineMomentumSpread5m"/>). Önceki tüm değerler
/// (VwapEmaHybrid / MicroScalperVwapEma30s / AtrScalperVwapEma1m /
/// DonchianBreakout15m / BbMeanReversion15m / EmaScalper1m /
/// HybridMomentum1m) silindi; <c>Loop67KmsReset</c> migration'ı veritabanı
/// state'ini sıfırlar (Strategies + StrategySignals + Positions + Orders +
/// OrderFills full delete) böylece reused enum ordinali için risk yok.
///
/// Yeni bir strateji eklendiğinde değer 2'den başlar — KMS rezervi 1'dir.
/// </summary>
public enum StrategyType
{
    /// <summary>
    /// Loop 67 — KlineMomentumSpread5m. Long-only spot paper. 5m bar
    /// kapanışında 5 AND koşul:
    /// <list type="number">
    ///   <item>RSI Recovery — <c>Rsi14 &gt; threshold AND Rsi14Prev &lt; threshold</c>
    ///         (önceki bar oversold, mevcut çıkış).</item>
    ///   <item>EMA9 pozitif slope — <c>Ema9Now &gt; Ema9Prev</c>.</item>
    ///   <item>TradeCount surge — <c>currentTradeCount &gt; avgTradeCount20 × N</c>.</item>
    ///   <item>Spread filter — <c>(Ask-Bid)/Ask &lt; threshold</c> (canlı
    ///         BookTicker okuması).</item>
    ///   <item>MinAtrPct — <c>Atr14 / close &gt;= threshold</c> (sessiz
    ///         piyasa engeli).</item>
    /// </list>
    /// Çıkış: ATR-tabanlı dinamik TP/SL (TP <c>1.8×ATR</c> clip [%0.5, %1.8],
    /// SL <c>0.75×ATR</c> clip [%0.3, %0.8]); MaxHold 45dk (9 bar × 5m);
    /// per-coin cooldown 3 bar (15dk). Evaluator:
    /// <c>KmsMomentumEvaluator</c>. Snapshot:
    /// <c>KmsMomentumSnapshot</c> 5m rolling buffer'dan.
    /// </summary>
    KlineMomentumSpread5m = 1,

    /// <summary>
    /// Loop 79 — Bollinger Band Reversal scalper (5m). Long-only spot paper.
    /// Multi-regime switch'in <b>Range</b> ayağı: KMS (Trending) ile paralel
    /// çalışır, fakat farklı rejim koşullarında emit verir. binance-expert spec
    /// (loop 79): 8 saatlik cumulative -%1.11 sonrası dead-range pazarda
    /// oversold-recovery (KMS) tek başına yetersiz; mean-reversion ekleyerek
    /// "boğa ucuz al, yan band'da sat" geometrisini açıyoruz.
    ///
    /// Entry (AND, 5m bar kapanışında):
    /// <list type="number">
    ///   <item>BBW ∈ [<c>BbwRangeMin</c>, <c>BbwRangeMax</c>] (range regime —
    ///         dead/trending olduğunda skip).</item>
    ///   <item><c>close &lt; BB_Lower × (1 + BufferPctEntry)</c> (alt band'a
    ///         yakın, %0.05 buffer ile false breakdown koruma).</item>
    ///   <item><c>RSI14 &lt; RsiOversoldEntry</c> (oversold).</item>
    ///   <item><c>RSI14 &gt; RSI14Prev</c> (RSI dip yapıp dönüyor — recovery).</item>
    ///   <item>Spread filter — <c>(Ask-Bid)/Ask &lt; SpreadThresholdPct</c>.</item>
    ///   <item>Cooldown — son <c>CooldownBarsAfterSignal</c> bar BB Reversal yok.</item>
    /// </list>
    ///
    /// Çıkış: TP = <c>BB_Middle</c> (orta band, 20-bar SMA — mean reversion
    /// hedefi); SL = <c>entryPrice × (1 - BufferPctExit)</c> (~%0.1 false
    /// breakout koruma); MaxHold <c>MaxHoldMinutes</c> (default 20dk = 4 bar).
    /// Evaluator: <c>BbReversalEvaluator</c>. Snapshot:
    /// <c>BbReversalSnapshot</c> 5m rolling buffer'dan (KMS ile aynı buffer).
    /// </summary>
    BollingerBandReversal5m = 2,
}

public enum StrategySignalDirection
{
    Long = 1,
    Short = 2,
    Exit = 3,
}
