using System.Text.Json;
using BinanceBot.Application.Strategies.Patterns;
using BinanceBot.Domain.MarketData;

namespace BinanceBot.Infrastructure.Strategies.Patterns.Detectors;

/// <summary>
/// Descending triangle — mirror of <see cref="AscendingTriangleDetector"/>:
/// flat support touched ≥3 times, falling resistance, breakdown below support.
/// </summary>
public sealed class DescendingTriangleDetector : IPatternDetector
{
    public PatternType Type => PatternType.DescendingTriangle;
    public decimal Weight => 0.62m;

    public PatternResult? Detect(IReadOnlyList<Kline> bars)
    {
        const int Window = 15;
        if (bars.Count < Window + 5)
        {
            return null;
        }

        var startIdx = bars.Count - 1 - Window;
        var endIdx = bars.Count - 2;

        decimal support = decimal.MaxValue;
        for (var i = startIdx; i <= endIdx; i++)
        {
            if (bars[i].LowPrice < support) support = bars[i].LowPrice;
        }
        if (support <= 0m)
        {
            return null;
        }

        var touches = 0;
        for (var i = startIdx; i <= endIdx; i++)
        {
            if (Math.Abs(bars[i].LowPrice - support) / support <= 0.003m)
            {
                touches++;
            }
        }
        if (touches < 3)
        {
            return null;
        }

        var midIdx = startIdx + Window / 2;
        decimal firstHalfMax = decimal.MinValue, secondHalfMax = decimal.MinValue;
        for (var i = startIdx; i < midIdx; i++)
        {
            if (bars[i].HighPrice > firstHalfMax) firstHalfMax = bars[i].HighPrice;
        }
        for (var i = midIdx; i <= endIdx; i++)
        {
            if (bars[i].HighPrice > secondHalfMax) secondHalfMax = bars[i].HighPrice;
        }
        if (secondHalfMax >= firstHalfMax)
        {
            return null;
        }

        var last = bars[^1];
        if (last.ClosePrice >= support)
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
            + (touches >= 4 ? 0.10m : 0m));

        var entry = last.ClosePrice;
        var stop = secondHalfMax + 0.0001m;
        var risk = stop - entry;
        if (risk <= 0m)
        {
            return null;
        }
        var triHeight = firstHalfMax - support;
        var tp = entry - Math.Max(triHeight, risk * 1.5m);

        var ctx = JsonSerializer.Serialize(new
        {
            type = "descending_triangle",
            support,
            touches,
            firstHalfMax,
            secondHalfMax,
            breakdown = last.ClosePrice,
            volume = last.Volume,
            avgVolume = avgVol,
            confidence = conf,
        });

        return new PatternResult(
            PatternType.DescendingTriangle,
            PatternDirection.Short,
            conf,
            entry,
            stop,
            tp,
            MaxHoldBars: 10,
            ctx);
    }
}
