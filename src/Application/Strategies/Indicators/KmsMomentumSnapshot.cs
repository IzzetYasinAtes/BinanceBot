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
    DateTimeOffset LastBarOpenTime,
    DateTimeOffset AsOf);
