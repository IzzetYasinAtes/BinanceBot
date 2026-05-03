using BinanceBot.Application.Strategies.Patterns;
using BinanceBot.Domain.Common;

namespace BinanceBot.Infrastructure.Strategies.Patterns.Detectors;

/// <summary>
/// Loop 81 — P4 RSI-Oversold-Recovery (spec §2 #4, weight 2). RSI14 önceki
/// bar &lt; 35 ve şimdi yukarı dönüyor (Rsi14 &gt; Rsi14Prev) + son bar close
/// önceki bar close üstünde.
///
/// Trigger:
/// <list type="bullet">
///   <item>Rsi14Prev &lt; 35 (oversold).</item>
///   <item>Rsi14 &gt; Rsi14Prev (recovery momentum).</item>
///   <item>Close &gt; PrevClose (fiyat da yukarı).</item>
/// </list>
/// Skor: hepsi 1.0; RSI14 hâlâ &lt; 40 ama prev &gt; 35 (sıkı eşik dışı) ⇒ 0.5.
/// </summary>
public sealed class RsiOversoldRecoveryDetector : IPatternDetector
{
    public string Name => "rsi_oversold_recovery";
    public decimal DefaultWeight => 2m;
    public bool IsHardGate => false;
    public TradeDirection? Direction => TradeDirection.Long;

    private const decimal OversoldStrict = 35m;
    private const decimal OversoldWeak = 40m;

    public PatternEvaluation Evaluate(BarSnapshot snapshot)
    {
        var risingRsi = snapshot.Rsi14 > snapshot.Rsi14Prev;
        var risingPrice = snapshot.Close > snapshot.PrevClose;

        if (!risingRsi || !risingPrice)
        {
            return new PatternEvaluation(Name, 0m,
                Reason: $"risingRsi={risingRsi} risingPrice={risingPrice}");
        }

        if (snapshot.Rsi14Prev < OversoldStrict)
        {
            return new PatternEvaluation(Name, 1m,
                Reason: "rsi_recovery_full",
                Payload: new { rsi = snapshot.Rsi14, rsiPrev = snapshot.Rsi14Prev });
        }
        if (snapshot.Rsi14Prev < OversoldWeak)
        {
            return new PatternEvaluation(Name, 0.5m,
                Reason: "rsi_recovery_weak",
                Payload: new { rsi = snapshot.Rsi14, rsiPrev = snapshot.Rsi14Prev });
        }

        return new PatternEvaluation(Name, 0m, Reason: "not_oversold");
    }
}
