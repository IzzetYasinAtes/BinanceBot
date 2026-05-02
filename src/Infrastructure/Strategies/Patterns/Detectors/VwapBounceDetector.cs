using BinanceBot.Application.Strategies.Patterns;

namespace BinanceBot.Infrastructure.Strategies.Patterns.Detectors;

/// <summary>
/// Loop 81 — P2 VWAP-Bounce (spec §2 #2, weight 2). Önceki bar VWAP altında,
/// son bar VWAP üstüne çıktı + RSI nötr-yukarı bölge + EMA21 yukarı eğim.
///
/// Trigger:
/// <list type="bullet">
///   <item>PrevClose &lt; Vwap AND Close &gt; Vwap (bounce above).</item>
///   <item>40 &lt; RSI14 &lt; 65 (aşırı bölgede değil).</item>
///   <item>EMA21 &gt; EMA21Prev5 (yukarı eğim).</item>
/// </list>
/// Skor: hepsi 1.0; sadece ana cross + RSI OK ama eğim hafif ⇒ 0.5.
/// </summary>
public sealed class VwapBounceDetector : IPatternDetector
{
    public string Name => "vwap_bounce";
    public decimal DefaultWeight => 2m;
    public bool IsHardGate => false;

    private const decimal RsiMin = 40m;
    private const decimal RsiMax = 65m;

    public PatternEvaluation Evaluate(BarSnapshot snapshot)
    {
        if (snapshot.Vwap <= 0m)
        {
            return new PatternEvaluation(Name, 0m, Reason: "vwap_zero");
        }

        var crossed = snapshot.PrevClose < snapshot.Vwap && snapshot.Close > snapshot.Vwap;
        if (!crossed)
        {
            return new PatternEvaluation(Name, 0m, Reason: "no_cross");
        }

        var rsiOk = snapshot.Rsi14 > RsiMin && snapshot.Rsi14 < RsiMax;
        if (!rsiOk)
        {
            return new PatternEvaluation(Name, 0m, Reason: "rsi_out_of_range");
        }

        var slopeUp = snapshot.Ema21 > snapshot.Ema21Prev5;
        if (slopeUp)
        {
            return new PatternEvaluation(Name, 1m,
                Reason: "vwap_bounce_full",
                Payload: new { vwap = snapshot.Vwap, rsi = snapshot.Rsi14 });
        }

        // Eğim yok ama cross + RSI OK → yarı puan
        return new PatternEvaluation(Name, 0.5m,
            Reason: "vwap_bounce_no_slope",
            Payload: new { vwap = snapshot.Vwap, rsi = snapshot.Rsi14 });
    }
}
