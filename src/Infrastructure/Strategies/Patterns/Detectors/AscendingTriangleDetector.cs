using System.Text.Json;
using BinanceBot.Application.Strategies.Patterns;
using BinanceBot.Domain.MarketData;

namespace BinanceBot.Infrastructure.Strategies.Patterns.Detectors;

/// <summary>
/// Ascending triangle — flat resistance touched ≥3 times within ±0.3%, rising
/// support trendline, breakout above resistance with volume spike.
/// ADR-0014 §14.3 weight 0.62, max-hold 10.
/// </summary>
public sealed class AscendingTriangleDetector : IPatternDetector
{
    public PatternType Type => PatternType.AscendingTriangle;
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

        // Resistance: highest high in the window — count touches within ±0.3%.
        decimal resistance = decimal.MinValue;
        for (var i = startIdx; i <= endIdx; i++)
        {
            if (bars[i].HighPrice > resistance) resistance = bars[i].HighPrice;
        }
        if (resistance <= 0m)
        {
            return null;
        }

        var touches = 0;
        for (var i = startIdx; i <= endIdx; i++)
        {
            if (Math.Abs(bars[i].HighPrice - resistance) / resistance <= 0.003m)
            {
                touches++;
            }
        }
        if (touches < 3)
        {
            return null;
        }

        // Rising support — first half min < second half min.
        var midIdx = startIdx + Window / 2;
        decimal firstHalfMin = decimal.MaxValue, secondHalfMin = decimal.MaxValue;
        for (var i = startIdx; i < midIdx; i++)
        {
            if (bars[i].LowPrice < firstHalfMin) firstHalfMin = bars[i].LowPrice;
        }
        for (var i = midIdx; i <= endIdx; i++)
        {
            if (bars[i].LowPrice < secondHalfMin) secondHalfMin = bars[i].LowPrice;
        }
        if (secondHalfMin <= firstHalfMin)
        {
            return null;
        }

        // Breakout.
        var last = bars[^1];
        if (last.ClosePrice <= resistance)
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
        var stop = secondHalfMin - 0.0001m;
        var risk = entry - stop;
        if (risk <= 0m)
        {
            return null;
        }
        // Triangle target: height of the triangle projected from breakout.
        var triHeight = resistance - firstHalfMin;
        var tp = entry + Math.Max(triHeight, risk * 1.5m);

        var ctx = JsonSerializer.Serialize(new
        {
            type = "ascending_triangle",
            resistance,
            touches,
            firstHalfMin,
            secondHalfMin,
            breakout = last.ClosePrice,
            volume = last.Volume,
            avgVolume = avgVol,
            confidence = conf,
        });

        return new PatternResult(
            PatternType.AscendingTriangle,
            PatternDirection.Long,
            conf,
            entry,
            stop,
            tp,
            MaxHoldBars: 10,
            ctx);
    }
}
