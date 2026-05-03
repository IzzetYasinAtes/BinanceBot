using BinanceBot.Application.Abstractions;
using BinanceBot.Application.Portfolio.Queries.GetPortfolioSummary;
using BinanceBot.Domain.Balances;
using BinanceBot.Domain.Common;
using BinanceBot.Domain.Orders;
using BinanceBot.Domain.Positions;
using BinanceBot.Domain.ValueObjects;
using BinanceBot.Tests.Infrastructure.Strategies;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace BinanceBot.Tests.Application.Portfolio;

/// <summary>
/// Loop 19 → Loop 93 — single-shot portfolio dashboard query.
///
/// Loop 93 §2 — Futures cash semantics (ADR-0025): <c>CurrentCash</c> is the
/// authoritative <c>VirtualBalance.WalletBalance</c> (commission − realized PnL
/// adjusted, notional NOT deducted because Futures parks notional as margin).
/// <c>TrueEquity</c> = <c>WalletBalance + Σ open.UnrealizedPnl</c>. Test setup
/// uses <see cref="VirtualBalance.ApplyFill"/> to model the wallet-side cash
/// flow that the paper simulator would emit on each fill.
/// </summary>
public class GetPortfolioSummaryQueryTests
{
    private static readonly DateTimeOffset T0 = new(2026, 4, 17, 12, 0, 0, TimeSpan.Zero);

    private static StubDbContext NewDb()
    {
        var opts = new DbContextOptionsBuilder<StubDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new StubDbContext(opts);
    }

    private static Mock<IClock> StubClock()
    {
        var clock = new Mock<IClock>();
        clock.SetupGet(c => c.UtcNow).Returns(T0);
        return clock;
    }

    private static VirtualBalance SeedPaper(StubDbContext db, decimal startingBalance)
    {
        var vb = VirtualBalance.CreateDefault(TradingMode.Paper, startingBalance, T0);
        db.VirtualBalances.Add(vb);
        db.SaveChanges();
        return vb;
    }

    private static Position OpenPos(
        StubDbContext db, string sym, TradeDirection side, decimal qty, decimal entry)
    {
        var p = Position.Open(
            Symbol.From(sym), side, qty, entry,
            stopPrice: null, strategyId: null,
            mode: TradingMode.Paper, now: T0);
        db.Positions.Add(p);
        db.SaveChanges();
        return p;
    }

