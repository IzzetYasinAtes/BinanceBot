using BinanceBot.Application.Strategies.Patterns;

namespace BinanceBot.Application.Strategies.Indicators;

/// <summary>
/// Loop 81 — Pattern-composite scalping pivot. Tek surface method:
/// <see cref="TryGetBarSnapshot"/>. KMS+BBR (Loop 67-80) dönemine ait
/// <c>TryGetKmsMomentumSnapshot</c> + <c>TryGetBbReversalSnapshot</c>
/// methodları kaldırıldı; pattern detector'lar paylaşılan
/// <see cref="BarSnapshot"/> üzerinden çalışır.
/// </summary>
public interface IMarketIndicatorService
{
    /// <summary>
    /// Loop 81 — Tek paylaşılan bar snapshot'ı (ADR-0024 §24.7). 5m rolling
    /// buffer'dan son kapalı barı + indikator setini hesaplar; tüm
    /// <c>IPatternDetector</c> implementation'ları bu snapshot'ı tüketir.
    ///
    /// Hesaplananlar: OHLCV (curr + prev), RSI14 (curr + prev), EMA9/21/50/200,
    /// ATR14, Bollinger(20,2) bands + BBW + BBW6-min, ADX14, AvgVolume20,
    /// AvgTradeCount20, Donchian20 high/low, VWAP, MACD line (curr + prev),
    /// SpreadPct (canlı BookTicker üzerinden).
    ///
    /// <para>
    /// Warmup eşiği: 200 bar (EMA200 + Bollinger sliding history). Yetersizse
    /// <c>null</c> döner — pattern composer "warmup not done — skip" olarak
    /// yorumlar.
    /// </para>
    ///
    /// <para>
    /// BookTicker verisi yoksa <see cref="BarSnapshot.SpreadPct"/> 1m gibi
    /// anormal değer döner ⇒ <c>SpreadGuardGate</c> hard-skip eder; snapshot
    /// build'i yine de gerçekleşir (defansif — diğer detector'lar pattern
    /// görmüş olabilir, sadece liquidity guard çalışır).
    /// </para>
    /// </summary>
    BarSnapshot? TryGetBarSnapshot(string symbol);
}
