namespace BinanceBot.Application.Strategies.Indicators;

/// <summary>
/// Loop 46 AR-GE — 1m bar bazında hesaplanmış EMA9/EMA21 crossover scalper
/// snapshot. Ayrı tip tutulması SRP gerekçeli: <see cref="MicroScalperIndicatorSnapshot"/>
/// (VWAP/EMA20/VolSMA/ATR — VWAP-bound momentum) ile
/// <see cref="EmaScalperIndicatorSnapshot"/>
/// (EMA9/EMA21/RSI14/VolSMA/ATR — EMA crossover trend-following scalp)
/// farklı sinyal modeline ait. Tek snapshot'ta nullable alan koleksiyonu
/// tutmak evaluator caller'ı "hangi alan null olabilir?" branch'ına sokar
/// (DonchianBreakout / BbMeanReversion ayrımındaki gerekçenin aynısı).
///
/// Loop 45 BbMeanReversion15m 6h'da 2 trade (düşük frekans) sonrası işlem
/// hacmi pivot — kullanıcı 20-30 trade/saat hedefi. 1m timeframe + EMA crossover
/// + nötr-RSI + gevşek hacim filtresi pozitif beklentili sinyal akışı sağlar
/// (binance-expert spec onaylı).
/// </summary>
/// <param name="Ema9Now">Son 1m bar dahil hesaplanmış EMA9 değeri — kısa-vade
/// trend referansı: <c>Ema9Now &gt; Ema21Now</c> long entry koşulu.</param>
/// <param name="Ema9Prev">Bir önceki bar EMA9 değeri — slope/cross teyidi
/// için bilgi amaçlı taşınır; current evaluator yalnız <c>Ema9Now</c> ile
/// karar verir, ContextJson loglamasında kullanılır.</param>
/// <param name="Ema21Now">Son bar dahil EMA21 değeri — uzun-vade trend
/// referansı: <c>Ema9Now &gt; Ema21Now</c> long entry koşulu.</param>
/// <param name="Ema21Prev">Bir önceki bar EMA21 değeri — bilgi amaçlı.</param>
/// <param name="Rsi14">14-bar Wilder RSI — nötr aralık filtresi
/// (default <c>40 &lt;= RSI14 &lt;= 65</c>): aşırı alım/satım uçlarında
/// sinyal yok.</param>
/// <param name="VolumeSma20">Son 20 bar (20dk) volume aritmetik ortalaması.
/// Gevşek hacim teyidi: <c>currentVolume &gt; VolumeSma20 × VolumeMultiplier</c>
/// (default 0.8 — DonchianBreakout 1.5'tan ve BbMeanReversion Z 1.0'dan
/// daha gevşek; trend-following frequency hedefi).</param>
/// <param name="CurrentVolume">Son kapalı 1m bar hacmi.</param>
/// <param name="Atr14">Son 14 bar Average True Range — TP/SL geometrisini
/// volatilite rejimine göre adaptif ölçeklemek için. Warmup yetersiz ⇒ 0;
/// evaluator MinAtrPct filtresi sessiz piyasa korumasını sağlar.</param>
/// <param name="CurrentClose">Son kapalı 1m bar kapanışı — entry referansı
/// + <c>CurrentClose &gt; Ema9Now</c> momentum koşulu.</param>
/// <param name="BarClosed">Son bar kapanmış mı (<c>k.x == true</c>).
/// MarketIndicatorService yalnızca <c>IsClosed=true</c> bar'ları buffer'a
/// alır; defansif tasarım gerekçesiyle her zaman <c>true</c> dönmelidir.</param>
/// <param name="LastBarOpenTime">Son kapalı bar OpenTime — cooldown referansı.</param>
/// <param name="AsOf">Son kapalı 1m bar close time.</param>
public sealed record EmaScalperIndicatorSnapshot(
    decimal Ema9Now,
    decimal Ema9Prev,
    decimal Ema21Now,
    decimal Ema21Prev,
    decimal Rsi14,
    decimal VolumeSma20,
    decimal CurrentVolume,
    decimal Atr14,
    decimal CurrentClose,
    bool BarClosed,
    DateTimeOffset LastBarOpenTime,
    DateTimeOffset AsOf);