    [Fact]
    public async Task NoBalanceRow_ReturnsNotFound()
    {
        var db = NewDb();
        var sut = new GetPortfolioSummaryQueryHandler(db, StubClock().Object);

        var result = await sut.Handle(
            new GetPortfolioSummaryQuery(TradingMode.Paper), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Status.Should().Be(Ardalis.Result.ResultStatus.NotFound);
    }

    [Fact]
    public async Task FreshAccount_NoPositions_ReturnsBaselineSnapshot()
    {
        var db = NewDb();
        SeedPaper(db, 100m);
        var sut = new GetPortfolioSummaryQueryHandler(db, StubClock().Object);

        var result = await sut.Handle(
            new GetPortfolioSummaryQuery(TradingMode.Paper), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value;
        dto.StartingBalance.Should().Be(100m);
        dto.CurrentCash.Should().Be(100m);
        dto.OpenPositionsValue.Should().Be(0m);
        dto.TrueEquity.Should().Be(100m);
        dto.OpenPositionCount.Should().Be(0);
        dto.ClosedTradeCount.Should().Be(0);
        dto.WinningTrades.Should().Be(0);
        dto.LosingTrades.Should().Be(0);
        dto.WinRate.Should().Be(0m);
        dto.NetPnl.Should().Be(0m);
    }

    /// <summary>
    /// Loop 93 §2 — Futures semantics: cash and equity columns must NOT collapse
    /// to the same number when an open position has unrealized PnL. Cash is the
    /// settled <c>WalletBalance</c> (paper sim's <c>ApplyFill</c> result); equity
    /// is <c>cash + Σ unrealized</c>. Setup uses <see cref="VirtualBalance.ApplyFill"/>
    /// to simulate the −commission deduction the paper simulator emits on a BUY
    /// open (notional itself is locked into margin, not removed from cash —
    /// that is the Futures invariant the previous Spot-era subtraction broke).
    /// </summary>
    [Fact]
    public async Task OpenPositionWithUnrealizedPnl_TrueEquityExceedsCash()
    {
        var db = NewDb();
        var vb = SeedPaper(db, 100m);
        // Paper sim would deduct commission only (notional → margin, not cash).
        // 0.05% taker fee × $20 notional = $0.01 wallet impact for the open leg.
        vb.ApplyFill(-0.01m, T0.AddSeconds(1));
        db.SaveChanges();

        var pos = OpenPos(db, "XRPUSDT", TradeDirection.Long, qty: 10m, entry: 2m); // notional 20
        pos.MarkToMarket(markPrice: 2.5m, now: T0.AddMinutes(1));                  // unrealized +5
        db.SaveChanges();

        var sut = new GetPortfolioSummaryQueryHandler(db, StubClock().Object);

        var result = await sut.Handle(
            new GetPortfolioSummaryQuery(TradingMode.Paper), CancellationToken.None);

        var dto = result.Value;
        // Loop 93 — wallet-driven cash: WalletBalance after the fee-only fill = 99.99.
        dto.CurrentCash.Should().Be(99.99m);
        dto.UnrealizedPnlTotal.Should().Be(5m);
        dto.OpenPositionsValue.Should().Be(25m);            // mark 2.5 × qty 10
        dto.TrueEquity.Should().Be(104.99m);                // wallet 99.99 + unrealized 5
        dto.OpenPositionCount.Should().Be(1);
        // Cash-grounded netPnl = trueEquity − starting = 104.99 − 100 = 4.99
        // (= +5 unrealized − 0.01 commission already booked into the wallet).
        dto.NetPnl.Should().Be(4.99m);
        dto.NetPnlPct.Should().Be(0.0499m);
    }

    [Fact]
    public async Task ClosedPositions_ProduceWinLossWinRate()
    {
        var db = NewDb();
        var vb = SeedPaper(db, 100m);

        var winner = OpenPos(db, "BTCUSDT", TradeDirection.Long, qty: 0.001m, entry: 30000m);
        winner.Close(exitPrice: 35000m, reason: "tp", now: T0.AddMinutes(5));   // +5
        var loser = OpenPos(db, "ETHUSDT", TradeDirection.Short, qty: 0.02m, entry: 2500m);
        loser.Close(exitPrice: 2600m, reason: "sl", now: T0.AddMinutes(10));    // -2
        var second = OpenPos(db, "BNBUSDT", TradeDirection.Long, qty: 0.1m, entry: 500m);
        second.Close(exitPrice: 505m, reason: "tp", now: T0.AddMinutes(15));    // +0.5
        db.SaveChanges();

        // Loop 93 §2 — Futures cash flow: realized PnL is rolled into the wallet
        // by the close path (paper sim → VirtualBalance.ApplyFill / ReturnMarginAndApplyPnl).
        // The test models the net effect (+5 −2 +0.5 = +3.5) as a single ApplyFill
        // so the assertion runs against the same authoritative cash field the
        // handler reads.
        vb.ApplyFill(3.5m, T0.AddMinutes(20));
        db.SaveChanges();

        var sut = new GetPortfolioSummaryQueryHandler(db, StubClock().Object);

        var result = await sut.Handle(
            new GetPortfolioSummaryQuery(TradingMode.Paper), CancellationToken.None);

        var dto = result.Value;
        dto.ClosedTradeCount.Should().Be(3);
        dto.WinningTrades.Should().Be(2);
        dto.LosingTrades.Should().Be(1);
        dto.WinRate.Should().BeApproximately(2m / 3m, 1e-6m);
        dto.RealizedPnlAllTime.Should().Be(3.5m);
        dto.RealizedPnl24h.Should().Be(3.5m);
        // Loop 93 — wallet-driven cash: WalletBalance = 100 + 3.5 = 103.5.
        // Handler reads WalletBalance directly; no notional adjustment because
        // there are no open positions left in this scenario.
        dto.CurrentCash.Should().Be(103.5m);
        dto.NetPnl.Should().Be(3.5m);
    }

    /// <summary>
    /// Loop 32 fix-a — invariant. `NetPnl` hero metriği tek bir tutarlılık sağlanan
    /// değerle gösterilir: <c>trueEquity - startingBalance</c>. Component metrikleri
    /// (RealizedPnlAllTime + UnrealizedPnlTotal) PaperFillSimulator BUY/SELL fee
    /// asimetrisi (ADR-0020 tracklenen) nedeniyle trueEquity'den ~fee_quote kadar
    /// sapabilir. Bu test üç farklı senaryoda (fresh, açık poz unrealized, kapalı
    /// poz realized) invariant'ın kırılmadığını doğrular.
    /// </summary>
    [Fact]
    public async Task NetPnl_Invariant_EqualsTrueEquityMinusStartingBalance_FreshAccount()
    {
        var db = NewDb();
        SeedPaper(db, 250m);
        var sut = new GetPortfolioSummaryQueryHandler(db, StubClock().Object);

        var result = await sut.Handle(
            new GetPortfolioSummaryQuery(TradingMode.Paper), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value;
        dto.NetPnl.Should().Be(dto.TrueEquity - dto.StartingBalance);
        dto.NetPnl.Should().Be(0m);
    }

    [Fact]
    public async Task NetPnl_Invariant_EqualsTrueEquityMinusStartingBalance_OpenUnrealized()
    {
        var db = NewDb();
        SeedPaper(db, 100m);
        var pos = OpenPos(db, "XRPUSDT", TradeDirection.Long, qty: 10m, entry: 2m);
        pos.MarkToMarket(markPrice: 2.5m, now: T0.AddMinutes(1));
        db.SaveChanges();

        var sut = new GetPortfolioSummaryQueryHandler(db, StubClock().Object);

        var result = await sut.Handle(
            new GetPortfolioSummaryQuery(TradingMode.Paper), CancellationToken.None);

        var dto = result.Value;
        dto.NetPnl.Should().Be(dto.TrueEquity - dto.StartingBalance);
    }

    [Fact]
    public async Task NetPnl_Invariant_EqualsTrueEquityMinusStartingBalance_ClosedPlusOpen()
    {
        var db = NewDb();
        SeedPaper(db, 500m);

        var closed = OpenPos(db, "BTCUSDT", TradeDirection.Long, qty: 0.001m, entry: 30000m);
        closed.Close(exitPrice: 31000m, reason: "tp", now: T0.AddMinutes(5));

        var openPos = OpenPos(db, "ETHUSDT", TradeDirection.Long, qty: 0.05m, entry: 2500m);
        openPos.MarkToMarket(markPrice: 2600m, now: T0.AddMinutes(6));
        db.SaveChanges();

        var sut = new GetPortfolioSummaryQueryHandler(db, StubClock().Object);

        var result = await sut.Handle(
            new GetPortfolioSummaryQuery(TradingMode.Paper), CancellationToken.None);

        var dto = result.Value;
        dto.NetPnl.Should().Be(dto.TrueEquity - dto.StartingBalance);
    }

    [Fact]
    public async Task LiveMainnet_NoBalance_StillReturnsNotFound()
    {
        var db = NewDb();
        var sut = new GetPortfolioSummaryQueryHandler(db, StubClock().Object);

        var result = await sut.Handle(
            new GetPortfolioSummaryQuery(TradingMode.LiveMainnet), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Status.Should().Be(Ardalis.Result.ResultStatus.NotFound);
    }

    /// <summary>
    /// Loop 93 §2 — Futures cash regression. Loop 92 t30 dump showed
    /// VirtualBalance.WalletBalance = $399.99 (correct: only fees deducted,
    /// notional locked in margin) but the dashboard rendered $79.16 because the
    /// Spot-era handler subtracted open notional + commission from a synthesised
    /// "ledger cash". The fix reads <c>WalletBalance</c> as the single source of
    /// truth for <c>CurrentCash</c>; <c>OpenPositionsValue</c> shows mark notional
    /// for display only and is NOT folded into cash.
    /// </summary>
    [Fact]
    public async Task CurrentCash_ReadsWalletBalance_NotSubtractedByOpenNotional()
    {
        var db = NewDb();
        var vb = SeedPaper(db, 500m);

        // Simulate Loop 92 t30 wallet trajectory: 3 fee-only fills (open BUY/SELL
        // legs in Futures park notional in margin). Total fee = $0.1517.
        vb.ApplyFill(-0.0508m, T0.AddMinutes(1));
        vb.ApplyFill(-0.0500m, T0.AddMinutes(2));
        vb.ApplyFill(-0.0509m, T0.AddMinutes(3));
        db.SaveChanges();

        // 3 open positions matching the t30 snapshot (BTC short, ADA long,
        // ETH long). Mark prices set so unrealized PnL replays the report.
        var btcShort = Position.Open(
            Symbol.From("BTCUSDT"),
            TradeDirection.Short,
            quantity: 0.0013m,
            entryPrice: 78315.32m,
            stopPrice: null,
            strategyId: null,
            mode: TradingMode.Paper,
            now: T0.AddMinutes(1),
            entryCommission: 0.0509m);
        btcShort.MarkToMarket(78359m, T0.AddMinutes(4));
        var adaLong = Position.Open(
            Symbol.From("ADAUSDT"),
            TradeDirection.Long,
            quantity: 403m,
            entryPrice: 0.2483m,
            stopPrice: null,
            strategyId: null,
            mode: TradingMode.Paper,
            now: T0.AddMinutes(2),
            entryCommission: 0.0124m);
        adaLong.MarkToMarket(0.24813m, T0.AddMinutes(4));
        var ethLong = Position.Open(
            Symbol.From("ETHUSDT"),
            TradeDirection.Long,
            quantity: 0.044m,
            entryPrice: 2309.03m,
            stopPrice: null,
            strategyId: null,
            mode: TradingMode.Paper,
            now: T0.AddMinutes(3),
            entryCommission: 0.0508m);
        ethLong.MarkToMarket(2308.19m, T0.AddMinutes(4));
        db.Positions.Add(btcShort);
        db.Positions.Add(adaLong);
        db.Positions.Add(ethLong);
        db.SaveChanges();

        var sut = new GetPortfolioSummaryQueryHandler(db, StubClock().Object);
        var result = await sut.Handle(
            new GetPortfolioSummaryQuery(TradingMode.Paper), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value;

        // CurrentCash MUST equal the WalletBalance (start − fees). Previously
        // the handler returned ~$79 because it subtracted open notional too.
        dto.CurrentCash.Should().Be(vb.WalletBalance);
        dto.CurrentCash.Should().BeApproximately(499.85m, 0.01m);

        // OpenPositionsValue is mark*qty across all 3 (sign-agnostic).
        dto.OpenPositionsValue.Should().Be(
            btcShort.MarkPrice * btcShort.Quantity
          + adaLong.MarkPrice * adaLong.Quantity
          + ethLong.MarkPrice * ethLong.Quantity);

        // TrueEquity = WalletBalance + Σ unrealized.
        dto.TrueEquity.Should().Be(dto.CurrentCash + dto.UnrealizedPnlTotal);

        // NetPnl invariant.
        dto.NetPnl.Should().Be(dto.TrueEquity - dto.StartingBalance);

        // Closed leg empty in this scenario.
        dto.ClosedTradeCount.Should().Be(0);
        dto.OpenPositionCount.Should().Be(3);
    }

    /// <summary>
    /// ADR-0020 §20.8 — the commission total now reads from the Position
    /// aggregate's own quote-denominated ledger (EntryCommission + ExitCommission),
    /// not from the OrderFills.Commission mixed-currency aggregation that was
    /// yielding BTC-value+USDT-value sums in loop 32 diagnosis §4.4.
    /// Staging: 3 closed positions each with entry+exit commissions set; the
    /// handler aggregates them to a single USDT total.
    /// </summary>
    [Fact]
    public async Task TotalCommissionPaid_ReadsFromPositionAggregate_EntryPlusExit()
    {
        var db = NewDb();
        SeedPaper(db, 1000m);

        // 3 closed paper positions, each with entry+exit quote fee = 0.003825
        // (0.001 BTC × $5100 × 0.075% BNB-discount per leg). Total expected =
        // 3 × (0.003825 + 0.003825) = 0.022950 USDT.
        for (var i = 0; i < 3; i++)
        {
            var pos = Position.Open(
                Symbol.From("BTCUSDT"),
                TradeDirection.Long,
                quantity: 0.001m,
                entryPrice: 5100m,
                stopPrice: null,
                strategyId: null,
                mode: TradingMode.Paper,
                now: T0,
                entryCommission: 0.003825m);
            pos.Close(
                exitPrice: 5110m,
                reason: "tp",
                now: T0.AddMinutes(5),
                exitCommission: 0.003825m);
            db.Positions.Add(pos);
        }
        db.SaveChanges();

        var sut = new GetPortfolioSummaryQueryHandler(db, StubClock().Object);
        var result = await sut.Handle(
            new GetPortfolioSummaryQuery(TradingMode.Paper), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        // 3 × (0.003825 + 0.003825) = 0.02295 USDT — fee-aware aggregate.
        result.Value.TotalCommissionPaid.Should().Be(0.02295m);
    }
}
