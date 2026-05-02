namespace BinanceBot.Application.Strategies.Patterns;

/// <summary>
/// Loop 81 — DI kayıtlı tüm <see cref="IPatternDetector"/> instance'larını tek
/// yerden sunan read-only registry (ADR-0024 §24.5). Singleton lifetime.
///
/// Composer ve evaluator bu registry üzerinden detector listesine erişir;
/// <c>IEnumerable&lt;IPatternDetector&gt;</c> doğrudan inject etmek yerine
/// registry sıra garanti eder (DI container'ın enumeration order'ı stable
/// değil — testlerde ve audit'te belirlenebilirlik için soyutlama gerekli).
/// </summary>
public interface IPatternRegistry
{
    /// <summary>
    /// Detector kayıt sırasına göre stable read-only liste. Composer her bar
    /// bu sırayla iterasyon yapar; ContextJson'da pattern dizilimi de bu
    /// sırayı izler.
    /// </summary>
    IReadOnlyList<IPatternDetector> Detectors { get; }
}
