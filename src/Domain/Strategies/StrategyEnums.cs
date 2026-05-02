namespace BinanceBot.Domain.Strategies;

public enum StrategyStatus
{
    Draft = 1,
    Paused = 2,
    Active = 3,
}

/// <summary>
/// Loop 81 — Pattern-composite scalping pivot (ADR-0024 supersedes ADR-0014).
/// Önceki KMS (=1) + BBR (=2) değerleri silindi; tek strateji ailesi
/// <see cref="PatternComposite"/> = 3. <c>Loop81PatternPivot</c> migration'ı
/// veritabanı state'ini sıfırlar (Strategies + StrategySignals + Positions +
/// Orders + OrderFills full delete) — ordinal reuse riski yok.
///
/// Yeni pattern eklemek StrategyType eklemez; <c>IPatternDetector</c>
/// implementasyonu + DI satırı yeterli (OCP).
/// </summary>
public enum StrategyType
{
    /// <summary>
    /// Loop 81 — PatternComposite. Tek evaluator, çoklu detector
    /// (<see cref="BinanceBot.Application.Strategies.Patterns.IPatternDetector"/>).
    /// 5m bar kapanışında: paylaşılan <c>BarSnapshot</c> üzerinden 10 skor
    /// detector + 2 hard-gate + 1 soft-filter çalışır;
    /// <c>WeightedScorePatternComposer</c> ağırlıklı toplam skoru hesaplar,
    /// <c>RequiredScore</c> üstü ⇒ emit. Geometri ATR-bazlı (R:R 1:2,
    /// SL clip [%0.6, %1.2], MaxHold 60dk).
    /// </summary>
    PatternComposite = 3,
}

public enum StrategySignalDirection
{
    Long = 1,
    Short = 2,
    Exit = 3,
}
