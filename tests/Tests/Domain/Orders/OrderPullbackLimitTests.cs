using BinanceBot.Domain.Common;
using BinanceBot.Domain.Orders;
using BinanceBot.Domain.Orders.Events;
using BinanceBot.Domain.ValueObjects;
using FluentAssertions;

namespace BinanceBot.Tests.Domain.Orders;

/// <summary>
/// Loop 107 / ADR-0026 §A — Pullback Limit Order domain tests.
/// <see cref="Order.PlaceLimit"/> factory + LimitPrice/ExpiresAt fields +
/// <see cref="Order.Expire"/> idempotent behaviour.
/// </summary>
public class OrderPullbackLimitTests
{
    private static readonly Symbol Btc = Symbol.From("BTCUSDT");

    [Fact]
    public void PlaceLimit_PersistsLimitPriceExpiresAt_AndRaisesOrderPlacedEvent()
    {
        var now = DateTimeOffset.UtcNow;
        var expires = now.AddMinutes(5);
        var order = Order.PlaceLimit(
            clientOrderId: "cid-pl-1",
            symbol: Btc,
            side: OrderSide.Buy,
            quantity: 0.01m,
            limitPrice: 29970m,
            stopPrice: 29850m,
            strategyId: 1,
            mode: TradingMode.Paper,
            now: now,
            expiresAt: expires,
            takeProfit: 30100m);

        order.Type.Should().Be(OrderType.Limit);
        order.TimeInForce.Should().Be(TimeInForce.Gtc);
        order.LimitPrice.Should().Be(29970m);
        order.Price.Should().Be(29970m);
        order.ExpiresAt.Should().Be(expires);
        order.Status.Should().Be(OrderStatus.New);
        order.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<OrderPlacedEvent>()
            .Which.Type.Should().Be(OrderType.Limit);
    }

    [Fact]
    public void PlaceLimit_NonPositiveLimitPrice_Throws()
    {
        var now = DateTimeOffset.UtcNow;
        Action act = () => Order.PlaceLimit(
            "cid-pl-bad", Btc, OrderSide.Buy, 0.01m,
            limitPrice: 0m, stopPrice: null, strategyId: 1,
            mode: TradingMode.Paper, now: now, expiresAt: now.AddMinutes(5));

        act.Should().Throw<DomainException>().WithMessage("*LimitPrice*");
    }

    [Fact]
    public void PlaceLimit_ExpiresAtNotInFuture_Throws()
    {
        var now = DateTimeOffset.UtcNow;
        Action act = () => Order.PlaceLimit(
            "cid-pl-stale", Btc, OrderSide.Buy, 0.01m,
            limitPrice: 29970m, stopPrice: null, strategyId: 1,
            mode: TradingMode.Paper, now: now, expiresAt: now);

        act.Should().Throw<DomainException>().WithMessage("*ExpiresAt*");
    }

    [Fact]
    public void Expire_NewLimitOrder_TransitionsToExpired_AndEmitsEvent()
    {
        var now = DateTimeOffset.UtcNow;
        var order = Order.PlaceLimit(
            "cid-pl-expire", Btc, OrderSide.Buy, 0.01m,
            limitPrice: 29970m, stopPrice: null, strategyId: 1,
            mode: TradingMode.Paper, now: now, expiresAt: now.AddMinutes(5));
        order.ClearDomainEvents();

        order.Expire(now.AddMinutes(6));

        order.Status.Should().Be(OrderStatus.Expired);
        order.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<OrderExpiredEvent>();
    }

    [Fact]
    public void Expire_FilledOrder_NoOp()
    {
        // Idempotent guard — terminal Filled status'a Expire çağrısı yutulur.
        var now = DateTimeOffset.UtcNow;
        var order = Order.PlaceLimit(
            "cid-pl-filled", Btc, OrderSide.Buy, 0.01m,
            limitPrice: 29970m, stopPrice: null, strategyId: 1,
            mode: TradingMode.Paper, now: now, expiresAt: now.AddMinutes(5));
        order.RegisterFill(1L, 29970m, 0.01m, 0.0015m, "USDT", now);
        order.Status.Should().Be(OrderStatus.Filled);
        order.ClearDomainEvents();

        order.Expire(now.AddMinutes(6));

        order.Status.Should().Be(OrderStatus.Filled);
        order.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void RegisterFill_LimitOrder_AtLimitPrice_TransitionsToFilled()
    {
        // Paper sim limit fill: mark ask <= limit (Long) → fill @ limit price.
        var now = DateTimeOffset.UtcNow;
        var order = Order.PlaceLimit(
            "cid-pl-fill", Btc, OrderSide.Buy, 0.01m,
            limitPrice: 29970m, stopPrice: null, strategyId: 1,
            mode: TradingMode.Paper, now: now, expiresAt: now.AddMinutes(5));
        order.ClearDomainEvents();

        order.RegisterFill(42L, 29970m, 0.01m, 0.0015m, "USDT", now.AddSeconds(30));

        order.Status.Should().Be(OrderStatus.Filled);
        order.ExecutedQuantity.Should().Be(0.01m);
        order.CumulativeQuoteQty.Should().Be(299.70m);
        order.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<OrderFilledEvent>();
    }
}
