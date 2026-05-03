using BinanceBot.Domain.Common;

namespace BinanceBot.Application.Strategies.Patterns;

/// <summary>
/// Loop 81 — Tek bir chart pattern algılayıcısı (ADR-0024 §24.8). Pure function:
/// <see cref="BarSnapshot"/> in → <see cref="PatternEvaluation"/> out. Stateless,
/// thread-safe, singleton lifetime. ≤1ms per call hedef.
///
/// SOLID/SRP: bir dosya = bir pattern. Yeni pattern eklemek = bu interface'i
/// implemente eden yeni sınıf + DI satırı; mevcut detector/composer dokunulmaz.
///
/// Composer ağırlıklı skor toplar; <see cref="IsHardGate"/> true olan detector
/// 0 puan döndürdüğünde composer emit'i tamamen iptal eder (hard-skip).
///
/// Loop 92 — Direction property eklendi (ADR-0025). Long-bias detector'lar (engulfing,
/// hammer, RSI oversold) yalnızca Long bucket'a, Short-bias detector'lar yalnızca Short
/// bucket'a katkı verir. Neutral detector'lar (volume, spread, ADX) iki bucket'a da katkı.
/// </summary>
public interface IPatternDetector
{
    /// <summary>Detector'ın stable identifier'ı — log + ContextJson key. snake_case.</summary>
    string Name { get; }

    /// <summary>Composer için default ağırlık; per-coin override mümkün.</summary>
    decimal DefaultWeight { get; }

    /// <summary>True ⇒ score 0 olduğunda composer emit'i hard-skip eder.</summary>
    bool IsHardGate { get; }

    /// <summary>
    /// Loop 92 — pattern yön bias'ı (ADR-0025). <c>Long</c> ⇒ sadece Long bucket'a
    /// katkı; <c>Short</c> ⇒ sadece Short bucket'a; <c>null</c> (Neutral) ⇒ iki
    /// bucket'a da katkı (volume/spread/ADX gibi yön-agnostik gate'ler).
    /// </summary>
    TradeDirection? Direction { get; }

    /// <summary>Pure pattern değerlendirme. Stateless; <c>snapshot</c> immutable.</summary>
    PatternEvaluation Evaluate(BarSnapshot snapshot);
}
