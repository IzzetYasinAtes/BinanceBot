using BinanceBot.Application.Strategies.Patterns;
using BinanceBot.Domain.Common;

namespace BinanceBot.Infrastructure.Strategies.Patterns.Detectors;

/// <summary>
/// Loop 92 — Short-bias EMA9 slope down (ADR-0025). HigherLowEmaTouch'un
/// downtrend versiyonu. EMA9 azalan + son bar EMA9 altında kapandı.
///
/// Trigger:
/// <list type="bullet">
///   <item>Ema9 &lt; Ema9Prev (slope down).</item>
///   <item>Close &lt; Ema9 (fiyat EMA9 altında).</item>
///   <item>Close &lt; Open (bear bar).</item>
/// </list>
/// </summary>
public sealed class Ema9SlopeDownDetector : IPatternDetector
{
    public string Name => "ema9_slope_down";
    public decimal DefaultWeight => 1m;
    public bool IsHardGate => false;
    public TradeDirection? Direction => TradeDirection.Short;

    public PatternEvaluation Evaluate(BarSnapshot snapshot)
    {
        if (snapshot.Ema9 <= 0m || snapshot.Ema9Prev <= 0m)
        {
            return new PatternEvaluation(Name, 0m, Reason: "ema9_warmup");
        }

        var slopeDown = snapshot.Ema9 < snapshot.Ema9Prev;
        var closeBelowEma = snapshot.Close < snapshot.Ema9;
        var bearBar = snapshot.Close < snapshot.Open;

        if (!slopeDown)
        {
            return new PatternEvaluation(Name, 0m, Reason: "ema9_slope_up");
        }

        if (closeBelowEma && bearBar)
        {
            return new PatternEvaluation(Name, 1m, Reason: "slope_down_close_below_bear");
        }
        if (closeBelowEma || bearBar)
        {
            return new PatternEvaluation(Name, 0.5m, Reason: "partial_short_signal");
        }
        return new PatternEvaluation(Name, 0m, Reason: "slope_down_only");
    }
}
