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
///
/// Loop 112 — ADR-0027 strateji ailesi pivot. <see cref="PatternComposite"/>
/// askıya alındı (Status=Paused, kod silinmedi); yeni aile
/// <see cref="SwingTrade"/> = 4 plug-in <c>IStrategyEvaluator</c> olarak
/// eklendi. Çoğul evaluator registry desteği zaten vardı (constructor
/// IEnumerable injection); yeni Type için DI satırı + Strategy seed yeterli.
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
    ///
    /// Loop 112 — ADR-0027 ile <c>Strategy.Status=Paused</c>'a alındı;
    /// kod ve test altyapısı korunur (re-aktivasyon: Status flip yeterli).
    /// </summary>
    PatternComposite = 3,

    /// <summary>
    /// Loop 112 — SwingTrade (ADR-0027 Aile A). 4h bar kapanışında EMA20/EMA50
    /// trend + Volume(bar) > VolumeSma(20) × 1.5 + RSI(14) ∈ [40, 65] (Long) /
    /// [35, 60] (Short) emit kuralları. ATR(14) × 1.5 SL, ATR × 3 TP (R:R 1:2),
    /// %1+ kar → BE stop, 8h hold + %0.5 kar → time-exit.
    /// PatternComposite Paused olduğunda DB seed'de bu Type Active.
    /// </summary>
    SwingTrade = 4,
}

public enum StrategySignalDirection
{
    Long = 1,
    Short = 2,
    Exit = 3,
}
