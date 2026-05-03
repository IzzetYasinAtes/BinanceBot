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
/// Loop 92 — FuturesPaperFillSimulator testleri (ADR-0025). Futures-spesifik
/// taker %0.05 fee, Long+Short symmetric fill akışı, MIN_NOTIONAL guard.
/// </summary>
public class FuturesPaperFillSimulatorTests
{
    private static FuturesPaperFillSimulator NewSim() => new(
        NullLogger<FuturesPaperFillSimulator>.Instance,
        Options.Create(new PaperFillOptions
        {
            FixedSlippagePct = 0m,           // deterministic
            SimulatedLatencyMs = 0,
            UseBnbFeeDiscount = false,
        }));

    private static Instrument NewInstrument()
    {
        var instrument = Instrument.Create(
            Symbol.From("BTCUSDT"), "BTC", "USDT", InstrumentStatus.Trading,
            tickSize: 0.10m, stepSize: 0.001m,
            minNotional: 5m, minQty: 0.001m, maxQty: 100m,
            syncedAt: DateTimeOffset.UtcNow);
        return instrument;
    }

    private static BookTicker NewBookTicker(decimal bid, decimal ask) =>
        BookTicker.Create(
            Symbol.From("BTCUSDT"),
            bidPrice: bid, bidQty: 1m,
            askPrice: ask, askQty: 1m,
            updateId: 1L, updatedAt: DateTimeOffset.UtcNow);

    [Fact]
    public async Task LongOpen_MarketBuy_DeductsFiftyBpsFromCash()
    {
        // Long open: BUY @ ask=$30000 × 0.01 = $300 notional. Fee 0.05% = $0.15.
        // Cash delta: -300 - 0.15 = -300.15.
        var sim = NewSim();
        var inst = NewInstrument();
        var bt = NewBookTicker(bid: 29990m, ask: 30000m);

        var order = Order.Place(
            "test-long-open", Symbol.From("BTCUSDT"), OrderSide.Buy,
            OrderType.Market, TimeInForce.Ioc,
            quantity: 0.01m, price: null, stopPrice: null,
            strategyId: 1, mode: TradingMode.Paper, now: DateTimeOffset.UtcNow);

        var outcome = await sim.SimulateAsync(order, inst, bt, null, DateTimeOffset.UtcNow, CancellationToken.None);

        outcome.Filled.Should().BeTrue();
        outcome.QuoteCommissionTotal.Should().BeApproximately(0.15m, 0.0001m);
        outcome.RealizedCashDelta.Should().BeApproximately(-300.15m, 0.0001m);
    }

    [Fact]
    public async Task ShortOpen_MarketSell_DeductsFiftyBpsFromCash()
    {
        // Short open: SELL @ bid=$30000 × 0.01 = $300 notional. Fee 0.05% = $0.15.
        // Cash delta: +300 - 0.15 = +299.85 (notional gelir, fee gider).
        var sim = NewSim();
        var inst = NewInstrument();
        var bt = NewBookTicker(bid: 30000m, ask: 30010m);

        var order = Order.Place(
            "test-short-open", Symbol.From("BTCUSDT"), OrderSide.Sell,
            OrderType.Market, TimeInForce.Ioc,
            quantity: 0.01m, price: null, stopPrice: null,
            strategyId: 1, mode: TradingMode.Paper, now: DateTimeOffset.UtcNow);

        var outcome = await sim.SimulateAsync(order, inst, bt, null, DateTimeOffset.UtcNow, CancellationToken.None);

        outcome.Filled.Should().BeTrue();
        outcome.QuoteCommissionTotal.Should().BeApproximately(0.15m, 0.0001m);
        outcome.RealizedCashDelta.Should().BeApproximately(+299.85m, 0.0001m);
    }

    [Fact]
    public async Task SubMinNotional_Rejected_NoFill()
    {
        // BTCUSDT minNotional 5 USDT. 0.001 × $30000 = $30 OK. 0.001 × $1 = $0.001 reject.
        var sim = NewSim();
        var inst = NewInstrument();
        var bt = NewBookTicker(bid: 0.99m, ask: 1m);  // tiny price → notional under 5 USDT

        var order = Order.Place(
            "test-min-notional", Symbol.From("BTCUSDT"), OrderSide.Buy,
            OrderType.Market, TimeInForce.Ioc,
            quantity: 0.001m, price: null, stopPrice: null,
            strategyId: null, mode: TradingMode.Paper, now: DateTimeOffset.UtcNow);

        var outcome = await sim.SimulateAsync(order, inst, bt, null, DateTimeOffset.UtcNow, CancellationToken.None);

        outcome.Filled.Should().BeFalse();
        outcome.Rejected.Should().BeTrue();
        outcome.RejectReason.Should().StartWith("filter_MIN_NOTIONAL");
    }

