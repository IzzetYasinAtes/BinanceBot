using BinanceBot.Application.Strategies.Patterns;
using BinanceBot.Domain.Common;

namespace BinanceBot.Infrastructure.Strategies.Patterns.Detectors;

/// <summary>
/// Loop 92 — Short-bias RSI overbought pullback (ADR-0025). RsiOversoldRecovery
/// simetrik. RSI prev 70+ (overbought), şimdi 70 altına döndü → pullback başlangıcı.
///
/// Trigger:
/// <list type="bullet">
///   <item>Rsi14Prev &gt; 70 (overbought eşiğinin üstündeydi).</item>
///   <item>Rsi14 &lt; 70 (overbought'tan çıkış).</item>
///   <item>Close &lt; Open (bear bar confirmation).</item>
/// </list>
/// Skor: full cross + bear bar ⇒ 1.0; sadece RSI cross ⇒ 0.5.
/// </summary>
public sealed class RsiOverboughtPullbackDetector : IPatternDetector
{
    public string Name => "rsi_overbought_pullback";
    public decimal DefaultWeight => 1.5m;
    public bool IsHardGate => false;
    public TradeDirection? Direction => TradeDirection.Short;

    private const decimal OverboughtThreshold = 70m;

    public PatternEvaluation Evaluate(BarSnapshot snapshot)
    {
        if (snapshot.Rsi14 <= 0m || snapshot.Rsi14Prev <= 0m)
        {
            return new PatternEvaluation(Name, 0m, Reason: "rsi_warmup");
        }

        var wasOverbought = snapshot.Rsi14Prev > OverboughtThreshold;
        var crossedDown = snapshot.Rsi14 < OverboughtThreshold;
        var bearBar = snapshot.Close < snapshot.Open;

        if (!wasOverbought || !crossedDown)
        {
            return new PatternEvaluation(Name, 0m,
                Reason: $"prev={snapshot.Rsi14Prev} curr={snapshot.Rsi14} no_cross");
        }

        if (bearBar)
        {
            return new PatternEvaluation(Name, 1m, Reason: "rsi_pullback_with_bear_bar");
        }
        return new PatternEvaluation(Name, 0.5m, Reason: "rsi_pullback_no_bear_confirmation");
    }
}
