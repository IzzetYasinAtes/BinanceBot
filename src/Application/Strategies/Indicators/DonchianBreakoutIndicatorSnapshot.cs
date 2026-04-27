namespace BinanceBot.Application.Strategies.Indicators;

/// <summary>
/// Loop 41 AR-GE — 15m bar bazında hesaplanmış Donchian Channel Breakout snapshot.
/// Ayrı tip tutulması SRP gerekçeli: <see cref="MicroScalperIndicatorSnapshot"/>
/// (VWAP/EMA/VolSMA/ATR) ile <see cref="DonchianBreakoutIndicatorSnapshot"/>
/// (Donchian/Volume Z-Score/ATR) farklı sinyal modeline ait. Tek snapshot'ta
/// nullable alan koleksiyonu tutmak evaluator caller'ı "hangi alan null olabilir?"
/// branch'ına sokar.
/// </summary>
/// <param name="DonchianHigh20">
/// Son 20 KAPANMIŞ 15m bar'ın en yüksek High değeri (current bar dahil değil).
/// Long entry: <c>currentClose &gt; DonchianHigh20</c>.
/// </param>
/// <param name="DonchianLow20">
/// Son 20 kapanmış 15m bar'ın en düşük Low değeri — bilgi amaçlı (long-only path
/// kullanmaz, ileride short eklenirse hazır).
/// </param>
/// <param name="VolumeAvg20">Son 20 kapanmış bar volume aritmetik ortalama.</param>
/// <param name="VolumeStd20">
/// Son 20 kapanmış bar volume population standard deviation. Z-Score:
/// <c>(currentVolume − VolumeAvg20) / VolumeStd20</c>. <c>VolumeStd20 == 0</c>
/// (flat hacim) ⇒ Z-Score hesaplanamaz, evaluator no-signal döner.
/// </param>
/// <param name="CurrentVolume">Son kapanmış 15m bar hacmi — Z-Score numeratörü.</param>
/// <param name="Atr14">
/// Son 14 bar Average True Range — TP/SL geometrisini volatilite rejimine göre
/// adaptif ölçeklemek için. Warmup yetersiz ⇒ <c>0</c>; evaluator MinAtrPct
/// filtresi sessiz piyasa korumasını sağlar.
/// </param>
/// <param name="CurrentClose">Son kapanmış 15m bar kapanışı — entry referansı.</param>
/// <param name="BarClosed">
/// Son bar kapanmış mı (<c>k.x == true</c>). MarketIndicatorService yalnızca
/// <c>IsClosed=true</c> bar'ları buffer'a alır; bu alan defansif tasarım
/// gerekçesiyle her zaman <c>true</c> dönmelidir, evaluator yine de kontrol eder.
/// </param>
/// <param name="LastBarOpenTime">
/// Son kapanmış bar'ın OpenTime'ı — cooldown hesabı için referans (PM/runtime
/// notu: cooldown evaluator-level değil, bar-id bazlı stateless cooldown
/// V2 iterasyonuna kadar StrategyEvaluationHandler'a delegated).
/// </param>
/// <param name="AsOf">Son kapalı 15m bar close time.</param>
public sealed record DonchianBreakoutIndicatorSnapshot(
    decimal DonchianHigh20,
    decimal DonchianLow20,
    decimal VolumeAvg20,
    decimal VolumeStd20,
    decimal CurrentVolume,
    decimal Atr14,
    decimal CurrentClose,
    bool BarClosed,
    DateTimeOffset LastBarOpenTime,
    DateTimeOffset AsOf);
