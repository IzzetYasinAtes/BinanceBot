namespace BinanceBot.Application.Strategies.Patterns;

/// <summary>
/// Loop 81 — Tek bir <see cref="IPatternDetector"/> 'ün <see cref="BarSnapshot"/>
/// üzerinde değerlendirme sonucu (ADR-0024 §24.8). Stateless value object;
/// persistence yok — sadece composer akışında ContextJson payload'ı için kullanılır.
///
/// <c>Score</c> [0..1] continuous (true/false yerine), composer ağırlıklarla
/// harmanlar. Skoru 0..1 dışına çıkan detector'lar composer tarafından
/// loglanır + clamp edilir (defansif).
/// </summary>
/// <param name="Name">Detector stable identifier — log + ContextJson key.</param>
/// <param name="Score">[0..1] continuous; 0 = pattern yok, 1 = tam tetiklendi.</param>
/// <param name="Reason">Opsiyonel insan-okur açıklama (log + audit).</param>
/// <param name="Payload">Opsiyonel zengin context (detector'ın kullandığı sayılar).</param>
public sealed record PatternEvaluation(
    string Name,
    decimal Score,
    string? Reason = null,
    object? Payload = null);
