using System.Text.Json;
using BinanceBot.Application.Strategies.Patterns;
using BinanceBot.Domain.MarketData;

namespace BinanceBot.Infrastructure.Strategies.Patterns.Detectors;

/// <summary>
/// Bear flag — mirror of <see cref="BullFlagDetector"/>: sharp drop, shallow
/// upward consolidation, breakdown below the consolidation low.
/// </summary>
public sealed class BearFlagDetector : IPatternDetector
{
    public PatternType Type => PatternType.BearFlag;
    public decimal Weight => 0.64m;

    public PatternResult? Detect(IReadOnlyList<Kline> bars)
    {
        if (bars.Count < 22)
        {
            return null;
        }

        var poleStart = bars[^9].HighPrice;
        var poleEnd = bars[^5].LowPrice;
        var poleDecline = poleStart - poleEnd;
        if (poleDecline <= 0m || poleDecline / poleStart < 0.01m)
        {
            return null;
        }

        var consMin = bars[^4].LowPrice;
        var consMax = bars[^4].HighPrice;
        for (var i = bars.Count - 4; i <= bars.Count - 2; i++)
        {
            if (bars[i].LowPrice < consMin) consMin = bars[i].LowPrice;
            if (bars[i].HighPrice > consMax) consMax = bars[i].HighPrice;
        }
        var retrace = consMax - poleEnd;
        if (retrace > poleDecline * 0.5m)
        {
            return null;
        }

        var last = bars[^1];
        if (last.ClosePrice >= consMin)
        {
            return null;
        }

        var avgVol = PatternFilters.AverageVolume(bars, 20);
        if (!PatternFilters.VolumeConfirmed(last, avgVol, 1.5m))
        {
            return null;
        }

        var conf = PatternFilters.Clamp01(0.55m
            + 0.15m
            + 0.10m
            + (PatternFilters.TrendDown(bars, 20) ? 0.10m : 0m));

        var entry = last.ClosePrice;
        var stop = consMax + 0.0001m;
        var risk = stop - entry;
        if (risk <= 0m)
        {
            return null;
        }
        var tp = entry - Math.Max(poleDecline, risk * 1.5m);

        var ctx = JsonSerializer.Serialize(new
        {
            type = "bear_flag",
            poleStart,
            poleEnd,
            poleDecline,
            consMin,
            consMax,
            volume = last.Volume,
            avgVolume = avgVol,
            confidence = conf,
        });

        return new PatternResult(
            PatternType.BearFlag,
            PatternDirection.Short,
            conf,
            entry,
            stop,
            tp,
            MaxHoldBars: 10,
            ctx);
    }
}
