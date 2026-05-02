using BinanceBot.Application.Strategies.Patterns;

namespace BinanceBot.Infrastructure.Strategies.Patterns;

/// <summary>
/// Loop 81 — DI'da kayıtlı <see cref="IPatternDetector"/> instance'larını
/// kayıt sırasına göre stable liste olarak sunar (ADR-0024 §24.5).
/// Singleton lifetime; constructor enumeration sonra immutable kalır.
/// </summary>
public sealed class PatternRegistry : IPatternRegistry
{
    public IReadOnlyList<IPatternDetector> Detectors { get; }

    public PatternRegistry(IEnumerable<IPatternDetector> detectors)
    {
        Detectors = detectors.ToArray();
    }
}
