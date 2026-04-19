namespace BinanceBot.Application.Strategies.Indicators;

/// <summary>
/// ADR-0015 §15.6. Value object carried from <c>IMarketIndicatorService</c> into
/// <c>VwapEmaStrategyEvaluator</c>. Contains every primitive required by the
/// 4-condition entry check (direction gate, VWAP context, VWAP reclaim, volume
/// confirm) plus the <see cref="SwingHigh20"/> used for the safe TP target.
/// </summary>
public sealed record MarketIndicatorSnapshot(
    decimal Vwap,
    decimal PrevBarClose,
    decimal LastBarClose,
    decimal LastBarVolume,
    decimal VolumeSma20,
    decimal Ema1h21Now,
    decimal Ema1h21Prev,
    decimal SwingHigh20,
    DateTimeOffset AsOf);
