namespace BinanceBot.Application.Strategies.Patterns;

/// <summary>
/// Loop 81 — <see cref="IPatternSignalComposer"/> çıktısı (ADR-0024 §24.9). Emit
/// ⇒ entry/stop/tp + ContextJson dolu; Skip ⇒ <see cref="SkipReason"/> dolu.
///
/// <c>ContextJson</c> her zaman dolu (audit + downstream telemetry).
/// </summary>
/// <param name="Emit">true ⇒ sinyal yayınlanmalı; false ⇒ skip.</param>
/// <param name="TotalScore">Ağırlıklı toplam skor (Σ Score × Weight × softMultiplier).</param>
/// <param name="RequiredScore">Composer eşiği — telemetry için.</param>
/// <param name="SkipReason">Skip sebebi (snake_case): <c>hard_gate:&lt;name&gt;</c> /
/// <c>score_below_threshold</c> / <c>geometry_invalid</c>.</param>
/// <param name="EntryPrice">Emit ⇒ entry fiyatı (bar close).</param>
/// <param name="StopPrice">Emit ⇒ SL fiyatı.</param>
/// <param name="TakeProfitPrice">Emit ⇒ TP fiyatı (R:R 1:2).</param>
/// <param name="MaxHoldMinutes">Emit ⇒ pozisyonun max hold süresi dakika cinsinden.</param>
/// <param name="ContextJson">Audit payload (her zaman dolu — emit/skip).</param>
public sealed record CompositeSignalDecision(
    bool Emit,
    decimal TotalScore,
    decimal RequiredScore,
    string? SkipReason,
    decimal? EntryPrice,
    decimal? StopPrice,
    decimal? TakeProfitPrice,
    int? MaxHoldMinutes,
    string ContextJson);
