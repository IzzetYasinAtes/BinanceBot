using System.Text.Json;
using BinanceBot.Application.Strategies.Patterns;
using BinanceBot.Domain.MarketData;

namespace BinanceBot.Infrastructure.Strategies.Patterns.Detectors;

/// <summary>
/// Morning star — bearish bar, small-body bar, bullish bar that closes above the
/// midpoint of the first bar. Crypto-aware: gap requirement disabled (24/7 spot).
/// ADR-0014 §14.3 weight 0.67, max-hold 7 bars.
/// </summary>
public sealed class MorningStarDetector : IPatternDetector
{
    public PatternType Type => PatternType.MorningStar;
    public decimal Weight => 0.67m;

    public PatternResult? Detect(IReadOnlyList<Kline> bars)
    {
        if (bars.Count < 22)
        {
            return null;
        }

        var b1 = bars[^3];
        var b2 = bars[^2];
        var b3 = bars[^1];

        // 1. b1 bearish with a meaningful body.
        var b1Body = Math.Abs(b1.OpenPrice - b1.ClosePrice);
        var b1Range = b1.HighPrice - b1.LowPrice;
        if (b1.ClosePrice >= b1.OpenPrice || b1Range <= 0m || b1Body < b1Range * 0.5m)
        {
            return null;
        }

        // 2. b2 small body (< 30% of b1 body).
        var b2Body = Math.Abs(b2.OpenPrice - b2.ClosePrice);
        if (b2Body > b1Body * 0.30m)
        {
            return null;
        }

        // 3. b3 bullish closing above b1 midpoint.
        if (b3.ClosePrice <= b3.OpenPrice)
        {
            return null;
        }
        var b1Mid = (b1.OpenPrice + b1.ClosePrice) / 2m;
        if (b3.ClosePrice <= b1Mid)
        {
            return null;
        }

        var avgVol = PatternFilters.AverageVolume(bars, 20);
        var volBonus = PatternFilters.VolumeConfirmed(b3, avgVol, 1.3m) ? 0.10m : 0m;

        var conf = PatternFilters.Clamp01(0.55m
            + (PatternFilters.TrendDown(bars, 20) ? 0.15m : 0m)
            + volBonus);

        var entry = b3.ClosePrice;
        var stop = Math.Min(b1.LowPrice, b2.LowPrice) - 0.0001m;
        var risk = entry - stop;
        if (risk <= 0m)
        {
            return null;
        }
        var tp = entry + risk * 1.7m;

        var ctx = JsonSerializer.Serialize(new
        {
            type = "morning_star",
            b1Body,
            b2Body,
            b1Mid,
            b3Close = b3.ClosePrice,
            volume = b3.Volume,
            avgVolume = avgVol,
            confidence = conf,
        });

        return new PatternResult(
            PatternType.MorningStar,
            PatternDirection.Long,
            conf,
            entry,
            stop,
            tp,
            MaxHoldBars: 7,
            ctx);
    }
}
