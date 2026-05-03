using BinanceBot.Domain.Common;

namespace BinanceBot.Domain.Positions.Events;

public sealed record PositionOpenedEvent(
    long PositionId,
    string Symbol,
    TradeDirection Direction,
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

/// <summary>
/// Loop 76 — trailing-stop exit trigger audit. Domain method
/// <c>Position.UpdatePeakAndCheckTrailing</c>, BE applied + trail crossed
/// koşulunu gördüğünde raise eder. Aggregate aslında close edilmez; bu sadece
/// "closer'a haber" event'idir — gerçek close, MarkToMarketWorker'ın
/// <c>CloseSignalPositionCommand</c> dispatch'iyle StopLoss/TakeProfit pattern'i
/// üzerinden gerçekleşir. Long: peak high üzerinden, Short: trough low
/// üzerinden hesaplanır (Loop 92 Futures pivot, ADR-0025).
/// </summary>
public sealed record PositionTrailingExitTriggeredEvent(
    long PositionId,
    string Symbol,
    decimal PeakPrice,
    decimal MarkPrice,
    decimal TrailPct) : DomainEventBase;
