using BinanceBot.Application.Strategies.Patterns;
using BinanceBot.Domain.Common;

namespace BinanceBot.Infrastructure.Strategies.Patterns.Detectors;

/// <summary>
/// Loop 92 — Short-bias Bollinger squeeze break down (ADR-0025). EmaSqueezeBreak
/// simetrik. BB sıkışması sonrası fiyat aşağı kırılır.
///
/// Trigger:
/// <list type="bullet">
///   <item>BollingerBandWidth ≈ Min6 (squeeze ongoing).</item>
///   <item>Close &lt; BollingerLower (alt banda kırma).</item>
///   <item>EMA21 slope (Ema21 - Ema21Prev5) &lt; 0 (downtrend confirmation).</item>
/// </list>
/// </summary>
public sealed class BollingerSqueezeBreakDownDetector : IPatternDetector
{
    public string Name => "bollinger_squeeze_break_down";
    public decimal DefaultWeight => 1.5m;
    public bool IsHardGate => false;
    public TradeDirection? Direction => TradeDirection.Short;

    public PatternEvaluation Evaluate(BarSnapshot snapshot)
    {
        if (snapshot.BollingerLower <= 0m || snapshot.BollingerBandWidth <= 0m)
        {
            return new PatternEvaluation(Name, 0m, Reason: "bb_warmup");
        }

        // Squeeze: current BBW son 6 barın min'ine yakın (1.2x içinde)
        var squeeze = snapshot.BollingerBandWidth <= snapshot.BollingerBandWidthMin6 * 1.2m;
        if (!squeeze)
        {
            return new PatternEvaluation(Name, 0m,
                Reason: $"bbw={snapshot.BollingerBandWidth}>min6*1.2");
        }

        var brokeDown = snapshot.Close < snapshot.BollingerLower;
        var emaDownSlope = snapshot.Ema21 - snapshot.Ema21Prev5 < 0m;

        if (brokeDown && emaDownSlope)
        {
            return new PatternEvaluation(Name, 1m, Reason: "squeeze_break_down_with_trend");
        }
        if (brokeDown)
        {
            return new PatternEvaluation(Name, 0.6m, Reason: "squeeze_break_down_no_trend");
        }
        return new PatternEvaluation(Name, 0m, Reason: "squeeze_no_break");
    }
}