    [Fact]
    public async Task NonCrossingLimit_Expires_NoFill()
    {
        var sim = NewSim();
        var inst = NewInstrument();
        var bt = NewBookTicker(bid: 29990m, ask: 30000m);

        // BUY @ 29000 < ask 30000 → not crossing → expire
        var order = Order.Place(
            "test-non-cross", Symbol.From("BTCUSDT"), OrderSide.Buy,
            OrderType.Limit, TimeInForce.Gtc,
            quantity: 0.01m, price: 29000m, stopPrice: null,
            strategyId: null, mode: TradingMode.Paper, now: DateTimeOffset.UtcNow);

        var outcome = await sim.SimulateAsync(order, inst, bt, null, DateTimeOffset.UtcNow, CancellationToken.None);

        outcome.Filled.Should().BeFalse();
        outcome.Rejected.Should().BeFalse();
        outcome.RejectReason.Should().Be("limit_not_crossing");
    }

    [Fact]
    public async Task NoLiquidity_Rejected()
    {
        var sim = NewSim();
        var inst = NewInstrument();
        var bt = NewBookTicker(bid: 0m, ask: 0m);

        var order = Order.Place(
            "test-no-liq", Symbol.From("BTCUSDT"), OrderSide.Buy,
            OrderType.Market, TimeInForce.Ioc,
            quantity: 0.01m, price: null, stopPrice: null,
            strategyId: null, mode: TradingMode.Paper, now: DateTimeOffset.UtcNow);

        var outcome = await sim.SimulateAsync(order, inst, bt, null, DateTimeOffset.UtcNow, CancellationToken.None);

        outcome.Filled.Should().BeFalse();
        outcome.Rejected.Should().BeTrue();
        outcome.RejectReason.Should().Be("no_liquidity");
    }

    [Fact]
    public async Task LotSizeViolation_Rejected()
    {
        var sim = NewSim();
        var inst = NewInstrument();
        var bt = NewBookTicker(bid: 29990m, ask: 30000m);

        // 0.0001 < minQty 0.001 → reject (LOT_SIZE)
        var order = Order.Place(
            "test-lot-size", Symbol.From("BTCUSDT"), OrderSide.Buy,
            OrderType.Market, TimeInForce.Ioc,
            quantity: 0.0001m, price: null, stopPrice: null,
            strategyId: null, mode: TradingMode.Paper, now: DateTimeOffset.UtcNow);

        var outcome = await sim.SimulateAsync(order, inst, bt, null, DateTimeOffset.UtcNow, CancellationToken.None);

        outcome.Filled.Should().BeFalse();
        outcome.Rejected.Should().BeTrue();
        outcome.RejectReason.Should().StartWith("filter_LOT_SIZE");
    }

    [Fact]
    public async Task FuturesFee_QuoteAsset_NotBaseAsset()
    {
        // Spot BUY commission base asset (BTC); Futures BUY commission quote (USDT). Loop 92 fix.
        var sim = NewSim();
        var inst = NewInstrument();
        var bt = NewBookTicker(bid: 29990m, ask: 30000m);

        var order = Order.Place(
            "test-fee-asset", Symbol.From("BTCUSDT"), OrderSide.Buy,
            OrderType.Market, TimeInForce.Ioc,
            quantity: 0.01m, price: null, stopPrice: null,
            strategyId: 1, mode: TradingMode.Paper, now: DateTimeOffset.UtcNow);

        await sim.SimulateAsync(order, inst, bt, null, DateTimeOffset.UtcNow, CancellationToken.None);

        order.Fills.Should().HaveCount(1);
        var fill = order.Fills.Single();
        fill.CommissionAsset.Should().Be("USDT");  // Futures: always quote
        fill.Commission.Should().BeApproximately(0.15m, 0.0001m);  // 300 × 0.0005
    }
}
