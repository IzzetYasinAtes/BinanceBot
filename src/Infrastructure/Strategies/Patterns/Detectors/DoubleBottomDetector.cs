using System.Text.Json;
using BinanceBot.Application.Strategies.Patterns;
using BinanceBot.Domain.MarketData;

namespace BinanceBot.Infrastructure.Strategies.Patterns.Detectors;

/// <summary>
/// Double bottom — two pivot lows within 0.5% of each other separated by a higher pivot,
/// breakout above the neckline with a volume spike. ADR-0014 §14.3 weight 0.85, max-hold 10.
/// </summary>
public sealed class DoubleBottomDetector : IPatternDetector
{
    public PatternType Type => PatternType.DoubleBottom;
    public decimal Weight => 0.85m;

    public PatternResult? Detect(IReadOnlyList<Kline> bars)
    {
        const int LookbackWindow = 18;
        if (bars.Count < LookbackWindow + 4)
        {
            return null;
        }

        var last = bars[^1];

        // Scan the window looking for two pivot lows. Walk back-to-front so the most
        // recent qualifying pair is the one we lock in.
        int? p2Index = null;
        int? p1Index = null;
        var windowStart = bars.Count - 1 - LookbackWindow;

        for (var i = bars.Count - 3; i >= windowStart && i > 1; i--)
        {
            if (!PatternFilters.IsPivotLow(bars, i))
            {
                continue;
            }
            if (p2Index is null)
            {
                p2Index = i;
                continue;
            }
            // Need a separation of at least 3 bars to be distinct lows.
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

        // Two lows must be within ±0.5% of each other.
        var lowDelta = Math.Abs(p1.LowPrice - p2.LowPrice) / p1.LowPrice;
        if (lowDelta > 0.005m)
        {
            return null;
        }

        // Neckline = highest high between the two pivots.
        decimal neckline = decimal.MinValue;
        for (var i = p1Index.Value + 1; i < p2Index.Value; i++)
        {
            if (bars[i].HighPrice > neckline)
            {
                neckline = bars[i].HighPrice;
            }
        }
        if (neckline == decimal.MinValue)
        {
            return null;
        }

        // Breakout: latest close above the neckline.
        if (last.ClosePrice <= neckline)
        {
            return null;
        }

        var avgVol = PatternFilters.AverageVolume(bars, 20);
        if (!PatternFilters.VolumeConfirmed(last, avgVol, 1.5m))
        {
            return null;
        }

        var conf = PatternFilters.Clamp01(0.60m
            + 0.15m  // breakout confirmed
            + 0.10m  // volume confirmed
            + (lowDelta < 0.002m ? 0.10m : 0m));  // tight match

        var entry = last.ClosePrice;
        var stop = Math.Min(p1.LowPrice, p2.LowPrice) - 0.0001m;
        var risk = entry - stop;
        if (risk <= 0m)
        {
            return null;
        }
        // Pattern target: distance from neckline to lows projected above neckline.
        var measuredMove = neckline - Math.Min(p1.LowPrice, p2.LowPrice);
        var tp = entry + Math.Max(measuredMove, risk * 1.5m);

        var ctx = JsonSerializer.Serialize(new
        {
            type = "double_bottom",
            p1Low = p1.LowPrice,
            p2Low = p2.LowPrice,
            neckline,
            breakout = last.ClosePrice,
            volume = last.Volume,
            avgVolume = avgVol,
            lowDelta,
            confidence = conf,
        });

        return new PatternResult(
            PatternType.DoubleBottom,
            PatternDirection.Long,
            conf,
            entry,
            stop,
            tp,
            MaxHoldBars: 10,
            ctx);
    }
}
