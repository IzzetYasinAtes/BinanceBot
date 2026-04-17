using BinanceBot.Domain.MarketData;

namespace BinanceBot.Application.Abstractions.Binance;

public sealed record ExchangeInfoSymbolDto(
    string Symbol,
    string BaseAsset,
    string QuoteAsset,
    string Status,
    decimal TickSize,
    decimal StepSize,
    decimal MinNotional,
    decimal MinQty,
    decimal MaxQty);

public sealed record RestKlineDto(
    DateTimeOffset OpenTime,
    DateTimeOffset CloseTime,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Volume,
    decimal QuoteVolume,
    int TradeCount,
    decimal TakerBuyBase,
    decimal TakerBuyQuote);

public sealed record OrderBookSnapshotDto(
    long LastUpdateId,
    IReadOnlyList<OrderBookLevelDto> Bids,
    IReadOnlyList<OrderBookLevelDto> Asks);

public sealed record OrderBookLevelDto(decimal Price, decimal Quantity);

public sealed record BinanceServerTimeDto(long ServerTimeMs);

public sealed record WsKlinePayload(
    string Symbol,
    KlineInterval Interval,
    DateTimeOffset OpenTime,
    DateTimeOffset CloseTime,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Volume,
    decimal QuoteVolume,
    int TradeCount,
    decimal TakerBuyBase,
    decimal TakerBuyQuote,
    bool IsClosed);

public sealed record WsBookTickerPayload(
    string Symbol,
    long UpdateId,
    decimal BidPrice,
    decimal BidQty,
    decimal AskPrice,
    decimal AskQty,
    DateTimeOffset ReceivedAt);

public sealed record WsDepthDiffPayload(
    string Symbol,
    long FirstUpdateId,
    long FinalUpdateId,
    long? PreviousFinalUpdateId,
    IReadOnlyList<OrderBookLevelDto> BidUpdates,
    IReadOnlyList<OrderBookLevelDto> AskUpdates,
    DateTimeOffset ReceivedAt);
