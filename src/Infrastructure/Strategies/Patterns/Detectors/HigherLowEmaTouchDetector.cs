using BinanceBot.Application.Strategies.Patterns;
using BinanceBot.Domain.Common;

namespace BinanceBot.Infrastructure.Strategies.Patterns.Detectors;

/// <summary>
/// Loop 81 — P6 Higher-Low EMA-Touch (spec §2 #6, weight 2). Uptrend (EMA21
/// &gt; EMA50) içinde son 3 bar higher-low yapıyor + close EMA21'e yakın
/// (touch).
///
/// Trigger:
/// <list type="bullet">
///   <item>EMA21 &gt; EMA50 (uptrend).</item>
///   <item>RecentBars son 3 bar higher-low (Low artıyor).</item>
///   <item>Close EMA21'in ±0.2% bandında (touch).</item>
/// </list>
/// Skor: hepsi 1.0; touch yok ama uptrend + higher-low varsa 0.5.
/// </summary>
public sealed class HigherLowEmaTouchDetector : IPatternDetector
{
    public string Name => "higher_low_ema_touch";
    public decimal DefaultWeight => 2m;
    public bool IsHardGate => false;
    public TradeDirection? Direction => TradeDirection.Long;

    private const decimal TouchBand = 0.002m; // ±0.2%

    public PatternEvaluation Evaluate(BarSnapshot snapshot)
    {
        var uptrend = snapshot.Ema21 > snapshot.Ema50;
        if (!uptrend)
        {
            return new PatternEvaluation(Name, 0m, Reason: "not_uptrend");
        }

        var bars = snapshot.RecentBars;
        if (bars.Count < 4)
        {
            return new PatternEvaluation(Name, 0m, Reason: "insufficient_history");
        }

        // Higher-low: son 3 bar low'ları artıyor (bars[^4] < bars[^3] < bars[^1])
        var l3 = bars[^4].LowPrice;
        var l2 = bars[^3].LowPrice;
        var l0 = bars[^1].LowPrice;
        var higherLow = l0 > l2 && l2 > l3;
        if (!higherLow)
        {
            return new PatternEvaluation(Name, 0m, Reason: "not_higher_low");
        }

        if (snapshot.Ema21 <= 0m)
        {
            return new PatternEvaluation(Name, 0m, Reason: "ema21_zero");
        }

        var distance = Math.Abs(snapshot.Close - snapshot.Ema21) / snapshot.Ema21;
        if (distance <= TouchBand)
        {
            return new PatternEvaluation(Name, 1m,
                Reason: "higher_low_ema_touch_full",
                Payload: new { ema21 = snapshot.Ema21, distance });
        }

        // Touch yok ama yapı doğru → yarı puan
        return new PatternEvaluation(Name, 0.5m,
            Reason: "higher_low_no_touch",
            Payload: new { ema21 = snapshot.Ema21, distance });
    }
}
