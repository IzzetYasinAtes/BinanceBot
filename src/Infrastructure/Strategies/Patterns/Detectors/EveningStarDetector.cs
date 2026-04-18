using System.Text.Json;
using BinanceBot.Application.Strategies.Patterns;
using BinanceBot.Domain.MarketData;

namespace BinanceBot.Infrastructure.Strategies.Patterns.Detectors;

/// <summary>
/// Evening star — mirror of <see cref="MorningStarDetector"/>: bullish bar, small-body
/// bar, bearish bar closing below the first bar's midpoint. Gap requirement disabled.
/// </summary>
public sealed class EveningStarDetector : IPatternDetector
{
    public PatternType Type => PatternType.EveningStar;
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

        var b1Body = Math.Abs(b1.OpenPrice - b1.ClosePrice);
        var b1Range = b1.HighPrice - b1.LowPrice;
        if (b1.ClosePrice <= b1.OpenPrice || b1Range <= 0m || b1Body < b1Range * 0.5m)
        {
            return null;
        }

        var b2Body = Math.Abs(b2.OpenPrice - b2.ClosePrice);
        if (b2Body > b1Body * 0.30m)
        {
            return null;
        }

        if (b3.ClosePrice >= b3.OpenPrice)
        {
            return null;
        }
        var b1Mid = (b1.OpenPrice + b1.ClosePrice) / 2m;
        if (b3.ClosePrice >= b1Mid)
        {
            return null;
        }

        var avgVol = PatternFilters.AverageVolume(bars, 20);
        var volBonus = PatternFilters.VolumeConfirmed(b3, avgVol, 1.3m) ? 0.10m : 0m;

        var conf = PatternFilters.Clamp01(0.55m
            + (PatternFilters.TrendUp(bars, 20) ? 0.15m : 0m)
            + volBonus);

        var entry = b3.ClosePrice;
        var stop = Math.Max(b1.HighPrice, b2.HighPrice) + 0.0001m;
        var risk = stop - entry;
        if (risk <= 0m)
        {
            return null;
        }
        var tp = entry - risk * 1.7m;

        var ctx = JsonSerializer.Serialize(new
        {
            type = "evening_star",
            b1Body,
            b2Body,
            b1Mid,
            b3Close = b3.ClosePrice,
            volume = b3.Volume,
            avgVolume = avgVol,
            confidence = conf,
        });

        return new PatternResult(
            PatternType.EveningStar,
            PatternDirection.Short,
            conf,
            entry,
            stop,
            tp,
            MaxHoldBars: 7,
            ctx);
    }
}
