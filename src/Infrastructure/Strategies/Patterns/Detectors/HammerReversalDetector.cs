using BinanceBot.Application.Strategies.Patterns;
using BinanceBot.Domain.Common;

namespace BinanceBot.Infrastructure.Strategies.Patterns.Detectors;

/// <summary>
/// Loop 81 — P9 Hammer (spec §2 #9, weight 2). Lower wick &gt; body × 2,
/// upper wick küçük, close &gt; open.
///
/// Trigger:
/// <list type="bullet">
///   <item>Close &gt; Open (bull bar).</item>
///   <item>LowerWick = min(Open,Close) - Low &gt; body × 2.</item>
///   <item>UpperWick = High - max(Open,Close) &lt; body.</item>
/// </list>
/// Skor: lowerWick &gt; body × 2.5 ⇒ 1.0; 2.0–2.5 arası ⇒ 0.5.
/// </summary>
public sealed class HammerReversalDetector : IPatternDetector
{
    public string Name => "hammer_reversal";
    public decimal DefaultWeight => 2m;
    public bool IsHardGate => false;
    public TradeDirection? Direction => TradeDirection.Long;

    private const decimal LowerWickFull = 2.5m;
    private const decimal LowerWickWeak = 2.0m;

    public PatternEvaluation Evaluate(BarSnapshot snapshot)
    {
        if (snapshot.Close <= snapshot.Open)
        {
            return new PatternEvaluation(Name, 0m, Reason: "not_bull_bar");
        }

        var body = snapshot.Close - snapshot.Open;
        if (body <= 0m)
        {
            return new PatternEvaluation(Name, 0m, Reason: "zero_body");
        }

        var lowerWick = Math.Min(snapshot.Open, snapshot.Close) - snapshot.Low;
        var upperWick = snapshot.High - Math.Max(snapshot.Open, snapshot.Close);

        if (lowerWick <= 0m || upperWick >= body)
        {
            return new PatternEvaluation(Name, 0m,
                Reason: $"lowerWick={lowerWick} upperWick={upperWick} body={body}");
        }

        var ratio = lowerWick / body;
        if (ratio >= LowerWickFull)
        {
            return new PatternEvaluation(Name, 1m,
                Reason: "hammer_full",
                Payload: new { ratio, body, lowerWick, upperWick });
        }
        if (ratio >= LowerWickWeak)
        {
            return new PatternEvaluation(Name, 0.5m,
                Reason: "hammer_weak",
                Payload: new { ratio, body });
        }

        return new PatternEvaluation(Name, 0m, Reason: "lower_wick_too_short");
    }
}
