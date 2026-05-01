namespace BinanceBot.Application.Strategies.Indicators;

/// <summary>
/// Loop 67 KMS — KlineMomentumSpread5m snapshot. Carries every primitive that
/// the KMS evaluator needs to evaluate the 5-condition AND filter on a closed
/// 5m bar:
/// <list type="number">
///   <item>RSI Recovery — <c>Rsi14 &gt; threshold AND Rsi14Prev &lt; threshold</c>.</item>
///   <item>EMA9 positive slope — <c>Ema9Now &gt; Ema9Prev</c>.</item>
///   <item>TradeCount surge — <c>CurrentTradeCount &gt; AvgTradeCount20 × N</c>.</item>
///   <item>Spread filter — derived live from <c>IBookTickerReader</c>, not in
///         this snapshot (BookTicker stream is sub-second; bar-aligned).</item>
///   <item>MinAtrPct — <c>Atr14 / CurrentClose &gt;= MinAtrPct</c>.</item>
/// </list>
///
/// Loop 77 — EMA200 trend gate + Bollinger band-width regime filter eklendi.
/// EMA200 hard-gate downtrend entry'leri eler (closePrice &gt; EMA200 zorunlu);
/// BBW skor sistemine 1 puan nice-to-have olarak girer (bant genişliği yüksekse
/// breakout dostu rejim). Buffer 200 bar olduğu için <c>Ema200</c> warmup
/// tamamlandıktan sonra anlamlı; warmup yetersizken <c>0m</c> döner ve gate
/// açılır (<c>snapshot.Ema200 &gt; 0m</c> kontrolü evaluator'da).
/// </summary>
/// <param name="CurrentClose">Last closed 5m bar close price — entry reference.</param>
/// <param name="Rsi14">14-bar Wilder RSI computed up to and including the
/// current closed bar (recovery side of the cross).</param>
/// <param name="Rsi14Prev">14-bar Wilder RSI computed on the bar prior to the
/// current bar (oversold side of the cross — must be below threshold).</param>
/// <param name="Ema9Now">EMA9 over the last 9 closes, current bar inclusive.</param>
/// <param name="Ema9Prev">EMA9 over the last 9 closes, current bar exclusive
/// (i.e. ending at <c>Count-2</c>) — slope reference.</param>
/// <param name="Atr14">14-bar Average True Range — TP/SL geometry source +
/// MinAtrPct quiet-market filter.</param>
/// <param name="AvgTradeCount20">20-bar arithmetic mean of <c>TradeCount</c>
/// — surge baseline.</param>
/// <param name="CurrentTradeCount">Trade count of the last closed 5m bar.</param>
/// <param name="Ema200">Loop 77 — 200-bar EMA at <c>Count-1</c>; trend gate
/// reference (long entries require <c>CurrentClose &gt; Ema200</c>). Returns
/// <c>0m</c> when warmup &lt; 200 bar; evaluator treats 0 as "gate disabled
/// (unavailable)".</param>
/// <param name="BollingerBandWidth">Loop 77 — BB(20, 2) bant genişliği:
/// <c>(Upper - Lower) / Middle</c>. Yüksek BBW = volatil/breakout dostu rejim;
/// düşük BBW = sıkışma. Skor sistemine 1 puan bonus (hard-gate değil — 0
/// puan emit'i tek başına engellemez, frekans korunur).</param>
/// <param name="LastBarOpenTime">Open time of the current closed 5m bar — log
/// + cooldown anchor.</param>
/// <param name="AsOf">Close time of the current 5m bar — snapshot freshness.</param>
public sealed record KmsMomentumSnapshot(
    decimal CurrentClose,
    decimal Rsi14,
    decimal Rsi14Prev,
    decimal Ema9Now,
    decimal Ema9Prev,
    decimal Atr14,
    decimal AvgTradeCount20,
    int CurrentTradeCount,
    decimal Ema200,
    decimal BollingerBandWidth,
    DateTimeOffset LastBarOpenTime,
    DateTimeOffset AsOf);
