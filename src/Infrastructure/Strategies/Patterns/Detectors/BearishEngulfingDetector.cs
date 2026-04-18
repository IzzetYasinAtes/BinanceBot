using System.Text.Json;
using BinanceBot.Application.Strategies.Patterns;
using BinanceBot.Domain.MarketData;

namespace BinanceBot.Infrastructure.Strategies.Patterns.Detectors;

/// <summary>
/// Bearish engulfing — mirror of <see cref="BullishEngulfingDetector"/>.
/// </summary>
public sealed class BearishEngulfingDetector : IPatternDetector
{
    public PatternType Type => PatternType.BearishEngulfing;
    public decimal Weight => 0.55m;

    public PatternResult? Detect(IReadOnlyList<Kline> bars)
    {
        if (bars.Count < 22)
        {
            return null;
        }

        var b1 = bars[^2];
        var b2 = bars[^1];

        var bullishB1 = b1.ClosePrice > b1.OpenPrice;
        var bearishB2 = b2.ClosePrice < b2.OpenPrice;
        var engulfs = b2.OpenPrice >= b1.ClosePrice && b2.ClosePrice <= b1.OpenPrice;
        if (!bullishB1 || !bearishB2 || !engulfs)
        {
            return null;
        }

        var avgVol = PatternFilters.AverageVolume(bars, 20);
        if (!PatternFilters.VolumeConfirmed(b2, avgVol, 1.5m))
        {
            return null;
        }

        if (!PatternFilters.RsiInBand(bars, 14, 40m, 60m))
        {
            return null;
        }

        var trendDown = PatternFilters.TrendDown(bars, 20);
        var conf = PatternFilters.Clamp01(0.50m
            + (trendDown ? 0.15m : 0m)
            + 0.10m
            + 0.10m);

        var entry = b2.ClosePrice;
        var stop = b1.HighPrice + 0.0001m;
        var risk = stop - entry;
        var tp = entry - risk * 1.5m;

        var ctx = JsonSerializer.Serialize(new
        {
            type = "bearish_engulfing",
            b1Close = b1.ClosePrice,
            b2Close = b2.ClosePrice,
            volume = b2.Volume,
            avgVolume = avgVol,
            trendDown,
            confidence = conf,
        });

        return new PatternResult(
            PatternType.BearishEngulfing,
            PatternDirection.Short,
            conf,
            entry,
            stop,
            tp,
            MaxHoldBars: 5,
            ctx);
    }
}
