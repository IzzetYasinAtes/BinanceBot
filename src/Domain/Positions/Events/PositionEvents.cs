using BinanceBot.Domain.Common;

namespace BinanceBot.Domain.Positions.Events;

public sealed record PositionOpenedEvent(
    long PositionId,
    string Symbol,
    PositionSide Side,
    decimal EntryPrice,
    decimal Quantity) : DomainEventBase;

public sealed record PositionUpdatedEvent(
    long PositionId,
    string Symbol,
    decimal Quantity,
    decimal AverageEntryPrice) : DomainEventBase;

public sealed record PositionMarkedToMarketEvent(
    long PositionId,
    string Symbol,
    decimal MarkPrice,
    decimal UnrealizedPnl) : DomainEventBase;

public sealed record PositionClosedEvent(
    long PositionId,
    string Symbol,
    decimal RealizedPnl,
    string Reason) : DomainEventBase;
