namespace BinanceBot.Application.Strategies.Indicators;

/// <summary>
/// Loop 79 — Bollinger Band Reversal evaluator snapshot. Range regime (BBW
/// ∈ [BbwRangeMin, BbwRangeMax]) içinde alt band'a değen + RSI dip-recovery
/// koşulunu sağlayan sinyalleri yakalar.
///
/// Bu snapshot, mevcut <see cref="KmsMomentumSnapshot"/> ile aynı 5m rolling
/// buffer üzerinden hesaplanır (200 bar) — additive surface, KMS yolunu
/// etkilemez. Hesaplama yolu:
/// <list type="bullet">
///   <item><see cref="Rsi14"/> + <see cref="Rsi14Prev"/> — Wilder RSI cross
///         (curr ve prev bar üzerinde aynı pencere bir bar geriye kaydırılarak).</item>
///   <item><see cref="BollingerLower"/> + <see cref="BollingerMean"/> +
///         <see cref="BollingerBandWidth"/> — BB(<c>bbPeriod</c>, <c>bbStdDev</c>)
///         son kapalı bar dahil; BBW = (Upper - Lower) / Mean.</item>
///   <item><see cref="Atr14"/> — bilgi amaçlı (audit + future ATR-based SL
///         varyantı için); evaluator default'ta sabit pct SL kullanır.</item>
/// </list>
///
/// Warmup yetersizse <c>null</c> döner; <c>BollingerBandWidth = 0</c> durumu
/// (flat bant) range regime check'inde otomatik filtrelenir (BbwRangeMin altı).
/// </summary>
/// <param name="CurrentClose">Son kapalı 5m bar close — entry referansı.</param>
/// <param name="Rsi14">Wilder RSI(14) son kapalı bar dahil (recovery side).</param>
/// <param name="Rsi14Prev">Wilder RSI(14) bir önceki bar dahil (dip side —
/// RSI bu bar'dan curr bar'a yükseliyorsa "dip yapıp dönüyor" semantiği).</param>
/// <param name="BollingerLower">BB lower band (Mean - stdDev × multiplier).
/// Entry: <c>close &lt; BollingerLower × (1 + BufferPctEntry)</c>.</param>
/// <param name="BollingerMean">BB orta band (20-bar SMA) — TP hedefi (mean
/// reversion). Evaluator <c>SuggestedTakeProfit</c> alanına direkt aktarır.</param>
/// <param name="BollingerBandWidth">BBW = (Upper - Lower) / Mean. Range regime
/// kontrolü: <c>BbwRangeMin &lt;= BBW &lt;= BbwRangeMax</c> olmalı; aksi halde
/// dead (sıkışma) veya trending (geniş bant — KMS bölgesi) olarak skip.</param>
/// <param name="Atr14">14-bar Average True Range — bilgi/audit amaçlı; default
/// SL hesabında kullanılmaz (sabit BufferPctExit).</param>
/// <param name="LastBarOpenTime">Son kapalı 5m bar açılış zamanı — log + cooldown anchor.</param>
/// <param name="AsOf">Son kapalı 5m bar kapanış zamanı — snapshot freshness.</param>
public sealed record BbReversalSnapshot(
    decimal CurrentClose,
    decimal Rsi14,
    decimal Rsi14Prev,
    decimal BollingerLower,
    decimal BollingerMean,
    decimal BollingerBandWidth,
    decimal Atr14,
    DateTimeOffset LastBarOpenTime,
    DateTimeOffset AsOf);
