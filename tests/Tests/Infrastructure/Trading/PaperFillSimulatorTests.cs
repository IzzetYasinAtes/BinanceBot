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

public class PaperFillSimulatorTests
{
    // These existing tests focus on depth-walk + commission semantics; slippage is exercised
    // separately in PaperFillSimulator_MarketMinNotionalTests.
    // ADR-0012 §12.9: tests pin SimulatedLatencyMs=0 to keep the suite fast.
    private static IOptions<PaperFillOptions> NoSlipOpts() =>
        Options.Create(new PaperFillOptions { FixedSlippagePct = 0m, SimulatedLatencyMs = 0 });

    private static Instrument BuildInstrument() => Instrument.Create(
        Symbol.From("BTCUSDT"),
        "BTC", "USDT",
        InstrumentStatus.Trading,
        tickSize: 0.01m,
        stepSize: 0.00001m,
        minNotional: 10m,
        minQty: 0.00001m,
        maxQty: 9000m,
        syncedAt: DateTimeOffset.UtcNow);

    private static BookTicker BuildBookTicker(decimal bidPrice, decimal askPrice, decimal qty) =>
        BookTicker.Create(Symbol.From("BTCUSDT"),
            bidPrice: bidPrice, bidQty: qty,
            askPrice: askPrice, askQty: qty,
            updateId: 1, updatedAt: DateTimeOffset.UtcNow);

    [Fact]
    public async Task Simulate_MarketBuy_HappyPath_FillsAtBestAsk_BaseCommission()
    {
        var sut = new PaperFillSimulator(NullLogger<PaperFillSimulator>.Instance, NoSlipOpts());
        var now = DateTimeOffset.UtcNow;

        var instrument = BuildInstrument();
        var bt = BuildBookTicker(bidPrice: 29990m, askPrice: 30000m, qty: 1m);

        var order = Order.Place(
            "cid-p-buy", Symbol.From("BTCUSDT"), OrderSide.Buy, OrderType.Market,
            TimeInForce.Ioc, 0.001m, null, null, 1, TradingMode.Paper, now);

        var outcome = await sut.SimulateAsync(order, instrument, bt, depthSnapshot: null, now, CancellationToken.None);

        outcome.Filled.Should().BeTrue();
        outcome.Rejected.Should().BeFalse();
        outcome.ExecutedQuantity.Should().Be(0.001m);
        outcome.AvgFillPrice.Should().Be(30000m);
        // BUY -> realized cash delta is -price*qty (commission in base, not in cash)
        outcome.RealizedCashDelta.Should().Be(-30m);
        order.Status.Should().Be(OrderStatus.Filled);

        // Commission: BUY -> base asset, 0.001 * 0.1% = 0.000001 BTC
        order.Fills.Should().ContainSingle()
            .Which.CommissionAsset.Should().Be("BTC");
    }

    [Fact]
    public async Task Simulate_MarketSell_HappyPath_FillsAtBestBid_QuoteCommission()
    {
        var sut = new PaperFillSimulator(NullLogger<PaperFillSimulator>.Instance, NoSlipOpts());
        var now = DateTimeOffset.UtcNow;

        var instrument = BuildInstrument();
        var bt = BuildBookTicker(bidPrice: 29990m, askPrice: 30000m, qty: 1m);

        var order = Order.Place(
            "cid-p-sell", Symbol.From("BTCUSDT"), OrderSide.Sell, OrderType.Market,
            TimeInForce.Ioc, 0.001m, null, null, 1, TradingMode.Paper, now);

        var outcome = await sut.SimulateAsync(order, instrument, bt, depthSnapshot: null, now, CancellationToken.None);

        outcome.Filled.Should().BeTrue();
        outcome.ExecutedQuantity.Should().Be(0.001m);
        outcome.AvgFillPrice.Should().Be(29990m);
        // SELL commission quote: 29990 * 0.001 * 0.001 = 0.02999 USDT
        // realized cash = +29990 * 0.001 - 0.02999 = 29.96001
        outcome.RealizedCashDelta.Should().BeApproximately(29.96001m, 0.00001m);
        order.Fills.Should().ContainSingle()
            .Which.CommissionAsset.Should().Be("USDT");
    }

    [Fact]
    public async Task Simulate_RejectsWhenQuantityBelowLotSize()
    {
        var sut = new PaperFillSimulator(NullLogger<PaperFillSimulator>.Instance, NoSlipOpts());
        var now = DateTimeOffset.UtcNow;

        var instrument = BuildInstrument();
        var bt = BuildBookTicker(29990m, 30000m, 1m);

        var order = Order.Place(
            "cid-bad", Symbol.From("BTCUSDT"), OrderSide.Buy, OrderType.Market,
            TimeInForce.Ioc,
            quantity: 0.000001m, // below minQty (0.00001)
            null, null, 1, TradingMode.Paper, now);

        var outcome = await sut.SimulateAsync(order, instrument, bt, null, now, CancellationToken.None);

        outcome.Rejected.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.Rejected);
    }

    [Fact]
    public async Task Simulate_LimitMakerThatCrosses_Rejected_MinusTwoThousandTen()
    {
        var sut = new PaperFillSimulator(NullLogger<PaperFillSimulator>.Instance, NoSlipOpts());
        var now = DateTimeOffset.UtcNow;

        var instrument = BuildInstrument();
        var bt = BuildBookTicker(bidPrice: 29990m, askPrice: 30000m, qty: 1m);

        var order = Order.Place(
            "cid-lm", Symbol.From("BTCUSDT"), OrderSide.Buy, OrderType.LimitMaker,
            TimeInForce.Gtc, 0.001m, price: 30010m, stopPrice: null,
            strategyId: 1, mode: TradingMode.Paper, now);

        var outcome = await sut.SimulateAsync(order, instrument, bt, null, now, CancellationToken.None);

        outcome.Rejected.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.Rejected);
    }

    [Fact]
    public async Task Simulate_MarketBuy_WalksDepthLevels_ProducesSlippage()
    {
        var sut = new PaperFillSimulator(NullLogger<PaperFillSimulator>.Instance, NoSlipOpts());
        var now = DateTimeOffset.UtcNow;

        var instrument = BuildInstrument();
        var bt = BuildBookTicker(29990m, 30000m, 0.0005m);

        // Depth: asks ascending — 0.0005 at 30000, 0.0005 at 30010, remainder at 30020
        var asksJson = "[[\"30000.00\",\"0.0005\"],[\"30010.00\",\"0.0005\"],[\"30020.00\",\"0.001\"]]";
        var bidsJson = "[[\"29990.00\",\"1\"]]";
        var snapshot = OrderBookSnapshot.Capture(Symbol.From("BTCUSDT"),
            lastUpdateId: 42, bidsJson: bidsJson, asksJson: asksJson, capturedAt: now);

        var order = Order.Place(
            "cid-walk", Symbol.From("BTCUSDT"), OrderSide.Buy, OrderType.Market,
            TimeInForce.Ioc, 0.001m, null, null, 1, TradingMode.Paper, now);

        var outcome = await sut.SimulateAsync(order, instrument, bt, snapshot, now, CancellationToken.None);

        outcome.Filled.Should().BeTrue();
        order.Fills.Should().HaveCount(2);
        outcome.AvgFillPrice.Should().Be(30005m); // (0.0005*30000 + 0.0005*30010) / 0.001
    }
}
