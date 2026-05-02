using BinanceBot.Domain.MarketData;

namespace BinanceBot.Application.Strategies.Patterns;

/// <summary>
/// Loop 81 — Pattern-composite scalping pivot için tek paylaşılan OHLCV +
/// indikator DTO (ADR-0024 §24.7). Tüm <see cref="IPatternDetector"/>
/// implementation'ları bu snapshot'tan beslenir; <c>IMarketIndicatorService.TryGetBarSnapshot</c>
/// bar başına bir kez build eder, detector'lar pure function olarak okurlar.
///
/// Bu DTO'nun varlık nedeni DRY: KMS+BBR mimarisi RSI/EMA/ATR/BBW/ADX gibi
/// indikator hesaplarını snapshot başına tekrarlıyordu; tek snapshot ile 10
/// detector ortak warmup penceresi, ortak hesap, ortak bar kayma tutarlılığı
/// kullanır.
///
/// <para>
/// Yetersiz warmup durumunda <c>TryGetBarSnapshot</c> <c>null</c> döner;
/// bireysel alan değerleri snapshot oluşturulduktan sonra her zaman tutarlıdır
/// (warmup sonrası degenerate 0 değer dönmez, EMA200 hariç — 200 bar dolmadan
/// snapshot null döner).
/// </para>
/// </summary>
/// <param name="Symbol">Trade sembolü (örn. "BTCUSDT") — log + cooldown anchor.</param>
/// <param name="BarOpenTime">Son kapanmış 5m bar açılış zamanı.</param>
/// <param name="BarCloseTime">Son kapanmış 5m bar kapanış zamanı — snapshot freshness.</param>
/// <param name="Open">Son kapalı bar açılış fiyatı.</param>
/// <param name="High">Son kapalı bar yüksek fiyatı.</param>
/// <param name="Low">Son kapalı bar düşük fiyatı.</param>
/// <param name="Close">Son kapalı bar kapanış fiyatı — entry referansı.</param>
/// <param name="Volume">Son kapalı bar volume.</param>
/// <param name="TradeCount">Son kapalı bar trade count (volume surge gate).</param>
/// <param name="PrevOpen">Bir önceki bar açılış (2-bar engulfing için).</param>
/// <param name="PrevHigh">Bir önceki bar yüksek (inside-bar / engulfing için).</param>
/// <param name="PrevLow">Bir önceki bar düşük (inside-bar için).</param>
/// <param name="PrevClose">Bir önceki bar kapanış.</param>
/// <param name="PrevTradeCount">Bir önceki bar trade count.</param>
/// <param name="Rsi14">Wilder RSI(14) son kapalı bar dahil.</param>
/// <param name="Rsi14Prev">Wilder RSI(14) bir bar önce — recovery cross için.</param>
/// <param name="Ema9">EMA(9) son bar dahil.</param>
/// <param name="Ema9Prev">EMA(9) bir önceki bar dahil — slope için.</param>
/// <param name="Ema21">EMA(21) son bar (squeeze break + higher-low touch için).</param>
/// <param name="Ema21Prev5">EMA(21) 5 bar önce — slope eğim referansı (VWAP bounce için).</param>
/// <param name="Ema50">EMA(50) son bar (squeeze break trend onayı için).</param>
/// <param name="Ema200">EMA(200) son bar (RSI oversold recovery için, opsiyonel trend gate).
/// Buffer 200 bar dolmadıysa snapshot zaten null döneceği için her zaman geçerli.</param>
/// <param name="Atr14">ATR(14) — TP/SL geometri kaynağı.</param>
/// <param name="BollingerLower">BB(20, 2) alt band.</param>
/// <param name="BollingerMiddle">BB(20, 2) orta band (20-bar SMA).</param>
/// <param name="BollingerUpper">BB(20, 2) üst band.</param>
/// <param name="BollingerBandWidth">(Upper - Lower) / Middle — sıkışma metrik.</param>
/// <param name="BollingerBandWidthMin6">Son 6 bar BBW minimumu — squeeze release için.</param>
/// <param name="Adx14">Wilder ADX(14) — soft-filter regime referansı.</param>
/// <param name="AvgVolume20">Son 20 bar volume aritmetik ortalaması (volume surge gate).</param>
/// <param name="AvgTradeCount20">Son 20 bar TradeCount ortalaması (alternatif gate).</param>
/// <param name="DonchianHigh20">Son 20 bar high'ın maksimumu (range break için).</param>
/// <param name="DonchianLow20">Son 20 bar low'un minimumu.</param>
/// <param name="Vwap">Bar buffer üzerinden hesaplanmış cumulative VWAP.</param>
/// <param name="MacdLine">MACD line = EMA(12) - EMA(26) son bar (zero-cross için).</param>
/// <param name="MacdLinePrev">MACD line bir önceki bar — zero-cross sıçrama tespit.</param>
/// <param name="SpreadPct">(Ask-Bid)/Ask — canlı BookTicker'dan snapshot build sırasında okunur.
/// BookTicker yoksa 1m gibi anormal yüksek değer döner ⇒ <c>SpreadGuardGate</c> hard-skip eder.</param>
/// <param name="RecentBars">Son 30 bar (oldest-first) — Donchian/Higher-low/RSI sequence için.
/// Memory hafif (30 × ~80 byte = 2.4KB).</param>
public sealed record BarSnapshot(
    string Symbol,
    DateTimeOffset BarOpenTime,
    DateTimeOffset BarCloseTime,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Volume,
    int TradeCount,
    decimal PrevOpen,
    decimal PrevHigh,
    decimal PrevLow,
    decimal PrevClose,
    int PrevTradeCount,
    decimal Rsi14,
    decimal Rsi14Prev,
    decimal Ema9,
    decimal Ema9Prev,
    decimal Ema21,
    decimal Ema21Prev5,
    decimal Ema50,
    decimal Ema200,
    decimal Atr14,
    decimal BollingerLower,
    decimal BollingerMiddle,
    decimal BollingerUpper,
    decimal BollingerBandWidth,
    decimal BollingerBandWidthMin6,
    decimal Adx14,
    decimal AvgVolume20,
    decimal AvgTradeCount20,
    decimal DonchianHigh20,
    decimal DonchianLow20,
    decimal Vwap,
    decimal MacdLine,
    decimal MacdLinePrev,
    decimal SpreadPct,
    IReadOnlyList<Kline> RecentBars);
