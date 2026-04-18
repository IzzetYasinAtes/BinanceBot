using BinanceBot.Domain.Common;
using BinanceBot.Domain.Positions.Events;
using BinanceBot.Domain.ValueObjects;

namespace BinanceBot.Domain.Positions;

public sealed class Position : AggregateRoot<long>
{
    public Symbol Symbol { get; private set; } = default!;
    public PositionSide Side { get; private set; }
    public PositionStatus Status { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal AverageEntryPrice { get; private set; }
    public decimal? ExitPrice { get; private set; }
    public decimal MarkPrice { get; private set; }

    /// <summary>
    /// Optional risk-management stop level captured at open time (ADR-0012 §12.4).
    /// Persisted alongside the aggregate so <see cref="Infrastructure.Trading.StopLossMonitorService"/>
    /// can soft-trigger an exit when mark price crosses this threshold without depending on
    /// any in-process cache.
    /// </summary>
    public decimal? StopPrice { get; private set; }

    public decimal UnrealizedPnl { get; private set; }
    public decimal RealizedPnl { get; private set; }
    public long? StrategyId { get; private set; }
    public TradingMode Mode { get; private set; }
    public DateTimeOffset OpenedAt { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private Position() { }

    public static Position Open(
        Symbol symbol,
        PositionSide side,
        decimal quantity,
        decimal entryPrice,
        decimal? stopPrice,
        long? strategyId,
        TradingMode mode,
        DateTimeOffset now)
    {
        if (quantity <= 0m)
        {
            throw new DomainException("Position quantity must be positive.");
        }
        if (entryPrice <= 0m)
        {
            throw new DomainException("Entry price must be positive.");
        }
        if (stopPrice is decimal s && s <= 0m)
        {
            throw new DomainException("Stop price must be positive when set.");
        }

        var position = new Position
        {
            Symbol = symbol,
            Side = side,
            Status = PositionStatus.Open,
            Quantity = quantity,
            AverageEntryPrice = entryPrice,
            MarkPrice = entryPrice,
            StopPrice = stopPrice,
            StrategyId = strategyId,
            Mode = mode,
            OpenedAt = now,
            UpdatedAt = now,
        };

        position.RaiseDomainEvent(new PositionOpenedEvent(
            position.Id, symbol.Value, side, entryPrice, quantity, mode));
        return position;
    }

    public void AddFill(decimal addQuantity, decimal addPrice, DateTimeOffset now)
    {
        EnsureOpen();
        if (addQuantity <= 0m || addPrice <= 0m)
        {
            throw new DomainException("Quantity and price must be positive.");
        }

        var totalCost = (AverageEntryPrice * Quantity) + (addPrice * addQuantity);
        Quantity += addQuantity;
        AverageEntryPrice = totalCost / Quantity;
        UpdatedAt = now;

        RaiseDomainEvent(new PositionUpdatedEvent(Id, Symbol.Value, Quantity, AverageEntryPrice));
    }

    public void MarkToMarket(decimal markPrice, DateTimeOffset now)
    {
        EnsureOpen();
        if (markPrice <= 0m)
        {
            throw new DomainException("Mark price must be positive.");
        }

        MarkPrice = markPrice;
        UnrealizedPnl = Side == PositionSide.Long
            ? (markPrice - AverageEntryPrice) * Quantity
            : (AverageEntryPrice - markPrice) * Quantity;
        UpdatedAt = now;

        RaiseDomainEvent(new PositionMarkedToMarketEvent(Id, Symbol.Value, markPrice, UnrealizedPnl));
    }

    public void Close(decimal exitPrice, string reason, DateTimeOffset now)
    {
        EnsureOpen();
        if (exitPrice <= 0m)
        {
            throw new DomainException("Exit price must be positive.");
        }

        ExitPrice = exitPrice;
        RealizedPnl = Side == PositionSide.Long
            ? (exitPrice - AverageEntryPrice) * Quantity
            : (AverageEntryPrice - exitPrice) * Quantity;
        UnrealizedPnl = 0m;
        Status = PositionStatus.Closed;
        ClosedAt = now;
        UpdatedAt = now;

        RaiseDomainEvent(new PositionClosedEvent(Id, Symbol.Value, RealizedPnl, reason, Mode));
    }

    private void EnsureOpen()
    {
        if (Status != PositionStatus.Open)
        {
            throw new DomainException($"Position {Id} is not open (Status={Status}).");
        }
    }
}
