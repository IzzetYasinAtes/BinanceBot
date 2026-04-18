using System.Text.Json;
using BinanceBot.Application.Strategies.Patterns;
using BinanceBot.Domain.MarketData;

namespace BinanceBot.Infrastructure.Strategies.Patterns.Detectors;

/// <summary>
/// Hammer — single bar with a long lower wick (≥ 2× body), tiny upper wick,
/// body sitting in the upper third. Bullish reversal in oversold zones (RSI &lt; 40).
/// ADR-0014 §14.3 weight 0.58, max-hold 5 bars.
/// </summary>
public sealed class HammerDetector : IPatternDetector
{
    public PatternType Type => PatternType.Hammer;
    public decimal Weight => 0.58m;

    public PatternResult? Detect(IReadOnlyList<Kline> bars)
    {
        if (bars.Count < 16)
        {
            return null;
        }

        var b = bars[^1];
        var bodyHigh = Math.Max(b.OpenPrice, b.ClosePrice);
        var bodyLow = Math.Min(b.OpenPrice, b.ClosePrice);
        var body = bodyHigh - bodyLow;
        var range = b.HighPrice - b.LowPrice;
        if (range <= 0m || body <= 0m)
        {
            return null;
        }

        var lowerWick = bodyLow - b.LowPrice;
        var upperWick = b.HighPrice - bodyHigh;

        // Lower wick at least 2× body, upper wick at most 10% of total range.
        if (lowerWick < body * 2m)
        {
            return null;
        }
        if (upperWick > range * 0.10m)
        {
            return null;
        }

        // Body in the upper portion — bodyLow above range midpoint.
        if (bodyLow < b.LowPrice + range * 0.5m)
        {
            return null;
        }

        // RSI must be in oversold band (Hammer is reversal, not continuation).
        if (!PatternFilters.RsiInBand(bars, 14, 0m, 40m))
        {
            return null;
        }

        var avgVol = PatternFilters.AverageVolume(bars, 14);
        var volBonus = PatternFilters.VolumeConfirmed(b, avgVol, 1.2m) ? 0.10m : 0m;
        var trendDown = PatternFilters.TrendDown(bars, 20);
        var conf = PatternFilters.Clamp01(0.50m
            + (trendDown ? 0.15m : 0m)
            + 0.10m
            + volBonus);

        var entry = b.ClosePrice;
        var stop = b.LowPrice - 0.0001m;
        var risk = entry - stop;
        var tp = entry + risk * 1.8m;

        var ctx = JsonSerializer.Serialize(new
        {
            type = "hammer",
            body,
            lowerWick,
            upperWick,
            volume = b.Volume,
            avgVolume = avgVol,
            confidence = conf,
        });

        return new PatternResult(
            PatternType.Hammer,
            PatternDirection.Long,
            conf,
            entry,
            stop,
            tp,
            MaxHoldBars: 5,
            ctx);
    }
}
