using BinanceBot.Application.Strategies.Patterns;
using BinanceBot.Domain.Common;

namespace BinanceBot.Infrastructure.Strategies.Patterns.Detectors;

/// <summary>
/// Loop 81 — P7 MACD-Zero-Cross (spec §2 #7, weight 2). MACD line önceki bar
/// 0'ın altında, son bar 0'ın üstüne geçti.
///
/// Trigger:
/// <list type="bullet">
///   <item>MacdLinePrev &lt; 0 AND MacdLine &gt; 0.</item>
/// </list>
/// Skor: cross + büyüklük &gt; 0.00005 × Close ⇒ 1.0; küçük cross ⇒ 0.5.
/// </summary>
public sealed class MacdZeroCrossDetector : IPatternDetector
{
    public string Name => "macd_zero_cross";
    public decimal DefaultWeight => 2m;
    public bool IsHardGate => false;
    public TradeDirection? Direction => TradeDirection.Long;

    private const decimal MinMagnitudeRatio = 0.00005m;

    public PatternEvaluation Evaluate(BarSnapshot snapshot)
    {
        var crossUp = snapshot.MacdLinePrev < 0m && snapshot.MacdLine > 0m;
        if (!crossUp)
        {
            return new PatternEvaluation(Name, 0m, Reason: "no_cross_up");
        }

        if (snapshot.Close <= 0m)
        {
            return new PatternEvaluation(Name, 0m, Reason: "close_zero");
        }

        var minMag = MinMagnitudeRatio * snapshot.Close;
        if (snapshot.MacdLine >= minMag)
        {
            return new PatternEvaluation(Name, 1m,
                Reason: "macd_cross_strong",
                Payload: new { macd = snapshot.MacdLine, macdPrev = snapshot.MacdLinePrev });
        }

        return new PatternEvaluation(Name, 0.5m,
            Reason: "macd_cross_weak",
            Payload: new { macd = snapshot.MacdLine, macdPrev = snapshot.MacdLinePrev });
    }
}
