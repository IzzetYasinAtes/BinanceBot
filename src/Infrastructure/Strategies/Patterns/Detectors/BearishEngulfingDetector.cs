using BinanceBot.Application.Strategies.Patterns;
using BinanceBot.Domain.Common;

namespace BinanceBot.Infrastructure.Strategies.Patterns.Detectors;

/// <summary>
/// Loop 92 — Short-bias bearish engulfing detector (ADR-0025). BullishEngulfing
/// simetrik. Önceki bar bull, son bar bear ve önceki bar gövdesini kapsıyor.
///
/// Trigger:
/// <list type="bullet">
///   <item>PrevClose &gt; PrevOpen (önceki bar bull).</item>
///   <item>Close &lt; Open (son bar bear).</item>
///   <item>Open &gt;= PrevClose AND Close &lt;= PrevOpen (gövde engulf).</item>
/// </list>
/// Skor: full engulf güçlü body ⇒ 1.0; weak body ⇒ 0.5; sadece close prev open altında ⇒ 0.5.
/// </summary>
public sealed class BearishEngulfingDetector : IPatternDetector
{
    public string Name => "bearish_engulfing";
    public decimal DefaultWeight => 2m;
    public bool IsHardGate => false;
    public TradeDirection? Direction => TradeDirection.Short;

    public PatternEvaluation Evaluate(BarSnapshot snapshot)
    {
        var prevBull = snapshot.PrevClose > snapshot.PrevOpen;
        var currBear = snapshot.Close < snapshot.Open;

        if (!prevBull || !currBear)
        {
            return new PatternEvaluation(Name, 0m,
                Reason: $"prevBull={prevBull} currBear={currBear}");
        }

        var fullEngulf = snapshot.Open >= snapshot.PrevClose && snapshot.Close <= snapshot.PrevOpen;
        if (fullEngulf)
        {
            var prevBody = Math.Abs(snapshot.PrevOpen - snapshot.PrevClose);
            var currBody = snapshot.Open - snapshot.Close;
            if (currBody >= prevBody)
            {
                return new PatternEvaluation(Name, 1m,
                    Reason: "bearish_engulfing_full",
                    Payload: new { prevBody, currBody });
            }
            return new PatternEvaluation(Name, 0.5m,
                Reason: "bearish_engulfing_weak_body",
                Payload: new { prevBody, currBody });
        }

        if (snapshot.Close <= snapshot.PrevOpen)
        {
            return new PatternEvaluation(Name, 0.5m,
                Reason: "bearish_close_below_prev_open");
        }

        return new PatternEvaluation(Name, 0m, Reason: "no_engulf");
    }
}
