namespace BinanceBot.Application.Strategies.Indicators;

/// <summary>
/// Loop 50 AR-GE — Hibrit 1m frekans tetiği + 15m kalite kapısı snapshot'ı.
/// İki timeframe birleşik: <see cref="MarketIndicatorService"/> hem
/// <c>OneMinute</c> hem <c>FifteenMinute</c> rolling buffer'larından okuyup
/// tek snapshot'ta evaluator'a verir. SRP gerekçeli ayrı tip:
/// <see cref="EmaScalperIndicatorSnapshot"/> (yalnız 1m EMA scalper) ile
/// <see cref="BbMeanReversionIndicatorSnapshot"/> (yalnız 15m BB mean reversion)
/// kombine edilmek istenseydi nullable alan koleksiyonu evaluator'ı "hangi
/// alan dolu?" branch'ına sokardı; iki timeframe birden ihtiyaç duyulan bu
/// snapshot için ayrı tip net SRP sınırı çizer.
///
/// Loop 49 EmaScalper1m düşük WR sonrası pivot — 1m crossover'ın "scalp tanıma"
/// gücüne 15m BB lower (counter-trend kalite filtresi) eklenir; sinyal hem
/// dip yakalama (BB lower altına girmiş 15m bar) hem momentum dönüşü (1m
/// EMA9 &gt; EMA21 + RSI15m yukarı dönüş + volume surge) ister. Asimetrik
/// R:R 1.875:1 (TP 1.5×ATR15 / SL 0.8×ATR15) — BE WR ~%34.8 (binance-expert
/// spec onaylı).
/// </summary>
/// <param name="Ema9_1m">Son 1m bar dahil EMA9 değeri — 1m crossover/üstü
/// trigger: <c>Ema9_1m &gt; Ema21_1m</c>.</param>
/// <param name="Ema21_1m">Son 1m bar dahil EMA21 değeri — 1m crossover/üstü
/// trigger.</param>
/// <param name="Ema9Prev_1m">Bir önceki 1m bar EMA9 değeri — crossover trace
/// (<c>prev &lt;= prev21 AND curr &gt; curr21</c>) için ContextJson logu.</param>
/// <param name="Ema21Prev_1m">Bir önceki 1m bar EMA21 değeri — crossover trace
/// için ContextJson logu.</param>
/// <param name="CurrentVolume_1m">Son kapalı 1m bar hacmi — volume surge
/// numeratörü.</param>
/// <param name="VolumeMa20_1m">Son 20 bar 1m volume aritmetik ortalaması —
/// volume surge denominatörü: <c>currentVolume_1m &gt; VolumeMa20_1m × 1.2</c>.</param>
/// <param name="Atr14_1m">Son 14 bar 1m Average True Range — MinAtrPct sessiz
/// piyasa filtresi: <c>Atr14_1m / currentClose_1m &gt;= 0.0003</c>.</param>
/// <param name="CurrentClose_1m">Son kapalı 1m bar kapanışı — entry referansı
/// + MinAtrPct denominatörü.</param>
/// <param name="BarClosed_1m">Son 1m bar kapanmış mı — defansif (service zaten
/// yalnızca kapalı bar'ı buffer'a alır).</param>
/// <param name="LastBarOpenTime_1m">Son kapalı 1m bar OpenTime — log/diagnostic
/// referansı.</param>
/// <param name="BbUpper_15m">Bollinger üst band 15m — bilgi amaçlı (long-only
/// path kullanmaz, ContextJson logu).</param>
/// <param name="BbMiddle_15m">Bollinger orta band 15m (SMA period). Hedef
/// referans olarak ContextJson'a yazılır; evaluator giriş kararında kullanmaz.</param>
/// <param name="BbLower_15m">Bollinger alt band 15m — kalite kapısı:
/// <c>currentClose_15m &lt; BbLower_15m</c> (dip yakalama).</param>
/// <param name="Rsi14_15m">14-bar Wilder RSI 15m (current bar dahil) —
/// oversold koşulu: <c>Rsi14_15m &lt; 40</c>.</param>
/// <param name="Rsi14Prev_15m">Bir önceki 15m bar dahil RSI14 değeri —
/// momentum yukarı dönüş koşulu: <c>Rsi14_15m_curr &gt; Rsi14_15m_prev</c>.</param>
/// <param name="Atr14_15m">Son 14 bar 15m Average True Range — TP/SL
/// geometrisi denominatörü.</param>
/// <param name="CurrentClose_15m">Son kapalı 15m bar kapanışı — BB kapışı
/// karşılaştırma referansı.</param>
/// <param name="BarClosed_15m">Son 15m bar kapanmış mı — defansif (service
/// zaten yalnızca kapalı bar'ı buffer'a alır).</param>
/// <param name="LastBarOpenTime_15m">Son kapalı 15m bar OpenTime — log/diagnostic
/// referansı.</param>
/// <param name="AsOf">Snapshot zamanı (son 1m bar close time — yüksek frekans
/// referansı; 15m bar close time her 15dk'da bir günceller).</param>
public sealed record HybridMomentum1mIndicatorSnapshot(
    // 1m fields
    decimal Ema9_1m,
    decimal Ema21_1m,
    decimal Ema9Prev_1m,
    decimal Ema21Prev_1m,
    decimal CurrentVolume_1m,
    decimal VolumeMa20_1m,
    decimal Atr14_1m,
    decimal CurrentClose_1m,
    bool BarClosed_1m,
    DateTimeOffset LastBarOpenTime_1m,
    // 15m fields
    decimal BbUpper_15m,
    decimal BbMiddle_15m,
    decimal BbLower_15m,
    decimal Rsi14_15m,
    decimal Rsi14Prev_15m,
    decimal Atr14_15m,
    decimal CurrentClose_15m,
    bool BarClosed_15m,
    DateTimeOffset LastBarOpenTime_15m,
    DateTimeOffset AsOf);
