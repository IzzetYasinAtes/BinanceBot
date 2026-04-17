using BinanceBot.Domain.Common;

namespace BinanceBot.Domain.Orders.Events;

public sealed record OrderPlacedEvent(
    string ClientOrderId,
    string Symbol,
    OrderSide Side,
    OrderType Type,
    decimal Quantity,
    decimal? Price) : DomainEventBase;

public sealed record OrderFilledEvent(
    string ClientOrderId,
    string Symbol,
    decimal ExecutedQuantity,
    decimal CumulativeQuoteQty) : DomainEventBase;

public sealed record OrderCancelledEvent(
    string ClientOrderId,
    string Symbol,
    string Reason) : DomainEventBase;

public sealed record OrderRejectedEvent(
    string ClientOrderId,
    string Symbol,
    string Reason) : DomainEventBase;

public sealed record OrderExpiredEvent(
    string ClientOrderId,
    string Symbol) : DomainEventBase;
