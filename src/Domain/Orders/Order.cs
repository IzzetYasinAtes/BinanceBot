using BinanceBot.Domain.Common;
using BinanceBot.Domain.Orders.Events;
using BinanceBot.Domain.ValueObjects;

namespace BinanceBot.Domain.Orders;

public sealed class Order : AggregateRoot<long>
{
    private readonly List<OrderFill> _fills = [];

    public string ClientOrderId { get; private set; } = default!;
    public long? ExchangeOrderId { get; private set; }
    public Symbol Symbol { get; private set; } = default!;
    public OrderSide Side { get; private set; }
    public OrderType Type { get; private set; }
    public TimeInForce TimeInForce { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal? Price { get; private set; }
    public decimal? StopPrice { get; private set; }
    /// <summary>
    /// Loop 107 / ADR-0026 §A — Pullback Limit Order. Limit fiyatı (LIMIT type için).
    /// Market emir için <c>null</c>. Limit type'da <see cref="Price"/> ile aynı değeri
    /// taşır; ayrı tutulması "limit niyeti" semantiğini DB query (Status=New AND
    /// LimitPrice IS NOT NULL) ile mümkün kılar — Loop 92 öncesi LIMIT testleri ile
    /// karışmaz.
    /// </summary>
    public decimal? LimitPrice { get; private set; }
    /// <summary>
    /// Loop 107 / ADR-0026 §A — pending limit timeout. Bu zamana kadar fill olmayan
    /// LIMIT emir <see cref="Expire"/> ile <see cref="OrderStatus.Expired"/>
    /// statüsüne geçirilir. Market emir için <c>null</c>.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; private set; }
    /// <summary>
    /// Optional profit-taking hint forwarded to the resulting <see cref="Domain.Positions.Position"/>
    /// (Loop 10 fix). Not sent to Binance — MARKET execution ignores it. Pure metadata for the
    /// downstream <c>TakeProfitMonitorService</c>.
    /// </summary>
    public decimal? TakeProfit { get; private set; }
    public decimal ExecutedQuantity { get; private set; }
    public decimal CumulativeQuoteQty { get; private set; }
    public OrderStatus Status { get; private set; }
    public long? StrategyId { get; private set; }
    public TradingMode Mode { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyCollection<OrderFill> Fills => _fills.AsReadOnly();

    private Order() { }

    public static Order Place(
        string clientOrderId,
        Symbol symbol,
        OrderSide side,
        OrderType type,
        TimeInForce timeInForce,
        decimal quantity,
        decimal? price,
        decimal? stopPrice,
        long? strategyId,
        TradingMode mode,
        DateTimeOffset now,
        decimal? takeProfit = null)
    {
        if (string.IsNullOrWhiteSpace(clientOrderId) || clientOrderId.Length > 36)
        {
            throw new DomainException("ClientOrderId is required (max 36).");
        }
        if (quantity <= 0m)
        {
            throw new DomainException("Quantity must be positive.");
        }
        if (type is OrderType.Limit or OrderType.LimitMaker
            && (price is null || price <= 0m))
        {
            throw new DomainException("Limit orders require positive Price.");
        }
        if (type is OrderType.StopLoss or OrderType.StopLossLimit
                 or OrderType.TakeProfit or OrderType.TakeProfitLimit
            && (stopPrice is null || stopPrice <= 0m))
        {
            throw new DomainException("Stop/TakeProfit orders require positive StopPrice.");
        }
        if (takeProfit is decimal tp && tp <= 0m)
        {
            throw new DomainException("TakeProfit hint must be positive when set.");
        }

        var order = new Order
        {
            ClientOrderId = clientOrderId,
            Symbol = symbol,
            Side = side,
            Type = type,
            TimeInForce = timeInForce,
            Quantity = quantity,
            Price = price,
            StopPrice = stopPrice,
            TakeProfit = takeProfit,
            Status = OrderStatus.New,
            StrategyId = strategyId,
            Mode = mode,
            CreatedAt = now,
            UpdatedAt = now,
        };

        order.RaiseDomainEvent(new OrderPlacedEvent(
            clientOrderId, symbol.Value, side, type, quantity, price, mode));
        return order;
    }

    /// <summary>
    /// Loop 107 / ADR-0026 §A — Pullback Limit Order factory. Bar close anında market
    /// yerine bar_close × (1 - offsetPct) (Long) / × (1 + offsetPct) (Short) fiyatında
    /// GTC LIMIT emir oluşturur; <paramref name="expiresAt"/> dolduğunda
    /// <see cref="PendingLimitTimeoutWorker"/> tarafından <see cref="Expire"/> ile
    /// iptal edilir. <see cref="LimitPrice"/> ve <see cref="ExpiresAt"/> ek alanlarla
    /// pending limit'i market emirden ayırır.
    /// </summary>
    public static Order PlaceLimit(
        string clientOrderId,
        Symbol symbol,
        OrderSide side,
        decimal quantity,
        decimal limitPrice,
        decimal? stopPrice,
        long? strategyId,
        TradingMode mode,
        DateTimeOffset now,
        DateTimeOffset expiresAt,
        decimal? takeProfit = null)
    {
        if (limitPrice <= 0m)
        {
            throw new DomainException("LimitPrice must be positive.");
        }
        if (expiresAt <= now)
        {
            throw new DomainException("ExpiresAt must be in the future.");
        }

        var order = Place(
            clientOrderId, symbol, side, OrderType.Limit, TimeInForce.Gtc,
            quantity, limitPrice, stopPrice, strategyId, mode, now, takeProfit);
        order.LimitPrice = limitPrice;
        order.ExpiresAt = expiresAt;
        return order;
    }

    public void AttachExchangeId(long exchangeOrderId, DateTimeOffset now)
    {
        if (exchangeOrderId <= 0)
        {
            throw new DomainException("ExchangeOrderId must be positive.");
        }
        ExchangeOrderId = exchangeOrderId;
        UpdatedAt = now;
    }

    public void RegisterFill(
        long exchangeTradeId,
        decimal price,
        decimal quantity,
        decimal commission,
        string commissionAsset,
        DateTimeOffset filledAt)
    {
        if (Status is OrderStatus.Cancelled or OrderStatus.Rejected or OrderStatus.Expired)
        {
            throw new DomainException($"Cannot register fill on {Status} order.");
        }

        var remaining = Quantity - ExecutedQuantity;
        if (quantity > remaining)
        {
            throw new DomainException(
                $"Fill quantity {quantity} exceeds remaining {remaining}.");
        }

        if (_fills.Any(f => f.ExchangeTradeId == exchangeTradeId))
        {
            return;
        }

        var fill = OrderFill.Create(exchangeTradeId, price, quantity,
            commission, commissionAsset, filledAt);
        _fills.Add(fill);

        ExecutedQuantity += quantity;
        CumulativeQuoteQty += price * quantity;
        UpdatedAt = filledAt;

        if (ExecutedQuantity >= Quantity)
        {
            Status = OrderStatus.Filled;
            RaiseDomainEvent(new OrderFilledEvent(
                ClientOrderId, Symbol.Value, ExecutedQuantity, CumulativeQuoteQty, Mode));
        }
        else
        {
            Status = OrderStatus.PartiallyFilled;
        }
    }

    public void Cancel(DateTimeOffset now, string reason)
    {
        if (Status is not OrderStatus.New and not OrderStatus.PartiallyFilled)
        {
            throw new DomainException($"Cannot cancel order in {Status} state.");
        }

        Status = OrderStatus.Cancelled;
        UpdatedAt = now;
        RaiseDomainEvent(new OrderCancelledEvent(ClientOrderId, Symbol.Value, reason));
    }

    public void Reject(string reason, DateTimeOffset now)
    {
        Status = OrderStatus.Rejected;
        UpdatedAt = now;
        RaiseDomainEvent(new OrderRejectedEvent(ClientOrderId, Symbol.Value, reason));
    }

    public void Expire(DateTimeOffset now)
    {
        if (Status is OrderStatus.Filled or OrderStatus.Cancelled)
        {
            return;
        }
        Status = OrderStatus.Expired;
        UpdatedAt = now;
        RaiseDomainEvent(new OrderExpiredEvent(ClientOrderId, Symbol.Value));
    }
}
