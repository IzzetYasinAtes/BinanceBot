using System.Text.Json;
using BinanceBot.Application.Strategies.Patterns;
using BinanceBot.Domain.MarketData;

namespace BinanceBot.Infrastructure.Strategies.Patterns.Detectors;

/// <summary>
/// Double top — mirror of <see cref="DoubleBottomDetector"/>: two pivot highs within
/// 0.5% of each other and a breakdown below the neckline.
/// </summary>
public sealed class DoubleTopDetector : IPatternDetector
{
    public PatternType Type => PatternType.DoubleTop;
    public decimal Weight => 0.85m;

    public PatternResult? Detect(IReadOnlyList<Kline> bars)
    {
        const int LookbackWindow = 18;
        if (bars.Count < LookbackWindow + 4)
        {
            return null;
        }

        var last = bars[^1];

        int? p2Index = null;
        int? p1Index = null;
        var windowStart = bars.Count - 1 - LookbackWindow;

        for (var i = bars.Count - 3; i >= windowStart && i > 1; i--)
        {
            if (!PatternFilters.IsPivotHigh(bars, i))
            {
                continue;
            }
            if (p2Index is null)
            {
                p2Index = i;
                continue;
            }
            if (p2Index.Value - i < 3)
            {
                continue;
            }
            p1Index = i;
            break;
        }

        if (p1Index is null || p2Index is null)
        {
            return null;
        }

        var p1 = bars[p1Index.Value];
        var p2 = bars[p2Index.Value];

        var highDelta = Math.Abs(p1.HighPrice - p2.HighPrice) / p1.HighPrice;
        if (highDelta > 0.005m)
        {
            return null;
        }

        decimal neckline = decimal.MaxValue;
        for (var i = p1Index.Value + 1; i < p2Index.Value; i++)
        {
            if (bars[i].LowPrice < neckline)
            {
                neckline = bars[i].LowPrice;
            }
        }
        if (neckline == decimal.MaxValue)
        {
            return null;
        }

        if (last.ClosePrice >= neckline)
        {
            return null;
        }

        var avgVol = PatternFilters.AverageVolume(bars, 20);
        if (!PatternFilters.VolumeConfirmed(last, avgVol, 1.5m))
        {
            return null;
        }

        var conf = PatternFilters.Clamp01(0.60m
            + 0.15m
            + 0.10m
            + (highDelta < 0.002m ? 0.10m : 0m));

        var entry = last.ClosePrice;
        var stop = Math.Max(p1.HighPrice, p2.HighPrice) + 0.0001m;
        var risk = stop - entry;
        if (risk <= 0m)
        {
            return null;
        }
        var measuredMove = Math.Max(p1.HighPrice, p2.HighPrice) - neckline;
        var tp = entry - Math.Max(measuredMove, risk * 1.5m);

        var ctx = JsonSerializer.Serialize(new
        {
            type = "double_top",
            p1High = p1.HighPrice,
            p2High = p2.HighPrice,
            neckline,
            breakdown = last.ClosePrice,
            volume = last.Volume,
            avgVolume = avgVol,
            highDelta,
            confidence = conf,
        });

        return new PatternResult(
            PatternType.DoubleTop,
            PatternDirection.Short,
            conf,
            entry,
            stop,
            tp,
            MaxHoldBars: 10,
            ctx);
    }
}
