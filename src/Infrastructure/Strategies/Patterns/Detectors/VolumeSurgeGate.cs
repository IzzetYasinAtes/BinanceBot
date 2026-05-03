using BinanceBot.Application.Strategies.Patterns;
using BinanceBot.Domain.Common;

namespace BinanceBot.Infrastructure.Strategies.Patterns.Detectors;

/// <summary>
/// Loop 81 — H1 VolumeSurgeGate (spec §2 hard-gates). Hard-gate (score=0
/// ⇒ composer skip).
///
/// Trigger: <c>Volume &gt; AvgVolume20 × 1.0</c> (warmup &lt; 20 bypass = score=1).
///
/// Likidite koruması: bar düşük volume ile pattern tetiklenirse genelde
/// fake-out (whipsaw); volume gate basit eşik ile çoğunu eler.
/// </summary>
public sealed class VolumeSurgeGate : IPatternDetector
{
    public string Name => "volume_surge_gate";
    public decimal DefaultWeight => 0m; // Hard-gate, ağırlığı skor toplamına katkısı 0
    public bool IsHardGate => true;
    public TradeDirection? Direction => null;  // Neutral

    private const decimal SurgeMul = 1.0m;

    public PatternEvaluation Evaluate(BarSnapshot snapshot)
    {
        // Warmup yetersiz (avg = 0) ⇒ gate açık, skor=1 (bypass)
        if (snapshot.AvgVolume20 <= 0m)
        {
            return new PatternEvaluation(Name, 1m, Reason: "warmup_bypass");
        }

        var ratio = snapshot.Volume / snapshot.AvgVolume20;
        if (ratio >= SurgeMul)
        {
            return new PatternEvaluation(Name, 1m,
                Reason: "volume_surge_pass",
                Payload: new { ratio });
        }

        // Hard-gate fail
        return new PatternEvaluation(Name, 0m,
            Reason: "volume_below_average",
            Payload: new { ratio });
    }
}
