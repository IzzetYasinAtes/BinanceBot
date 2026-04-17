using BinanceBot.Domain.Common;
using BinanceBot.Domain.Orders;
using BinanceBot.Domain.Orders.Events;
using BinanceBot.Domain.ValueObjects;
using FluentAssertions;

namespace BinanceBot.Tests.Domain.Orders;

public class OrderModeTests
{
    [Theory]
    [InlineData(TradingMode.Paper)]
    [InlineData(TradingMode.LiveTestnet)]
    [InlineData(TradingMode.LiveMainnet)]
    public void Place_PersistsMode_AndRaisesEventWithMode(TradingMode mode)
    {
        var order = Order.Place(
            clientOrderId: $"sig-1-1744819200-{mode.ToCidSuffix()}",
            symbol: Symbol.From("BTCUSDT"),
            side: OrderSide.Buy,
            type: OrderType.Market,
            timeInForce: TimeInForce.Ioc,
            quantity: 0.001m,
            price: null,
            stopPrice: null,
            strategyId: 1,
            mode: mode,
            now: DateTimeOffset.UtcNow);

        order.Mode.Should().Be(mode);
        order.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<OrderPlacedEvent>()
            .Which.Mode.Should().Be(mode);
    }

    [Theory]
    [InlineData(TradingMode.Paper, "p")]
    [InlineData(TradingMode.LiveTestnet, "lt")]
    [InlineData(TradingMode.LiveMainnet, "lm")]
    public void ToCidSuffix_ReturnsExpectedShortCode(TradingMode mode, string expected)
    {
        mode.ToCidSuffix().Should().Be(expected);
    }

    [Fact]
    public void RegisterFill_FilledStatus_EmitsOrderFilledEventWithMode()
    {
        var order = Order.Place(
            "cid-p", Symbol.From("BTCUSDT"), OrderSide.Buy, OrderType.Market,
            TimeInForce.Ioc, 0.001m, null, null, 1, TradingMode.Paper, DateTimeOffset.UtcNow);
        order.ClearDomainEvents();

        order.RegisterFill(42L, 30000m, 0.001m, 0.000001m, "BTC", DateTimeOffset.UtcNow);

        order.Status.Should().Be(OrderStatus.Filled);
        order.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<OrderFilledEvent>()
            .Which.Mode.Should().Be(TradingMode.Paper);
    }
}
