using System.Text.Json;
using BinanceBot.Application.Strategies.Patterns;
using BinanceBot.Domain.MarketData;

namespace BinanceBot.Infrastructure.Strategies.Patterns.Detectors;

/// <summary>
/// Shooting star — mirror of <see cref="HammerDetector"/>: long upper wick (≥ 2× body),
/// tiny lower wick, body in lower third. Bearish reversal in overbought zones (RSI &gt; 60).
/// </summary>
public sealed class ShootingStarDetector : IPatternDetector
{
    public PatternType Type => PatternType.ShootingStar;
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

        if (upperWick < body * 2m)
        {
            return null;
        }
        if (lowerWick > range * 0.10m)
        {
            return null;
        }

        if (bodyHigh > b.HighPrice - range * 0.5m)
        {
            return null;
        }

        if (!PatternFilters.RsiInBand(bars, 14, 60m, 100m))
        {
            return null;
        }

        var avgVol = PatternFilters.AverageVolume(bars, 14);
        var volBonus = PatternFilters.VolumeConfirmed(b, avgVol, 1.2m) ? 0.10m : 0m;
        var trendUp = PatternFilters.TrendUp(bars, 20);
        var conf = PatternFilters.Clamp01(0.50m
            + (trendUp ? 0.15m : 0m)
            + 0.10m
            + volBonus);

        var entry = b.ClosePrice;
        var stop = b.HighPrice + 0.0001m;
        var risk = stop - entry;
        var tp = entry - risk * 1.8m;

        var ctx = JsonSerializer.Serialize(new
        {
            type = "shooting_star",
            body,
            lowerWick,
            upperWick,
            volume = b.Volume,
            avgVolume = avgVol,
            confidence = conf,
        });

        return new PatternResult(
            PatternType.ShootingStar,
            PatternDirection.Short,
            conf,
            entry,
            stop,
            tp,
            MaxHoldBars: 5,
            ctx);
    }
}
