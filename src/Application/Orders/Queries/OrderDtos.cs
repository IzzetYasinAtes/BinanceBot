namespace BinanceBot.Application.Orders.Queries;

public sealed record OrderDto(
    string ClientOrderId,
    long? ExchangeOrderId,
    string Symbol,
    string Side,
    string Type,
    string TimeInForce,
    decimal Quantity,
    decimal? Price,
    decimal? StopPrice,
    decimal ExecutedQuantity,
    decimal CumulativeQuoteQty,
    string Status,
    long? StrategyId,
    string Mode,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record OrderFillDto(
    long ExchangeTradeId,
    decimal Price,
    decimal Quantity,
    decimal Commission,
    string CommissionAsset,
    DateTimeOffset FilledAt);
