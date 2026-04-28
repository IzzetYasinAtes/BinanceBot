namespace BinanceBot.Domain.Strategies;

public enum StrategyStatus
{
    Draft = 1,
    Paused = 2,
    Active = 3,
}

/// <summary>
/// ADR-0015 Loop 19: strategy surface reset. ADR-0014 <c>PatternScalping</c> is
/// superseded after Loop 16-19 produced a -$0.39 net realised PnL across 16 trades
/// (100% time-stop exit rate, 0% TP hit rate). The 14 pattern-detector family and
/// its orchestrator <c>PatternScalpingEvaluator</c> are deleted; <c>VwapEmaHybrid</c>
/// re-uses enum value <c>1</c> and is implemented by <c>VwapEmaStrategyEvaluator</c>
/// — a single-strategy 1m VWAP reclaim + 1h EMA21 trend-gate. DB wipe is performed by
/// migration <c>Loop19VwapEmaHybridReset</c> so the reused numeric value is safe.
///
/// ADR-0018 §18.6 Loop 23: <see cref="MicroScalperVwapEma30s"/> value <c>2</c> added.
/// 30sn kline tabanlı micro-scalper (TP 0.60% / SL 0.35% / 2dk TimeStop, BNB fee
/// discount). <see cref="VwapEmaHybrid"/> enum ordinali korunur (Loop 19 pattern) —
/// DB reseed ile eski seed'ler silinir, kod yüzeyinde <c>VwapEmaStrategyEvaluator</c>
/// deprecated olarak kalır (backward-compat ParametersJson).
///
/// Loop 33 AR-GE Strateji D: <see cref="AtrScalperVwapEma1m"/> value <c>3</c> added.
/// 1m kline VWAP reclaim + EMA20 slope + ATR14 çarpanlı dinamik TP/SL — fee/gross
/// oranını yüksek-volatiliteli semboller (SOL, ADA) üzerinden %85 yerine %24'e
/// indirir. MicroScalper ile aynı filtre geometrisi; tek fark TP/SL ATR-adaptif.
///
/// Loop 41 AR-GE: <see cref="DonchianBreakout15m"/> value <c>4</c> added. 15m kline
/// Donchian 20-bar üst kırılım + Volume Z-Score &gt; 1.5 filtresi. ATR14 tabanlı
/// dinamik TP/SL (TP 2.0×ATR clip [%0.5, %1.2], SL 0.65×ATR clip [%0.2, %0.5]),
/// MaxHold 90dk, BE WR ~%36.5 hedef. AtrScalperVwapEma1m enum ordinali korunur
/// (Activate=false ile DB'de yedekte kalır), kod yüzeyinde evaluator class'ı
/// silinmez — backward-compat (loop rollback için referans).
///
/// Loop 44 AR-GE: <see cref="BbMeanReversion15m"/> value <c>5</c> added. 15m kline
/// Bollinger Bands lower kapışı + RSI oversold (&lt; 30) + Volume Z-Score &gt; 1.0
/// (panik hacmi) + MinAtrPct filtresi. ATR14 tabanlı dinamik TP/SL (TP 1.5×ATR
/// clip [%0.4, %1.0], SL 1.0×ATR clip [%0.3, %0.6]), MaxHold 90dk. Loop 41-42-43
/// DonchianBreakout %0 WR sonrası counter-trend pivot (binance-expert spec onaylı).
/// DonchianBreakout15m enum ordinali korunur (Activate=false ile yedekte), kod
/// yüzeyinde evaluator class'ı silinmez — backward-compat (loop rollback için).
///
/// Loop 46 AR-GE: <see cref="EmaScalper1m"/> value <c>6</c> added. 1m kline
/// EMA9/EMA21 crossover scalper + RSI14 nötr aralık (40-65) + gevşek hacim teyidi
/// + MinAtrPct filtresi. ATR14 tabanlı dinamik TP/SL (TP 1.5×ATR clip
/// [%0.30, %0.80], SL 0.8×ATR clip [%0.20, %0.50] — R:R 1.875:1, BE WR ~%34.8),
/// MaxHold 8dk (8 bar × 1m), per-coin 2 bar (2dk) cooldown. Loop 45 BbMeanRev15m
/// 6h'da 2 trade düşük frekans tespiti sonrası işlem hacmi pivot (kullanıcı talebi
/// 20-30 trade/saat). BbMeanReversion15m enum ordinali korunur (Activate=false
/// ile yedekte), kod yüzeyinde evaluator class'ı silinmez — backward-compat.
///
/// Loop 50 AR-GE: <see cref="HybridMomentum1m"/> value <c>7</c> added. Hibrit
/// kalite kapısı + frekans tetiği — 15m BB lower kapışı (kalite/dip yakalama)
/// + 15m RSI14 yukarı dönüş (momentum) + 1m EMA9/EMA21 crossover (frekans
/// tetiği) + 1m hacim sürpriz (×1.2) + 1m MinAtrPct (%0.03) + per-coin 3 bar
/// (3dk) cooldown. ATR15m tabanlı dinamik TP/SL (TP 1.5×ATR15 clip [%0.40,
/// %1.00], SL 0.8×ATR15 clip [%0.20, %0.40] — R:R 1.875:1, BE WR ~%34.8),
/// MaxHold 30dk. Long-only spot paper. Evaluator: <c>HybridMomentum1mEvaluator</c>.
/// Snapshot: <c>HybridMomentum1mIndicatorSnapshot</c> hem OneMinute hem
/// FifteenMinute buffer'ından okur (2 timeframe birleşik). Önceki tüm strateji
/// enum ordinalleri korunur (Activate=false ile yedekte) — backward-compat.
/// </summary>
public enum StrategyType
{
    VwapEmaHybrid = 1,

