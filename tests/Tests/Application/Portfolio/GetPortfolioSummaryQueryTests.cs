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
/// Loop 19 — single-shot portfolio dashboard query. Validates the cash/equity
/// split (the bug that produced the misleading "Mevcut Bakiye $316" display)
/// and the realized/unrealized/win-rate aggregates the UI needs.
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
    /// Loop 84 cash-bug fix — the cash and equity columns must NOT collapse to the
    /// same number when an open position has unrealized PnL. Cash is the
    /// ledger-derived settled cash (start + realized − open notional − open fees);
    /// equity is cash + open MTM. The handler no longer reads
    /// VirtualBalance.CurrentBalance for the headline numbers because that snapshot
    /// drifted across iterations crossing PaperFillSimulator semantic changes
    /// (loop 84 phantom +$157 trace).
    /// </summary>
    [Fact]
    public async Task OpenPositionWithUnrealizedPnl_TrueEquityExceedsCash()
    {
        var db = NewDb();
        SeedPaper(db, 100m);
        var pos = OpenPos(db, "XRPUSDT", TradeDirection.Long, qty: 10m, entry: 2m); // cost 20
        pos.MarkToMarket(markPrice: 2.5m, now: T0.AddMinutes(1));                  // unrealized +5
        db.SaveChanges();

        var sut = new GetPortfolioSummaryQueryHandler(db, StubClock().Object);

        var result = await sut.Handle(
            new GetPortfolioSummaryQuery(TradingMode.Paper), CancellationToken.None);

        var dto = result.Value;
        // Loop 84 — ledger-driven cash: 100 start + 0 realized − 20 open notional
        // − 0 open commission = 80. Snapshot CurrentBalance (still 100m on the
        // VirtualBalance row because no fill applied via PaperFillSimulator) is
        // intentionally bypassed.
        dto.CurrentCash.Should().Be(80m);
        dto.UnrealizedPnlTotal.Should().Be(5m);
        dto.OpenPositionsValue.Should().Be(25m);            // 20 cost + 5 unrealized
        dto.TrueEquity.Should().Be(105m);                   // ledgerCash 80 + open 25
        dto.OpenPositionCount.Should().Be(1);
        // Cash-grounded netPnl = trueEquity − starting = 105 − 100 = 5 (matches
        // the +5 unrealized; closed leg fees would normally net out via
        // RealizedPnl, here zero because no Close was issued).
        dto.NetPnl.Should().Be(5m);
        dto.NetPnlPct.Should().Be(0.05m);
    }

    [Fact]
    public async Task ClosedPositions_ProduceWinLossWinRate()
    {
        var db = NewDb();
        SeedPaper(db, 100m);

        var winner = OpenPos(db, "BTCUSDT", TradeDirection.Long, qty: 0.001m, entry: 30000m);
        winner.Close(exitPrice: 35000m, reason: "tp", now: T0.AddMinutes(5));   // +5
        var loser = OpenPos(db, "ETHUSDT", TradeDirection.Short, qty: 0.02m, entry: 2500m);
        loser.Close(exitPrice: 2600m, reason: "sl", now: T0.AddMinutes(10));    // -2
        var second = OpenPos(db, "BNBUSDT", TradeDirection.Long, qty: 0.1m, entry: 500m);
        second.Close(exitPrice: 505m, reason: "tp", now: T0.AddMinutes(15));    // +0.5
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
        // Loop 84 cash-bug fix: ledger-driven cash = 100 start + 3.5 realized − 0
        // open notional − 0 open commission = 103.5. trueEquity = 103.5 + 0 open
        // value = 103.5. netPnl = 3.5 (= sum realized). The previous test pinned
        // 0m because the handler read the unmoved VirtualBalance.CurrentBalance
        // snapshot — exactly the drift mode the fix eliminates.
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
    /// Loop 84 cash-bug regression — the user reported a phantom +$157 on the UI
    /// "Toplam Net K/Z" hero (start $500, 8 closed trades realized −$0.61, 3 open
    /// positions notional $300.53, open commission −$0.23 → expected cash $197.43,
    /// snapshot showed $355.14). Root cause: <see cref="GetPortfolioSummaryQuery"/>
    /// previously read <c>VirtualBalance.CurrentBalance</c> directly. When that
    /// snapshot drifted (paper iterations crossing PaperFillSimulator semantic
    /// changes, or in-flight reset interleaving with fills) the equity formula
    /// double-counted open notional. Fix: ledger-derived cash (start + realized
    /// − open cost basis − open commission) is now the single source of truth.
    /// This test pins the exact arithmetic so a regression cannot reintroduce
    /// the snapshot-drift codepath.
    /// </summary>
    [Fact]
    public async Task LedgerCash_DerivesFromPositions_NotFromVirtualBalanceSnapshot()
    {
        var db = NewDb();
        var vb = SeedPaper(db, 500m);

        // Simulate the exact user scenario: 8 closed trades net realized −$0.61
        // (entry+exit commission already baked into Position.RealizedPnl per
        // ADR-0020 §20.6). Aggregated as 1 representative closed position.
        var closed = Position.Open(
            Symbol.From("BTCUSDT"),
            TradeDirection.Long,
            quantity: 0.01m,
            entryPrice: 100m,            // notional 1.00
            stopPrice: null,
            strategyId: null,
            mode: TradingMode.Paper,
            now: T0,
            entryCommission: 0.60m);
        closed.Close(
            exitPrice: 100m,             // gross PnL 0; net = 0 − 0.60 − 0.60 = −1.20
            reason: "test",
            now: T0.AddMinutes(5),
            exitCommission: 0.60m);
        // Sanity: this puts realized at −1.20. Adjust to match the −0.61 figure
        // by adding a small winning trade (gross +0.59).
        var winner = Position.Open(
            Symbol.From("ETHUSDT"),
            TradeDirection.Long,
            quantity: 1m,
            entryPrice: 1m,
            stopPrice: null,
            strategyId: null,
            mode: TradingMode.Paper,
            now: T0,
            entryCommission: 0m);
        winner.Close(
            exitPrice: 1.59m,
            reason: "test",
            now: T0.AddMinutes(6),
            exitCommission: 0m);
        db.Positions.Add(closed);
        db.Positions.Add(winner);

        // 3 open positions: total notional $300.53, total entry commission $0.23.
        var open1 = Position.Open(
            Symbol.From("XRPUSDT"),
            TradeDirection.Long,
            quantity: 100m,
            entryPrice: 1m,              // notional 100.00
            stopPrice: null,
            strategyId: null,
            mode: TradingMode.Paper,
            now: T0.AddMinutes(7),
            entryCommission: 0.10m);
        var open2 = Position.Open(
            Symbol.From("BNBUSDT"),
            TradeDirection.Long,
            quantity: 1m,
            entryPrice: 100.53m,         // notional 100.53
            stopPrice: null,
            strategyId: null,
            mode: TradingMode.Paper,
            now: T0.AddMinutes(8),
            entryCommission: 0.10m);
        var open3 = Position.Open(
            Symbol.From("SOLUSDT"),
            TradeDirection.Long,
            quantity: 2m,
            entryPrice: 50m,             // notional 100.00
            stopPrice: null,
            strategyId: null,
            mode: TradingMode.Paper,
            now: T0.AddMinutes(9),
            entryCommission: 0.03m);
        db.Positions.Add(open1);
        db.Positions.Add(open2);
        db.Positions.Add(open3);
        db.SaveChanges();

        // Inject the buggy snapshot value the user observed ($355.14). The fix
        // must ignore this and derive cash from the ledger instead.
        // Using ApplyFill keeps the domain encapsulation intact (no public setters).
        var drift = 355.14m - vb.CurrentBalance;
        vb.ApplyFill(drift, T0.AddMinutes(10));
        db.SaveChanges();

        var sut = new GetPortfolioSummaryQueryHandler(db, StubClock().Object);
        var result = await sut.Handle(
            new GetPortfolioSummaryQuery(TradingMode.Paper), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value;

        // Realized = closed.RealizedPnl sum = (−1.20) + 0.59 = −0.61. Confirmed
        // before asserting cash to keep the test diagnostic on regression.
        dto.RealizedPnlAllTime.Should().Be(-0.61m);

        // Open ledger: cost basis 100 + 100.53 + 100 = 300.53; open commission
        // 0.10 + 0.10 + 0.03 = 0.23. Ledger cash = 500 + (−0.61) − 300.53 − 0.23
        // = 198.63. The user's observed $355.14 snapshot is overwritten.
        dto.CurrentCash.Should().Be(198.63m);
        dto.OpenPositionsValue.Should().Be(300.53m);  // no MTM applied
        dto.TrueEquity.Should().Be(499.16m);          // 198.63 + 300.53
        dto.NetPnl.Should().Be(-0.84m);               // realized −0.61 − openCommission −0.23

        // Invariant: NetProfitAfterFees == NetPnl when no MTM drift is in play.
        dto.NetProfitAfterFees.Should().Be(dto.NetPnl);
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
