using BinanceBot.Application.Strategies.Patterns;

namespace BinanceBot.Infrastructure.Strategies.Patterns.Detectors;

/// <summary>
/// Loop 81 — P10 BollingerLowerReversal (spec §2 #10, weight 2). Önceki bar
/// alt band'a değdi/altına indi, son bar yukarı dönüyor.
///
/// Trigger:
/// <list type="bullet">
///   <item>PrevLow &lt;= BollingerLower × (1 + buffer) (alt band touch).</item>
///   <item>Close &gt; PrevClose (yukarı dönüş).</item>
///   <item>Close &gt; BollingerLower (band üstüne döndü).</item>
/// </list>
/// Skor: Rsi14 &gt; Rsi14Prev ek olarak rising ⇒ 1.0; sadece touch + bounce ⇒ 0.5.
/// </summary>
public sealed class BollingerLowerReversalDetector : IPatternDetector
{
    public string Name => "bollinger_lower_reversal";
    public decimal DefaultWeight => 2m;
    public bool IsHardGate => false;

    private const decimal LowerTouchBuffer = 0.0005m; // %0.05

    public PatternEvaluation Evaluate(BarSnapshot snapshot)
    {
        if (snapshot.BollingerLower <= 0m)
        {
            return new PatternEvaluation(Name, 0m, Reason: "lower_band_zero");
        }

        var touchThreshold = snapshot.BollingerLower * (1m + LowerTouchBuffer);
        var touched = snapshot.PrevLow <= touchThreshold;
        var bounced = snapshot.Close > snapshot.PrevClose && snapshot.Close > snapshot.BollingerLower;

        if (!touched || !bounced)
        {
            return new PatternEvaluation(Name, 0m,
                Reason: $"touched={touched} bounced={bounced}");
        }

        var rsiRising = snapshot.Rsi14 > snapshot.Rsi14Prev;
        if (rsiRising)
        {
            return new PatternEvaluation(Name, 1m,
                Reason: "bb_lower_reversal_full",
                Payload: new { lower = snapshot.BollingerLower, prevLow = snapshot.PrevLow });
        }

        return new PatternEvaluation(Name, 0.5m,
            Reason: "bb_lower_reversal_no_rsi",
            Payload: new { lower = snapshot.BollingerLower, prevLow = snapshot.PrevLow });
    }
}
