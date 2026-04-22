using BinanceBot.Domain.Balances;
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
    private static IOptions<PaperFillOptions> NoSlipOpts(bool bnbDiscount = false) =>
        Options.Create(new PaperFillOptions
        {
            FixedSlippagePct = 0m,
            SimulatedLatencyMs = 0,
            UseBnbFeeDiscount = bnbDiscount,
        });

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
        // ADR-0020 §20.7 cash-symmetric — BUY cash delta = -price*qty - quoteFee.
        // Notional 30, standard 0.10% fee = 0.03 USDT, so cash delta = -30.03.
        outcome.RealizedCashDelta.Should().Be(-30.03m);
        outcome.QuoteCommissionTotal.Should().Be(0.03m);
        order.Status.Should().Be(OrderStatus.Filled);

        // Commission ledger shape stays Binance-compatible: BUY reports base asset.
        // 0.03 quote / 30000 price = 0.000001 BTC.
        var fill = order.Fills.Should().ContainSingle().Subject;
        fill.CommissionAsset.Should().Be("BTC");
        fill.Commission.Should().Be(0.000001m);
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

    // ---------- ADR-0019 §19.9 fee chain verification ----------
    //
    // binance-expert Loop 30 research §5 red flag: 21 trade/saat × $10 × 0.0015
    // round-trip fee = $0.315/saat vs ~$0.25/saat projected gross. If the paper
    // fee chain silently bypasses commission anywhere, Loop 30 looks profitable
    // when it isn't. These tests lock the three invariants the ADR demands:
    //   (a) PaperFeeSimulator.CalculateCommission branches on UseBnbFeeDiscount.
    //   (b) Each fill writes the real commission number to OrderFill.Commission.
    //   (c) VirtualBalance.ApplyFill sees a cash delta that is net of commission
    //       on the sell leg (buy leg commission is denominated in base, not
    //       cash — ledger consistency check).

    [Fact]
    public void CalculateFee_BnbDiscountTrue_Equals_Notional_Times_75Bps()
    {
        // ADR-0019 §19.9 — BNB-discount taker = 0.075%.
        // 5.10 notional × 0.00075 = 0.003825.
        PaperFeeSimulator.CalculateCommission(5.10m, bnbDiscount: true)
            .Should().Be(0.003825m);
        // 10 USD sizing target × 0.00075 = 0.0075.
        PaperFeeSimulator.CalculateCommission(10m, bnbDiscount: true)
            .Should().Be(0.0075m);
    }

    [Fact]
    public void CalculateFee_BnbDiscountFalse_Equals_Notional_Times_100Bps()
    {
        // ADR-0019 §19.9 — standard VIP0 taker = 0.100%.
        PaperFeeSimulator.CalculateCommission(5.10m, bnbDiscount: false)
            .Should().Be(0.00510m);
        PaperFeeSimulator.CalculateCommission(10m, bnbDiscount: false)
            .Should().Be(0.010m);
    }

    [Fact]
    public async Task Fill_WritesCommissionToOrderFill_UnderBnbDiscount()
    {
        // ADR-0019 §19.9 (b): the per-fill ledger must carry the discounted rate.
        // SELL side so the commission is expressed in quote (USDT) and the
        // numeric value is directly comparable to the 0.075% formula.
        var sut = new PaperFillSimulator(
            NullLogger<PaperFillSimulator>.Instance, NoSlipOpts(bnbDiscount: true));
        var now = DateTimeOffset.UtcNow;

        var instrument = BuildInstrument();
        var bt = BuildBookTicker(bidPrice: 30000m, askPrice: 30010m, qty: 1m);

        var order = Order.Place(
            "cid-feediscount", Symbol.From("BTCUSDT"), OrderSide.Sell, OrderType.Market,
            TimeInForce.Ioc, quantity: 0.001m, null, null,
            strategyId: 1, TradingMode.Paper, now);

        var outcome = await sut.SimulateAsync(order, instrument, bt, null, now, CancellationToken.None);

        outcome.Filled.Should().BeTrue();
        // Notional = 0.001 × 30000 = 30 USDT. Commission @ 0.075% = 0.0225 USDT.
        order.Fills.Should().ContainSingle()
            .Which.Commission.Should().Be(0.0225m);
        order.Fills.Single().CommissionAsset.Should().Be("USDT");
        // Cash delta on SELL = +30 − 0.0225 = 29.9775 (fee deducted).
        outcome.RealizedCashDelta.Should().Be(29.9775m);
    }

    [Fact]
    public async Task Fill_WritesCommissionToOrderFill_UnderStandardRate()
    {
        // ADR-0019 §19.9 (b) symmetric branch — no BNB discount, expect the
        // 0.10% commission to appear on the fill record.
        var sut = new PaperFillSimulator(
            NullLogger<PaperFillSimulator>.Instance, NoSlipOpts(bnbDiscount: false));
        var now = DateTimeOffset.UtcNow;

        var instrument = BuildInstrument();
        var bt = BuildBookTicker(bidPrice: 30000m, askPrice: 30010m, qty: 1m);

        var order = Order.Place(
            "cid-feefull", Symbol.From("BTCUSDT"), OrderSide.Sell, OrderType.Market,
            TimeInForce.Ioc, quantity: 0.001m, null, null,
            strategyId: 1, TradingMode.Paper, now);

        var outcome = await sut.SimulateAsync(order, instrument, bt, null, now, CancellationToken.None);

        // 0.001 × 30000 × 0.001 = 0.03 USDT.
        order.Fills.Single().Commission.Should().Be(0.03m);
    }

    [Fact]
    public async Task VirtualBalance_ApplyFill_DeductsCommission_OnSellLeg()
    {
        // ADR-0019 §19.9 (c): after a Sell fill the VirtualBalance cash delta
        // equals gross notional minus commission. On the Buy leg the commission
        // is taken in base asset, so the quote-currency cash delta is just
        // −(price × qty) — that invariant is locked below.
        var sut = new PaperFillSimulator(
            NullLogger<PaperFillSimulator>.Instance, NoSlipOpts(bnbDiscount: true));
        var now = DateTimeOffset.UtcNow;
        var instrument = BuildInstrument();
        var bt = BuildBookTicker(bidPrice: 30000m, askPrice: 30010m, qty: 1m);

        var vb = VirtualBalance.CreateDefault(TradingMode.Paper, startingBalance: 1000m, now);

        // ADR-0020 §20.7 cash-symmetric — BUY and SELL both deduct the quote fee.
        // BUY leg — qty 0.001 × ask 30010 = 30.01 notional; fee = 0.00075 × 30.01 = 0.0225075;
        // cash delta = −30.01 − 0.0225075 = −30.0325075.
        var buy = Order.Place(
            "cid-vb-buy", Symbol.From("BTCUSDT"), OrderSide.Buy, OrderType.Market,
            TimeInForce.Ioc, 0.001m, null, null, 1, TradingMode.Paper, now);
        var buyOut = await sut.SimulateAsync(buy, instrument, bt, null, now, CancellationToken.None);
        vb.ApplyFill(buyOut.RealizedCashDelta, now);

        buyOut.QuoteCommissionTotal.Should().Be(0.0225075m);
        vb.CurrentBalance.Should().Be(1000m - 30.0325075m);
        buy.Fills.Single().Commission.Should().BeGreaterThan(0m); // base commission present

        // SELL leg — fill at bid 30000, fee = 0.075% × 30 = 0.0225, cash = +30 − 0.0225 = 29.9775.
        var sell = Order.Place(
            "cid-vb-sell", Symbol.From("BTCUSDT"), OrderSide.Sell, OrderType.Market,
            TimeInForce.Ioc, 0.001m, null, null, 1, TradingMode.Paper, now);
        var sellOut = await sut.SimulateAsync(sell, instrument, bt, null, now, CancellationToken.None);
        vb.ApplyFill(sellOut.RealizedCashDelta, now);

        sellOut.RealizedCashDelta.Should().Be(29.9775m);
        sellOut.QuoteCommissionTotal.Should().Be(0.0225m);
        // Final cash = 1000 − 30.0325075 + 29.9775 = 999.9449925 (round-trip loss includes both fees).
        vb.CurrentBalance.Should().Be(999.9449925m);
    }

    [Fact]
    public async Task Simulate_BuyThenSellSameSize_CashDelta_EqualsMinusTwoQuoteFees()
    {
        // ADR-0020 §20.7 invariant — symmetric BUY+SELL at identical price/qty
        // should leave cash reduced by exactly 2 × quote fee (no ghost fees,
        // no asymmetric missing commission). This is the cash-symmetric core test.
        var sut = new PaperFillSimulator(
            NullLogger<PaperFillSimulator>.Instance, NoSlipOpts(bnbDiscount: false));
        var now = DateTimeOffset.UtcNow;
        var instrument = BuildInstrument();
        // Identical price both ways so only fees move the balance.
        var bt = BuildBookTicker(bidPrice: 30000m, askPrice: 30000m, qty: 1m);

        var buy = Order.Place(
            "cid-sym-buy", Symbol.From("BTCUSDT"), OrderSide.Buy, OrderType.Market,
            TimeInForce.Ioc, 0.001m, null, null, 1, TradingMode.Paper, now);
        var buyOut = await sut.SimulateAsync(buy, instrument, bt, null, now, CancellationToken.None);

        var sell = Order.Place(
            "cid-sym-sell", Symbol.From("BTCUSDT"), OrderSide.Sell, OrderType.Market,
            TimeInForce.Ioc, 0.001m, null, null, 1, TradingMode.Paper, now);
        var sellOut = await sut.SimulateAsync(sell, instrument, bt, null, now, CancellationToken.None);

        var cashDelta = buyOut.RealizedCashDelta + sellOut.RealizedCashDelta;
        var totalQuoteFee = buyOut.QuoteCommissionTotal + sellOut.QuoteCommissionTotal;

        // Round-trip cash delta equals exactly minus the sum of quote fees.
        cashDelta.Should().Be(-totalQuoteFee);
        // And that sum equals 2 × notional × 0.10% = 0.06 USDT.
        totalQuoteFee.Should().Be(0.06m);
    }
}
