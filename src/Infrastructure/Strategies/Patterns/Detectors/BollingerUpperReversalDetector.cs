using BinanceBot.Application.Strategies.Patterns;
using BinanceBot.Domain.Common;

namespace BinanceBot.Infrastructure.Strategies.Patterns.Detectors;

/// <summary>
/// Loop 92 — Short-bias Bollinger upper reversal (ADR-0025). BollingerLowerReversal
/// simetrik. Bar üst banda dokundu/aştı, kapanış ortayı altta — mean reversion short.
///
/// Trigger:
/// <list type="bullet">
///   <item>High &gt;= BollingerUpper.</item>
///   <item>Close &lt; BollingerMiddle (orta band altına geri çekildi).</item>
///   <item>Close &lt; Open (bear bar).</item>
/// </list>
/// </summary>
public sealed class BollingerUpperReversalDetector : IPatternDetector
{
    public string Name => "bollinger_upper_reversal";
    public decimal DefaultWeight => 1.5m;
    public bool IsHardGate => false;
    public TradeDirection? Direction => TradeDirection.Short;

    public PatternEvaluation Evaluate(BarSnapshot snapshot)
    {
        if (snapshot.BollingerUpper <= 0m || snapshot.BollingerMiddle <= 0m)
        {
            return new PatternEvaluation(Name, 0m, Reason: "bb_warmup");
        }

        var touchedUpper = snapshot.High >= snapshot.BollingerUpper;
        var closedBelowMid = snapshot.Close < snapshot.BollingerMiddle;
        var bearBar = snapshot.Close < snapshot.Open;

        if (!touchedUpper)
        {
            return new PatternEvaluation(Name, 0m,
                Reason: $"high={snapshot.High}<upper={snapshot.BollingerUpper}");
        }

        if (closedBelowMid && bearBar)
        {
            return new PatternEvaluation(Name, 1m, Reason: "full_reversal");
        }
        if (closedBelowMid)
        {
            return new PatternEvaluation(Name, 0.6m, Reason: "close_below_mid_no_bear_bar");
        }
        if (bearBar)
        {
            return new PatternEvaluation(Name, 0.4m, Reason: "bear_bar_only");
        }
        return new PatternEvaluation(Name, 0m, Reason: "touched_upper_no_reversal");
    }
}
