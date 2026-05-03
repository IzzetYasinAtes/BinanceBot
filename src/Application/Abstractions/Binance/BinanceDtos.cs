using BinanceBot.Domain.Common;
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

/// <summary>
/// Loop 92 — Futures pivot. <c>Direction</c> alanı eklendi (Long/Short hint).
/// Spot'ta Side BUY=Long open, SELL=Long close idi; Futures'ta Side BUY/SELL
/// exchange protokol değeri, Direction botun pozisyon yönü hint'i (audit + logging).
/// </summary>
public sealed record PlaceOrderRequest(
    string Symbol,
    string Side,
    TradeDirection Direction,
    string Type,
    string TimeInForce,
    decimal Quantity,
    decimal? Price,
    decimal? StopPrice,
    string ClientOrderId);

public sealed record TestOrderResponse(bool Accepted, string? ErrorCode, string? ErrorMessage);
public sealed record CancelOrderResponse(bool Accepted, string? ErrorCode, string? ErrorMessage);

public sealed record LiveOrderResponse(
    bool Accepted,
    long? ExchangeOrderId,
    string Status,
    decimal ExecutedQuantity,
    decimal CummulativeQuoteQty,
    IReadOnlyList<LiveFillDto> Fills,
    string? ErrorCode,
    string? ErrorMessage);

public sealed record LiveFillDto(
    long TradeId,
    decimal Price,
    decimal Quantity,
    decimal Commission,
    string CommissionAsset);
