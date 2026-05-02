using BinanceBot.Application.Strategies.Patterns;

namespace BinanceBot.Infrastructure.Strategies.Patterns.Detectors;

/// <summary>
/// Loop 81 — P5 Volume-Spike-Donchian-Break (spec §2 #5, weight 4 — en güçlü).
/// Volume &gt; avg × 2.5 + close Donchian-20 üstünü kırdı.
///
/// Trigger:
/// <list type="bullet">
///   <item>Volume &gt; AvgVolume20 × 2.5.</item>
///   <item>Close &gt; DonchianHigh20 (range break).</item>
/// </list>
/// Skor: hepsi 1.0; volume 1.5–2.5 arası ama break var ⇒ 0.5.
/// </summary>
public sealed class VolumeSpikeDonchianDetector : IPatternDetector
{
    public string Name => "volume_spike_donchian";
    public decimal DefaultWeight => 4m;
    public bool IsHardGate => false;

    private const decimal SpikeFull = 2.5m;
    private const decimal SpikeWeak = 1.5m;

    public PatternEvaluation Evaluate(BarSnapshot snapshot)
    {
        if (snapshot.AvgVolume20 <= 0m)
        {
            return new PatternEvaluation(Name, 0m, Reason: "avg_volume_zero");
        }
        if (snapshot.DonchianHigh20 <= 0m)
        {
            return new PatternEvaluation(Name, 0m, Reason: "donchian_unavailable");
        }

        var brokeUp = snapshot.Close > snapshot.DonchianHigh20;
        if (!brokeUp)
        {
            return new PatternEvaluation(Name, 0m, Reason: "no_break");
        }

        var volRatio = snapshot.Volume / snapshot.AvgVolume20;
        if (volRatio >= SpikeFull)
        {
            return new PatternEvaluation(Name, 1m,
                Reason: "volume_spike_donchian_full",
                Payload: new { donchian = snapshot.DonchianHigh20, volRatio });
        }
        if (volRatio >= SpikeWeak)
        {
            return new PatternEvaluation(Name, 0.5m,
                Reason: "volume_spike_donchian_weak",
                Payload: new { donchian = snapshot.DonchianHigh20, volRatio });
        }

        return new PatternEvaluation(Name, 0m, Reason: "volume_too_low");
    }
}
