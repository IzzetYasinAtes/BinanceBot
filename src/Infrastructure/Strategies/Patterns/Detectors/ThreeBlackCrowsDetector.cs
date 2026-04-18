using System.Text.Json;
using BinanceBot.Application.Strategies.Patterns;
using BinanceBot.Domain.MarketData;

namespace BinanceBot.Infrastructure.Strategies.Patterns.Detectors;

/// <summary>
/// Three black crows — mirror of <see cref="ThreeWhiteSoldiersDetector"/>.
/// </summary>
public sealed class ThreeBlackCrowsDetector : IPatternDetector
{
    public PatternType Type => PatternType.ThreeBlackCrows;
    public decimal Weight => 0.78m;

    public PatternResult? Detect(IReadOnlyList<Kline> bars)
    {
        if (bars.Count < 22)
        {
            return null;
        }

        var b1 = bars[^3];
        var b2 = bars[^2];
        var b3 = bars[^1];

        if (!(b1.ClosePrice < b1.OpenPrice
              && b2.ClosePrice < b2.OpenPrice
              && b3.ClosePrice < b3.OpenPrice))
        {
            return null;
        }

        if (!(b2.ClosePrice < b1.ClosePrice && b3.ClosePrice < b2.ClosePrice))
        {
            return null;
        }

        if (!(b2.OpenPrice <= b1.OpenPrice && b2.OpenPrice >= b1.ClosePrice
              && b3.OpenPrice <= b2.OpenPrice && b3.OpenPrice >= b2.ClosePrice))
        {
            return null;
        }

        var avgVol = PatternFilters.AverageVolume(bars, 20);
        if (!PatternFilters.VolumeConfirmed(b3, avgVol, 1.2m))
        {
            return null;
        }

        var conf = PatternFilters.Clamp01(0.55m
            + (PatternFilters.TrendDown(bars, 20) ? 0.15m : 0m)
            + 0.10m);

        var entry = b3.ClosePrice;
        var stop = b1.HighPrice + 0.0001m;
        var risk = stop - entry;
        if (risk <= 0m)
        {
            return null;
        }
        var tp = entry - risk * 1.5m;

        var ctx = JsonSerializer.Serialize(new
        {
            type = "three_black_crows",
            b1Close = b1.ClosePrice,
            b2Close = b2.ClosePrice,
            b3Close = b3.ClosePrice,
            volume = b3.Volume,
            avgVolume = avgVol,
            confidence = conf,
        });

        return new PatternResult(
            PatternType.ThreeBlackCrows,
            PatternDirection.Short,
            conf,
            entry,
            stop,
            tp,
            MaxHoldBars: 5,
            ctx);
    }
}
