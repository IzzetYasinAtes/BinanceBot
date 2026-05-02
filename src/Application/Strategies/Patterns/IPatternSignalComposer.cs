namespace BinanceBot.Application.Strategies.Patterns;

/// <summary>
/// Loop 81 — Detector çıktılarını ağırlıklı skor topladıktan sonra emit/skip
/// kararı + geometri üreten port (ADR-0024 §24.9).
///
/// Composer iş kuralları:
/// <list type="number">
///   <item>Hard-gate disiplini: <c>IsHardGate==true &amp;&amp; Score==0</c> ⇒ skip.</item>
///   <item>Soft filter (örn. <c>AdxRegimeFilter</c>): regime dışıysa tüm skorlar × multiplier.</item>
///   <item>Ağırlıklı toplam: <c>Σ(Score × Weight)</c> ≥ <c>RequiredScore</c> ⇒ Emit.</item>
///   <item>Geometri ATR-bazlı: SL = max(ATR×SlMul, MinSlPct×Close); TP = Entry × (1 + SL × TpRatio).</item>
/// </list>
/// </summary>
public interface IPatternSignalComposer
{
    /// <summary>
    /// Detector çıktılarını harmanlayıp emit/skip kararı döner. Çıktı emit
    /// olduğunda <c>EntryPrice/StopPrice/TakeProfitPrice/MaxHoldMinutes</c>
    /// dolu olur; skip ise <c>SkipReason</c> dolu olur.
    /// </summary>
    CompositeSignalDecision Compose(
        BarSnapshot snapshot,
        IReadOnlyList<PatternEvaluation> evaluations,
        PatternComposerOptions options);
}