    /// <summary>
    /// ADR-0018 §18.6 — 30sn kline VWAP reclaim + EMA20 slope + volume 1.5× filtresi.
    /// Evaluator: <c>MicroScalperVwapEma30sEvaluator</c>.
    /// </summary>
    MicroScalperVwapEma30s = 2,

    /// <summary>
    /// Loop 33 AR-GE (Strateji D) — 1m kline VWAP reclaim + EMA20 slope + ATR14
    /// çarpanlı dinamik TP/SL. Evaluator: <c>AtrScalperVwapEmaEvaluator</c>. Yüksek
    /// volatiliteli altcoin rotasyonu (SOL, ADA) için çalışır; BTC/ETH/BNB/XRP için
    /// MicroScalperVwapEma30s geometrisi korunur.
    /// </summary>
    AtrScalperVwapEma1m = 3,

    /// <summary>
    /// Loop 41 AR-GE — 15m kline Donchian 20-bar üst kırılım + Volume Z-Score &gt; 1.5
    /// + ATR14 tabanlı TP/SL geometrisi (R:R 2.67:1, BE WR ~%36.5). Evaluator:
    /// <c>DonchianBreakoutEvaluator</c>. Long-only spot paper. Sessiz piyasa filtresi
    /// (MinAtrPct=0.0006), aynı sembolde sinyal sonrası 4 bar (60dk) cooldown.
    /// </summary>
    DonchianBreakout15m = 4,

    /// <summary>
    /// Loop 44 AR-GE — 15m kline Bollinger Bands counter-trend mean reversion.
    /// Giriş AND: <c>currentClose &lt; BbLower(20, 2σ)</c> + <c>RSI14 &lt; 30</c>
    /// + Volume Z-Score &gt; 1.0 (panik satış teyidi) + <c>ATR14/Close &gt;= 0.0007</c>.
    /// ATR14 tabanlı TP/SL (TP 1.5×ATR clip [%0.4, %1.0], SL 1.0×ATR clip
    /// [%0.3, %0.6]), MaxHold 90dk, sinyal sonrası 4 bar (60dk) cooldown.
    /// Evaluator: <c>BbMeanReversionEvaluator</c>. Long-only spot paper.
    /// </summary>
    BbMeanReversion15m = 5,

    /// <summary>
    /// Loop 46 AR-GE — 1m kline EMA9/EMA21 crossover scalper.
    /// Giriş AND: <c>EMA9 &gt; EMA21</c> (kısa-vade trend) + <c>currentClose &gt; EMA9</c>
    /// (fiyat momentum üstünde) + <c>RSI14 ∈ [40, 65]</c> (nötr) +
    /// <c>currentVolume &gt; volumeSma20 × 0.8</c> (gevşek hacim teyidi) +
    /// <c>ATR14/Close &gt;= MinAtrPct</c> (sessiz piyasa engeli) + bar kapandı.
    /// ATR14 tabanlı TP/SL (TP 1.5×ATR clip [%0.30, %0.80], SL 0.8×ATR clip
    /// [%0.20, %0.50] — R:R 1.875:1, BE WR ~%34.8), MaxHold 8dk (8 bar × 1m),
    /// per-coin sinyal sonrası 2 bar (2dk) cooldown. Long-only spot paper.
    /// Evaluator: <c>EmaScalper1mEvaluator</c>. Snapshot:
    /// <c>EmaScalperIndicatorSnapshot</c> üzerinden 1m FifteenMinute olmayan
    /// OneMinute buffer kaynağı.
    /// </summary>
    EmaScalper1m = 6,

    /// <summary>
    /// Loop 50 AR-GE — Hibrit 15m kalite kapısı + 1m frekans tetiği.
    /// Giriş AND (kapalı 15m bar sonrası, 1m bar bazında polled):
    ///   1. 15m BB lower kapışı: <c>currentClose_15m &lt; BbLower_15m(20, 2σ)</c>.
    ///   2. 15m RSI14 yukarı dönüş: <c>Rsi14_15m_curr &lt; 40 AND
    ///      Rsi14_15m_curr &gt; Rsi14_15m_prev</c>.
    ///   3. 1m EMA crossover/üstü: <c>Ema9_1m &gt; Ema21_1m</c> (crossover veya
    ///      sustained).
    ///   4. 1m volume surge: <c>currentVolume_1m &gt; VolumeSma20_1m × 1.2</c>.
    ///   5. 1m ATR aktif: <c>Atr14_1m / currentClose_1m &gt;= 0.0003</c>.
    ///   6. 15m bar kapanmış (<see cref="HybridMomentum1mIndicatorSnapshot"/>
    ///      <c>BarClosed_15m == true</c>).
    ///   7. Cooldown: per-coin 3 bar × 1dk = 3dk (<see cref="ICooldownService"/>).
    /// ATR15m tabanlı TP/SL (TP 1.5×ATR15m clip [%0.40, %1.00], SL 0.8×ATR15m
    /// clip [%0.20, %0.40] — R:R 1.875:1, BE WR ~%34.8), MaxHold 30dk.
    /// Long-only spot paper. Evaluator: <c>HybridMomentum1mEvaluator</c>.
    /// </summary>
    HybridMomentum1m = 7,
}

public enum StrategySignalDirection
{
    Long = 1,
    Short = 2,
    Exit = 3,
}
