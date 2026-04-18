namespace BinanceBot.Application.Abstractions.Binance;

/// <summary>
/// 24h rolling-window ticker snapshot from Binance Spot REST <c>/api/v3/ticker/24hr</c>
/// (ADR-0012 §12.1). <see cref="PriceChangePct"/> is expressed as percent points (e.g.
/// <c>-2.34</c> for -2.34%), matching Binance's <c>priceChangePercent</c> field.
/// </summary>
public sealed record Ticker24hDto(
    string Symbol,
    decimal LastPrice,
    decimal PriceChangePct,
    decimal HighPrice,
    decimal LowPrice,
    decimal QuoteVolume,
    DateTimeOffset CloseTime);
