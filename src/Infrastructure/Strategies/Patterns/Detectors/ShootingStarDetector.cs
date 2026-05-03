using BinanceBot.Application.Strategies.Patterns;
using BinanceBot.Domain.Common;

namespace BinanceBot.Infrastructure.Strategies.Patterns.Detectors;

/// <summary>
/// Loop 92 — Short-bias shooting star detector (ADR-0025). HammerReversal'ın
/// ters versiyonu. Üst gölge gövdenin 2 katından büyük, alt gölge minimal,
/// kapanış open'ın altında veya yakın → reddediş tepe sinyali.
///
/// Trigger:
/// <list type="bullet">
///   <item>UpperShadow &gt; Body × 2.</item>
///   <item>LowerShadow &lt; Body × 0.5.</item>
///   <item>Close &lt;= Open + bar range × 0.3 (kapanış üst gölgenin altında).</item>
/// </list>
/// </summary>
public sealed class ShootingStarDetector : IPatternDetector
{
    public string Name => "shooting_star";
    public decimal DefaultWeight => 2m;
    public bool IsHardGate => false;
    public TradeDirection? Direction => TradeDirection.Short;

    public PatternEvaluation Evaluate(BarSnapshot snapshot)
    {
        var range = snapshot.High - snapshot.Low;
        if (range <= 0m) return new PatternEvaluation(Name, 0m, Reason: "no_range");

        var body = Math.Abs(snapshot.Close - snapshot.Open);
        var upperShadow = snapshot.High - Math.Max(snapshot.Open, snapshot.Close);
        var lowerShadow = Math.Min(snapshot.Open, snapshot.Close) - snapshot.Low;

        if (body <= 0m) return new PatternEvaluation(Name, 0m, Reason: "doji_no_body");

        if (upperShadow < body * 2m)
        {
            return new PatternEvaluation(Name, 0m,
                Reason: $"upper_shadow={upperShadow}<body*2={body * 2m}");
        }

        if (lowerShadow > body * 0.5m)
        {
            return new PatternEvaluation(Name, 0m,
                Reason: $"lower_shadow={lowerShadow}>body*0.5");
        }

        var rejectThreshold = snapshot.Low + range * 0.3m;
        if (snapshot.Close > rejectThreshold)
        {
            return new PatternEvaluation(Name, 0.5m, Reason: "weak_shooting_star_close_high");
        }

        return new PatternEvaluation(Name, 1m,
            Reason: "shooting_star_full",
            Payload: new { upperShadow, lowerShadow, body });
    }
}
