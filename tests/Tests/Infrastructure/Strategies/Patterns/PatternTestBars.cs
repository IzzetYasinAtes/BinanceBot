using BinanceBot.Domain.MarketData;
using BinanceBot.Domain.ValueObjects;

namespace BinanceBot.Tests.Infrastructure.Strategies.Patterns;

/// <summary>
/// Synthetic bar builder used by the 14 pattern-detector unit tests
/// (ADR-0014 §14.9.1). Pure factories — every helper returns a fresh list,
/// no shared mutable state. All bars are 1-minute spot klines on BTCUSDT.
/// </summary>
internal static class PatternTestBars
{
    private static readonly Symbol Btc = Symbol.From("BTCUSDT");

    public static Kline Bar(int seq, decimal open, decimal high, decimal low, decimal close, decimal volume)
    {
        var openTime = DateTimeOffset.UnixEpoch.AddMinutes(seq);
        return Kline.Ingest(
            Btc, KlineInterval.OneMinute,
            openTime: openTime,
            closeTime: openTime.AddMinutes(1),
            open: open, high: high, low: low, close: close,
            volume: volume,
            quoteVolume: close * volume,
            tradeCount: 1,
            takerBuyBase: 0m, takerBuyQuote: 0m,
            isClosed: true);
    }

    /// <summary>
    /// Range-bound prefix series — small alternating moves around <paramref name="centre"/>
    /// so RSI sits ~50. Used as a neutral runway in front of a pattern-forming tail.
    /// </summary>
    public static List<Kline> RangePrefix(int count, decimal centre, decimal volume = 100m)
    {
        var list = new List<Kline>(count);
        for (var i = 0; i < count; i++)
        {
            var c = centre + (i % 2 == 0 ? -0.4m : 0.4m);
            list.Add(Bar(i, c, c + 0.5m, c - 0.5m, c, volume));
        }
        return list;
    }

    /// <summary>Steady downtrend — used to seed an oversold context.</summary>
    public static List<Kline> DowntrendPrefix(int count, decimal start, decimal step = 0.6m, decimal volume = 100m)
    {
        var list = new List<Kline>(count);
        for (var i = 0; i < count; i++)
        {
            var c = start - i * step;
            list.Add(Bar(i, c + 0.2m, c + 0.4m, c - 0.4m, c, volume));
        }
        return list;
    }

    /// <summary>Steady uptrend — used to seed an overbought context.</summary>
    public static List<Kline> UptrendPrefix(int count, decimal start, decimal step = 0.6m, decimal volume = 100m)
    {
        var list = new List<Kline>(count);
        for (var i = 0; i < count; i++)
        {
            var c = start + i * step;
            list.Add(Bar(i, c - 0.2m, c + 0.4m, c - 0.4m, c, volume));
        }
        return list;
    }
}
