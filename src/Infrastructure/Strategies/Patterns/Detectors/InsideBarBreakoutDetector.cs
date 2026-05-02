using BinanceBot.Application.Strategies.Patterns;

namespace BinanceBot.Infrastructure.Strategies.Patterns.Detectors;

/// <summary>
/// Loop 81 — P3 Inside-Bar-Breakout (spec §2 #3, weight 3). Önceki bar inside
/// (bir önceki bar'ın range'i içinde), son bar inside-bar high'ını kırdı.
///
/// Trigger (3 bar gerekli):
/// <list type="bullet">
///   <item>RecentBars[-2] inside RecentBars[-3] (high &lt; prev high, low &gt; prev low).</item>
///   <item>Close &gt; RecentBars[-2].High (kırılma yukarı).</item>
///   <item>Volume &gt; AvgVolume20 × 1.5.</item>
/// </list>
/// Skor: hepsi 1.0; volume zayıfsa (1.0–1.5) 0.5.
/// </summary>
public sealed class InsideBarBreakoutDetector : IPatternDetector
{
    public string Name => "inside_bar_breakout";
    public decimal DefaultWeight => 3m;
    public bool IsHardGate => false;

    private const decimal VolumeSurgeMul = 1.5m;
    private const decimal VolumeWeakMul = 1.0m;

    public PatternEvaluation Evaluate(BarSnapshot snapshot)
    {
        var bars = snapshot.RecentBars;
        if (bars.Count < 3)
        {
            return new PatternEvaluation(Name, 0m, Reason: "insufficient_history");
        }

        var insideBar = bars[^2]; // önceki bar
        var motherBar = bars[^3]; // inside-bar'ın referansı

        var isInside = insideBar.HighPrice < motherBar.HighPrice
            && insideBar.LowPrice > motherBar.LowPrice;
        if (!isInside)
        {
            return new PatternEvaluation(Name, 0m, Reason: "not_inside_bar");
        }

        var brokeUp = snapshot.Close > insideBar.HighPrice;
        if (!brokeUp)
        {
            return new PatternEvaluation(Name, 0m, Reason: "no_breakout");
        }

        if (snapshot.AvgVolume20 <= 0m)
        {
            return new PatternEvaluation(Name, 0m, Reason: "avg_volume_zero");
        }

        var volRatio = snapshot.Volume / snapshot.AvgVolume20;
        if (volRatio >= VolumeSurgeMul)
        {
            return new PatternEvaluation(Name, 1m,
                Reason: "inside_bar_break_full",
                Payload: new { motherHigh = motherBar.HighPrice, insideHigh = insideBar.HighPrice, volRatio });
        }
        if (volRatio >= VolumeWeakMul)
        {
            return new PatternEvaluation(Name, 0.5m,
                Reason: "inside_bar_break_weak_volume",
                Payload: new { volRatio });
        }

        return new PatternEvaluation(Name, 0m, Reason: "volume_too_low");
    }
}
