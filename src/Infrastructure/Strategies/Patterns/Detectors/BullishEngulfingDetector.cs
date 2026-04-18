using System.Text.Json;
using BinanceBot.Application.Strategies.Patterns;
using BinanceBot.Domain.MarketData;

namespace BinanceBot.Infrastructure.Strategies.Patterns.Detectors;

/// <summary>
/// Bullish engulfing — bearish bar fully wrapped by the next bullish bar.
/// ADR-0014 §14.3: volume + RSI band filters mandatory (Bulkowski red flag).
/// </summary>
public sealed class BullishEngulfingDetector : IPatternDetector
{
    public PatternType Type => PatternType.BullishEngulfing;
    public decimal Weight => 0.55m;

    public PatternResult? Detect(IReadOnlyList<Kline> bars)
    {
        if (bars.Count < 22)
        {
            return null;
        }

        var b1 = bars[^2];
        var b2 = bars[^1];

        var bearishB1 = b1.ClosePrice < b1.OpenPrice;
        var bullishB2 = b2.ClosePrice > b2.OpenPrice;
        var engulfs = b2.OpenPrice <= b1.ClosePrice && b2.ClosePrice >= b1.OpenPrice;
        if (!bearishB1 || !bullishB2 || !engulfs)
        {
            return null;
        }

        var avgVol = PatternFilters.AverageVolume(bars, 20);
        if (!PatternFilters.VolumeConfirmed(b2, avgVol, 1.5m))
        {
            return null;
        }

        if (!PatternFilters.RsiInBand(bars, 14, 40m, 60m))
        {
            return null;
        }

        var trendUp = PatternFilters.TrendUp(bars, 20);
        var conf = PatternFilters.Clamp01(0.50m
            + (trendUp ? 0.15m : 0m)
            + 0.10m
            + 0.10m);

        var entry = b2.ClosePrice;
        var stop = b1.LowPrice - 0.0001m;
        var risk = entry - stop;
        var tp = entry + risk * 1.5m;

        var ctx = JsonSerializer.Serialize(new
        {
            type = "bullish_engulfing",
            b1Close = b1.ClosePrice,
            b2Close = b2.ClosePrice,
            volume = b2.Volume,
            avgVolume = avgVol,
            trendUp,
            confidence = conf,
        });

        return new PatternResult(
            PatternType.BullishEngulfing,
            PatternDirection.Long,
            conf,
            entry,
            stop,
            tp,
            MaxHoldBars: 5,
            ctx);
    }
}
