using BinanceBot.Application.Strategies.Patterns;

namespace BinanceBot.Infrastructure.Strategies.Patterns.Detectors;

/// <summary>
/// Loop 81 — H2 SpreadGuardGate (spec §2 hard-gates). Hard-gate (score=0
/// ⇒ composer skip).
///
/// Trigger: <c>SpreadPct &lt; 0.001</c> (XRP/ADA korunur — bu coinler için
/// %0.1 spread normal kabul edilir; daha yüksekse fee + slippage pozisyonu
/// yer).
///
/// BookTicker yoksa snapshot.SpreadPct = 1.0 (fallback) ⇒ gate fail ⇒ skip.
/// </summary>
public sealed class SpreadGuardGate : IPatternDetector
{
    public string Name => "spread_guard_gate";
    public decimal DefaultWeight => 0m;
    public bool IsHardGate => true;

    private const decimal MaxSpreadPct = 0.001m; // %0.1

    public PatternEvaluation Evaluate(BarSnapshot snapshot)
    {
        if (snapshot.SpreadPct < MaxSpreadPct)
        {
            return new PatternEvaluation(Name, 1m,
                Reason: "spread_ok",
                Payload: new { spreadPct = snapshot.SpreadPct });
        }

        return new PatternEvaluation(Name, 0m,
            Reason: "spread_too_wide",
            Payload: new { spreadPct = snapshot.SpreadPct, threshold = MaxSpreadPct });
    }
}
