using BinanceBot.Domain.Common;
using BinanceBot.Domain.Instruments;
using BinanceBot.Domain.MarketData;
using BinanceBot.Domain.Orders;
using BinanceBot.Domain.ValueObjects;
using BinanceBot.Infrastructure.Trading.Paper;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BinanceBot.Tests.Infrastructure.Trading;

/// <summary>
/// BUG-A regression (decision-sizing.md Reviewer Checklist §10): MARKET orders bypass
/// the LIMIT-only MIN_NOTIONAL filter — the simulator must compute notional from the
/// top-of-book (with slippage) and reject below the floor.
/// </summary>
public class PaperFillSimulator_MarketMinNotionalTests
{
    private static IOptions<PaperFillOptions> Opts(decimal slip = 0m) =>
        Options.Create(new PaperFillOptions { FixedSlippagePct = slip });

    private static Instrument BuildInstrument(decimal minNotional = 10m) =>
        Instrument.Create(
            Symbol.From("BTCUSDT"),
            "BTC", "USDT",
            InstrumentStatus.Trading,
            tickSize: 0.01m,
            stepSize: 0.00001m,
            minNotional: minNotional,
            minQty: 0.00001m,
            maxQty: 9000m,
            syncedAt: DateTimeOffset.UtcNow);

    private static BookTicker BuildBookTicker(decimal price, decimal qty) =>
        BookTicker.Create(Symbol.From("BTCUSDT"),
            bidPrice: price, bidQty: qty,
            askPrice: price, askQty: qty,
            updateId: 1, updatedAt: DateTimeOffset.UtcNow);

    [Fact]
    public void Below5UsdtNotional_Rejects()
    {
        var sut = new PaperFillSimulator(NullLogger<PaperFillSimulator>.Instance, Opts());
        var now = DateTimeOffset.UtcNow;

        var instrument = BuildInstrument(minNotional: 10m);
        var bt = BuildBookTicker(price: 30000m, qty: 1m);

        // 0.0001 * 30000 = 3 USDT, well below 10 floor
        var order = Order.Place(
            "cid-tiny", Symbol.From("BTCUSDT"), OrderSide.Buy, OrderType.Market,
            TimeInForce.Ioc, quantity: 0.0001m,
            price: null, stopPrice: null,
            strategyId: 1, mode: TradingMode.Paper, now: now);

        var outcome = sut.Simulate(order, instrument, bt, depthSnapshot: null, now: now);

        outcome.Rejected.Should().BeTrue();
        outcome.RejectReason.Should().StartWith("filter_MIN_NOTIONAL_");
        order.Status.Should().Be(OrderStatus.Rejected);
        order.ExecutedQuantity.Should().Be(0m);
    }

    [Fact]
    public void AboveMinNotional_FillsNormally()
    {
        var sut = new PaperFillSimulator(NullLogger<PaperFillSimulator>.Instance, Opts());
        var now = DateTimeOffset.UtcNow;

        var instrument = BuildInstrument(minNotional: 5m);
        var bt = BuildBookTicker(price: 30000m, qty: 1m);

        // 0.001 * 30000 = 30 USDT -> well above 5
        var order = Order.Place(
            "cid-ok", Symbol.From("BTCUSDT"), OrderSide.Buy, OrderType.Market,
            TimeInForce.Ioc, quantity: 0.001m,
            price: null, stopPrice: null,
            strategyId: 1, mode: TradingMode.Paper, now: now);

        var outcome = sut.Simulate(order, instrument, bt, depthSnapshot: null, now: now);

        outcome.Filled.Should().BeTrue();
        outcome.Rejected.Should().BeFalse();
        order.Status.Should().Be(OrderStatus.Filled);
    }

    [Fact]
    public void Slippage_AppliedToBuy_RaisesFillPrice()
    {
        // 0.0005 (5 bps) slip on 30000 -> 30015 fill price for BUY
        var sut = new PaperFillSimulator(NullLogger<PaperFillSimulator>.Instance, Opts(slip: 0.0005m));
        var now = DateTimeOffset.UtcNow;

        var instrument = BuildInstrument(minNotional: 5m);
        var bt = BuildBookTicker(price: 30000m, qty: 1m);

        var order = Order.Place(
            "cid-slip-buy", Symbol.From("BTCUSDT"), OrderSide.Buy, OrderType.Market,
            TimeInForce.Ioc, quantity: 0.001m, price: null, stopPrice: null,
            strategyId: 1, mode: TradingMode.Paper, now: now);

        var outcome = sut.Simulate(order, instrument, bt, depthSnapshot: null, now: now);

        outcome.Filled.Should().BeTrue();
        outcome.AvgFillPrice.Should().Be(30015m);
        // BUY cash delta: -price*qty = -30015 * 0.001 = -30.015
        outcome.RealizedCashDelta.Should().Be(-30.015m);
    }

    [Fact]
    public void Slippage_AppliedToSell_LowersReceivedPrice()
    {
        var sut = new PaperFillSimulator(NullLogger<PaperFillSimulator>.Instance, Opts(slip: 0.0005m));
        var now = DateTimeOffset.UtcNow;

        var instrument = BuildInstrument(minNotional: 5m);
        var bt = BuildBookTicker(price: 30000m, qty: 1m);

        var order = Order.Place(
            "cid-slip-sell", Symbol.From("BTCUSDT"), OrderSide.Sell, OrderType.Market,
            TimeInForce.Ioc, quantity: 0.001m, price: null, stopPrice: null,
            strategyId: 1, mode: TradingMode.Paper, now: now);

        var outcome = sut.Simulate(order, instrument, bt, depthSnapshot: null, now: now);

        outcome.Filled.Should().BeTrue();
        // SELL: 30000 * (1 - 0.0005) = 29985
        outcome.AvgFillPrice.Should().Be(29985m);
    }
}
