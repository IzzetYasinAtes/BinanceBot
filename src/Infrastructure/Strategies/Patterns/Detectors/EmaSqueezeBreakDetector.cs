using BinanceBot.Application.Strategies.Patterns;

namespace BinanceBot.Infrastructure.Strategies.Patterns.Detectors;

/// <summary>
/// Loop 81 — P1 EMA-Squeeze-Break (spec §2 #1, weight 3). BBW sıkışmış,
/// EMA9/21 yukarı cross + volume surge + close > EMA21.
///
/// Trigger:
/// <list type="bullet">
///   <item>BBW &lt; 0.0025 (sıkışma rejimi).</item>
///   <item>EMA9 &gt; EMA21 (kısa vade trend yukarı).</item>
///   <item>Close &gt; EMA21 (fiyat band üstünde).</item>
///   <item>Volume &gt; AvgVolume20 × 1.3 (kırılma onayı).</item>
/// </list>
///
/// Skor: hepsi sağlanırsa 1.0; volume zayıfsa (× 1.0–1.3 arası) 0.5; aksi 0.
/// </summary>
public sealed class EmaSqueezeBreakDetector : IPatternDetector
{
    public string Name => "ema_squeeze_break";
    public decimal DefaultWeight => 3m;
    public bool IsHardGate => false;

    private const decimal SqueezeBbwMax = 0.0025m;
    private const decimal VolumeSurgeMul = 1.3m;
    private const decimal VolumeWeakMul = 1.0m;

    public PatternEvaluation Evaluate(BarSnapshot snapshot)
    {
        var squeezed = snapshot.BollingerBandWidth > 0m
            && snapshot.BollingerBandWidth < SqueezeBbwMax;
        var emaCross = snapshot.Ema9 > snapshot.Ema21;
        var aboveEma21 = snapshot.Close > snapshot.Ema21;

        if (!squeezed || !emaCross || !aboveEma21)
        {
            return new PatternEvaluation(Name, 0m,
                Reason: $"squeezed={squeezed} cross={emaCross} above={aboveEma21}");
        }

        if (snapshot.AvgVolume20 <= 0m)
        {
            return new PatternEvaluation(Name, 0m, Reason: "avg_volume_zero");
        }

        var volRatio = snapshot.Volume / snapshot.AvgVolume20;
        if (volRatio >= VolumeSurgeMul)
        {
            return new PatternEvaluation(Name, 1m,
                Reason: "squeeze_break_full",
                Payload: new { bbw = snapshot.BollingerBandWidth, volRatio });
        }
        if (volRatio >= VolumeWeakMul)
        {
            return new PatternEvaluation(Name, 0.5m,
                Reason: "squeeze_break_weak_volume",
                Payload: new { bbw = snapshot.BollingerBandWidth, volRatio });
        }

        return new PatternEvaluation(Name, 0m, Reason: "volume_too_low");
    }
}
