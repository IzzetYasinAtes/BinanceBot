using BinanceBot.Application.Strategies.Patterns;
using BinanceBot.Domain.Common;

namespace BinanceBot.Infrastructure.Strategies.Patterns.Detectors;

/// <summary>
/// Loop 81 — P8 BullishEngulfing (spec §2 #8, weight 2). Önceki bar bear,
/// son bar bull ve önceki bar gövdesini kapsıyor.
///
/// Trigger:
/// <list type="bullet">
///   <item>PrevClose &lt; PrevOpen (önceki bar bear).</item>
///   <item>Close &gt; Open (son bar bull).</item>
///   <item>Open &lt;= PrevClose AND Close &gt;= PrevOpen (gövde engulf).</item>
/// </list>
/// Skor: full engulf ⇒ 1.0; close prev open üstünde ama body küçük ⇒ 0.5.
/// </summary>
public sealed class BullishEngulfingDetector : IPatternDetector
{
    public string Name => "bullish_engulfing";
    public decimal DefaultWeight => 2m;
    public bool IsHardGate => false;
    public TradeDirection? Direction => TradeDirection.Long;

    public PatternEvaluation Evaluate(BarSnapshot snapshot)
    {
        var prevBear = snapshot.PrevClose < snapshot.PrevOpen;
        var currBull = snapshot.Close > snapshot.Open;

        if (!prevBear || !currBull)
        {
            return new PatternEvaluation(Name, 0m,
                Reason: $"prevBear={prevBear} currBull={currBull}");
        }

        var fullEngulf = snapshot.Open <= snapshot.PrevClose && snapshot.Close >= snapshot.PrevOpen;
        if (fullEngulf)
        {
            var prevBody = Math.Abs(snapshot.PrevOpen - snapshot.PrevClose);
            var currBody = snapshot.Close - snapshot.Open;
            // body güçlü mü? curr body >= prev body (klasik)
            if (currBody >= prevBody)
            {
                return new PatternEvaluation(Name, 1m,
                    Reason: "bullish_engulfing_full",
                    Payload: new { prevBody, currBody });
            }
            return new PatternEvaluation(Name, 0.5m,
                Reason: "bullish_engulfing_weak_body",
                Payload: new { prevBody, currBody });
        }

        // Sadece close prev open üstünde (yarı engulf)
        if (snapshot.Close >= snapshot.PrevOpen)
        {
            return new PatternEvaluation(Name, 0.5m,
                Reason: "bullish_close_above_prev_open");
        }

        return new PatternEvaluation(Name, 0m, Reason: "no_engulf");
    }
}
