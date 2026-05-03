using BinanceBot.Domain.Common;

namespace BinanceBot.Application.Strategies.Patterns;

/// <summary>
/// Loop 81 — <see cref="IPatternSignalComposer"/> çıktısı (ADR-0024 §24.9 + ADR-0025 Loop 92).
/// Emit ⇒ entry/stop/tp + Direction + ContextJson dolu; Skip ⇒ <see cref="SkipReason"/> dolu.
///
/// Loop 92 — <c>Direction</c> alanı eklendi. Composer iki-kova logic ile Long ve Short
/// skorlarını ayrı toplar; baskın bucket emit eder. "Both qualified" durumunda
/// SkipReason = both_directions_qualified ile skip olur (yön belirsizliği).
/// </summary>
/// <param name="Emit">true ⇒ sinyal yayınlanmalı; false ⇒ skip.</param>
/// <param name="Direction">Loop 92 — emit yönü. Skip durumunda null.</param>
/// <param name="TotalScore">Emit edilen yönün ağırlıklı toplam skoru.</param>
/// <param name="RequiredScore">Composer eşiği — telemetry için.</param>
/// <param name="SkipReason">Skip sebebi (snake_case).</param>
/// <param name="EntryPrice">Emit ⇒ entry fiyatı (bar close).</param>
/// <param name="StopPrice">Emit ⇒ SL fiyatı (Long: entry altı; Short: entry üstü).</param>
/// <param name="TakeProfitPrice">Emit ⇒ TP fiyatı (R:R 1:2).</param>
/// <param name="MaxHoldMinutes">Emit ⇒ pozisyonun max hold süresi dakika cinsinden.</param>
/// <param name="ContextJson">Audit payload (her zaman dolu — emit/skip).</param>
public sealed record CompositeSignalDecision(
    bool Emit,
    TradeDirection? Direction,
    decimal TotalScore,
    decimal RequiredScore,
    string? SkipReason,
    decimal? EntryPrice,
    decimal? StopPrice,
    decimal? TakeProfitPrice,
    int? MaxHoldMinutes,
    string ContextJson);
