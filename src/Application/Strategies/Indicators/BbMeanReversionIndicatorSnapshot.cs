namespace BinanceBot.Application.Strategies.Indicators;

/// <summary>
/// Loop 44 AR-GE — 15m bar bazında hesaplanmış Bollinger Bands Mean Reversion
/// snapshot. Ayrı tip tutulması SRP gerekçeli: <see cref="DonchianBreakoutIndicatorSnapshot"/>
/// (Donchian/Volume Z-Score/ATR — momentum/breakout) ile
/// <see cref="BbMeanReversionIndicatorSnapshot"/>
/// (BB/RSI/Volume Z-Score/ATR — counter-trend mean reversion) farklı sinyal modeline
/// ait. Tek snapshot'ta nullable alan koleksiyonu tutmak evaluator caller'ı
/// "hangi alan null olabilir?" branch'ına sokar.
///
/// Loop 41-42-43 DonchianBreakout %0 WR sonrası pivot — breakout-rejim filtresi
/// testnet hacim profili altında zayıf hit oranı verdi; karşı-trend BB lower
/// kapışı + RSI oversold + panik hacim teyidi alternatif arayış (binance-expert
/// spec onaylı).
/// </summary>
/// <param name="BbUpper">Bollinger üst band — bilgi amaçlı (long-only path
/// kullanmaz, ileride short eklenirse hazır).</param>
/// <param name="BbMiddle">Bollinger orta band (SMA period). Hedef referans olarak
/// ContextJson'a yazılır; evaluator giriş kararında kullanmaz.</param>
/// <param name="BbLower">Bollinger alt band — long entry sinyali:
/// <c>currentClose &lt; BbLower</c>.</param>
/// <param name="Rsi14">14-bar Wilder RSI — oversold filtresi (default eşik &lt; 30).</param>
/// <param name="VolumeAvg20">Son 20 bar volume aritmetik ortalaması (current bar
/// dahil). Z-Score numeratörü.</param>
/// <param name="VolumeStd20">Son 20 bar volume population std dev. Z-Score:
/// <c>(currentVolume − VolumeAvg20) / VolumeStd20</c>. Std == 0 (flat hacim) ⇒
/// Z-Score hesaplanamaz, evaluator no-signal döner.</param>
/// <param name="CurrentVolume">Son kapalı 15m bar hacmi — Z-Score numeratörü.</param>
/// <param name="Atr14">Son 14 bar Average True Range — TP/SL geometrisini volatilite
/// rejimine göre adaptif ölçeklemek için. Warmup yetersiz ⇒ 0; evaluator MinAtrPct
/// filtresi sessiz piyasa korumasını sağlar.</param>
/// <param name="CurrentClose">Son kapalı 15m bar kapanışı — entry referansı.</param>
/// <param name="BarClosed">Son bar kapanmış mı (<c>k.x == true</c>).
/// MarketIndicatorService yalnızca <c>IsClosed=true</c> bar'ları buffer'a alır;
/// bu alan defansif tasarım gerekçesiyle her zaman <c>true</c> dönmelidir,
/// evaluator yine de kontrol eder.</param>
/// <param name="LastBarOpenTime">Son kapalı bar OpenTime — cooldown referansı.</param>
/// <param name="AsOf">Son kapalı 15m bar close time.</param>
/// <param name="Ema200_15m">Loop 58 disaster recovery — 200-period EMA close
/// üzerinde 15m bar bazında. Trend filtresi: <c>currentClose &gt; Ema200_15m</c>
/// ⇒ uptrend (long sinyallerine izin); aksi halde downtrend → evaluator
/// "downtrend_skip" reason ile atar. EMA200 anlamlı olması için service
/// tarafında minimum 200 bar warmup eşiği zorunlu (FifteenMinuteBufferCapacity
/// ≥ 200). Pre-warmup ⇒ snapshot null döner.</param>
public sealed record BbMeanReversionIndicatorSnapshot(
    decimal BbUpper,
    decimal BbMiddle,
    decimal BbLower,
    decimal Rsi14,
    decimal VolumeAvg20,
    decimal VolumeStd20,
    decimal CurrentVolume,
    decimal Atr14,
    decimal CurrentClose,
    bool BarClosed,
    DateTimeOffset LastBarOpenTime,
    DateTimeOffset AsOf,
    decimal Ema200_15m);
