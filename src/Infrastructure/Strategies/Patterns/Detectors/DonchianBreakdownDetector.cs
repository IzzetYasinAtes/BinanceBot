using BinanceBot.Application.Strategies.Patterns;
using BinanceBot.Domain.Common;

namespace BinanceBot.Infrastructure.Strategies.Patterns.Detectors;

/// <summary>
/// Loop 92 — Short-bias Donchian breakdown (ADR-0025). VolumeSpikeDonchian'ın
/// downtrend versiyonu. Fiyat 20-bar low altına kırıldı.
///
/// Trigger:
/// <list type="bullet">
///   <item>Close &lt; DonchianLow20 (20-bar low aşağı kırma).</item>
///   <item>Volume &gt; AvgVolume20 (volume confirmation, optional).</item>
/// </list>
/// </summary>
public sealed class DonchianBreakdownDetector : IPatternDetector
{
    public string Name => "donchian_breakdown";
    public decimal DefaultWeight => 1.5m;
    public bool IsHardGate => false;
    public TradeDirection? Direction => TradeDirection.Short;

    public PatternEvaluation Evaluate(BarSnapshot snapshot)
    {
        if (snapshot.DonchianLow20 <= 0m)
        {
            return new PatternEvaluation(Name, 0m, Reason: "donchian_warmup");
        }

        var brokeDown = snapshot.Close < snapshot.DonchianLow20;
        if (!brokeDown)
        {
            return new PatternEvaluation(Name, 0m,
                Reason: $"close={snapshot.Close}>donchianLow={snapshot.DonchianLow20}");
        }

        var volumeConfirms = snapshot.AvgVolume20 > 0m && snapshot.Volume > snapshot.AvgVolume20;
        if (volumeConfirms)
        {
            return new PatternEvaluation(Name, 1m, Reason: "breakdown_with_volume");
        }
        return new PatternEvaluation(Name, 0.6m, Reason: "breakdown_no_volume");
    }
}
