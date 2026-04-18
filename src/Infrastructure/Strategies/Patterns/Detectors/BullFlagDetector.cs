using System.Text.Json;
using BinanceBot.Application.Strategies.Patterns;
using BinanceBot.Domain.MarketData;

namespace BinanceBot.Infrastructure.Strategies.Patterns.Detectors;

/// <summary>
/// Bull flag — sharp rally (flagpole) of ≥1% over 4 bars, followed by a 3-bar
/// shallow consolidation (retrace ≤ 50% of pole), then a breakout above the
/// consolidation high with a volume spike. ADR-0014 §14.3 weight 0.64, max-hold 10.
/// </summary>
public sealed class BullFlagDetector : IPatternDetector
{
    public PatternType Type => PatternType.BullFlag;
    public decimal Weight => 0.64m;

    public PatternResult? Detect(IReadOnlyList<Kline> bars)
    {
        if (bars.Count < 22)
        {
            return null;
        }

        // Flagpole: bars[^9..^5] should be net ≥1% advance.
        var poleStart = bars[^9].LowPrice;
        var poleEnd = bars[^5].HighPrice;
        var poleAdvance = poleEnd - poleStart;
        if (poleAdvance <= 0m || poleAdvance / poleStart < 0.01m)
        {
            return null;
        }

        // Consolidation: bars[^4..^2] retracement ≤ 50% of pole, slope shallow.
        var consMin = bars[^4].LowPrice;
        var consMax = bars[^4].HighPrice;
        for (var i = bars.Count - 4; i <= bars.Count - 2; i++)
        {
            if (bars[i].LowPrice < consMin) consMin = bars[i].LowPrice;
            if (bars[i].HighPrice > consMax) consMax = bars[i].HighPrice;
        }
        var retrace = poleEnd - consMin;
        if (retrace > poleAdvance * 0.5m)
        {
            return null;
        }

        // Breakout: latest close above the consolidation high.
        var last = bars[^1];
        if (last.ClosePrice <= consMax)
        {
            return null;
        }

        var avgVol = PatternFilters.AverageVolume(bars, 20);
        if (!PatternFilters.VolumeConfirmed(last, avgVol, 1.5m))
        {
            return null;
        }

        var conf = PatternFilters.Clamp01(0.55m
            + 0.15m  // breakout
            + 0.10m  // volume
            + (PatternFilters.TrendUp(bars, 20) ? 0.10m : 0m));

        var entry = last.ClosePrice;
        var stop = consMin - 0.0001m;
        var risk = entry - stop;
        if (risk <= 0m)
        {
            return null;
        }
        // Pattern target: pole length projected from breakout.
        var tp = entry + Math.Max(poleAdvance, risk * 1.5m);

        var ctx = JsonSerializer.Serialize(new
        {
            type = "bull_flag",
            poleStart,
            poleEnd,
            poleAdvance,
            consMin,
            consMax,
            volume = last.Volume,
            avgVolume = avgVol,
            confidence = conf,
        });

        return new PatternResult(
            PatternType.BullFlag,
            PatternDirection.Long,
            conf,
            entry,
            stop,
            tp,
            MaxHoldBars: 10,
            ctx);
    }
}
