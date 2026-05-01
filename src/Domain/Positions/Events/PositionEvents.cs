using BinanceBot.Domain.Common;

namespace BinanceBot.Domain.Positions.Events;

public sealed record PositionOpenedEvent(
    long PositionId,
    string Symbol,
    PositionSide Side,
    decimal EntryPrice,
    decimal Quantity,
    TradingMode Mode) : DomainEventBase;

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
    string Reason,
    TradingMode Mode) : DomainEventBase;

/// <summary>
/// Loop 75 — Break-even SL move audit event. Yayını <see cref="PositionsExtensions"/>
/// aracılığıyla domain method'undan tetiklenir. UI/audit consumer'lar SystemEvents
/// hattı üzerinden tüketir; iş kuralı (price-stop trigger) yine
/// <see cref="Infrastructure.Trading.StopLossMonitorService"/> aktif tutar.
/// </summary>
public sealed record PositionStopMovedEvent(
    long PositionId,
    string Symbol,
    decimal PreviousStopPrice,
    decimal NewStopPrice,
    string Reason) : DomainEventBase;
