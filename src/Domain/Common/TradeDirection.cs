namespace BinanceBot.Domain.Common;

/// <summary>
/// Loop 92 — Pivot Spot long-only → USDT-M Futures Long+Short. Tek concept yön
/// kavramı (ADR-0025). Önceki <c>PositionSide</c> enum'u (Loop 1-91) bu yapıya
/// rename edildi; <c>StrategySignalDirection</c> (Long/Short/Exit) ile semantik
/// olarak uyumlu, fakat Position aggregate'i sadece Long/Short tutar (Exit
/// kapanış komutudur, pozisyon yönü değil).
///
/// Integer backing aynı: 1=Long, 2=Short. Mevcut migration'larda Side kolonu
/// 1 değeriyle backfill edilir; Loop 92 reset migration'ı zaten boş tabloyla
/// başladığından risk taşımaz.
///
/// Kullanım yerleri:
/// <list type="bullet">
///   <item><see cref="BinanceBot.Domain.Positions.Position.Direction"/> — pozisyonun yönü.</item>
///   <item>StrategySignal.Direction — sinyalin yönü (Long/Short emit).</item>
///   <item>Detector hint'i (<c>IPatternDetector.Direction</c>) — Long-bias / Short-bias / Neutral.</item>
/// </list>
/// </summary>
public enum TradeDirection
{
    Long = 1,
    Short = 2,
}
