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
        StubDbContext db, string sym, PositionSide side, decimal qty, decimal entry)
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
    /// Loop 19 acceptance — the cash and equity columns must NOT collapse to the
    /// same number when an open position has unrealized PnL. Cash is the settled
    /// VirtualBalance; equity is cash + open MTM. The /api/balances endpoint
    /// returned both equal because VirtualBalance.Equity raced unrealized writes.
    /// </summary>
    [Fact]
    public async Task OpenPositionWithUnrealizedPnl_TrueEquityExceedsCash()
    {
        var db = NewDb();
        SeedPaper(db, 100m);
        var pos = OpenPos(db, "XRPUSDT", PositionSide.Long, qty: 10m, entry: 2m); // cost 20
        pos.MarkToMarket(markPrice: 2.5m, now: T0.AddMinutes(1));                  // unrealized +5
        db.SaveChanges();

        var sut = new GetPortfolioSummaryQueryHandler(db, StubClock().Object);

        var result = await sut.Handle(
            new GetPortfolioSummaryQuery(TradingMode.Paper), CancellationToken.None);

        var dto = result.Value;
        dto.CurrentCash.Should().Be(100m);                  // cash untouched (no fill applied)
        dto.UnrealizedPnlTotal.Should().Be(5m);
        dto.OpenPositionsValue.Should().Be(25m);            // 20 cost + 5 unrealized
        dto.TrueEquity.Should().Be(125m);                   // cash + open value
        dto.OpenPositionCount.Should().Be(1);
        // Loop 32 fix-a: netPnl cash-grounded (trueEquity - starting).
        // Bu senaryoda fill yok, cash=100, trueEquity=125, starting=100 → netPnl=25.
        // Component gross toplamı (0 realized + 5 unrealized = 5) artık hero
        // metriği değil; UI ayrı `UnrealizedPnlTotal` alanından gösterecek.
        dto.NetPnl.Should().Be(25m);
        dto.NetPnlPct.Should().Be(0.25m);
    }

    [Fact]
    public async Task ClosedPositions_ProduceWinLossWinRate()
    {
        var db = NewDb();
        SeedPaper(db, 100m);

        var winner = OpenPos(db, "BTCUSDT", PositionSide.Long, qty: 0.001m, entry: 30000m);
        winner.Close(exitPrice: 35000m, reason: "tp", now: T0.AddMinutes(5));   // +5
        var loser = OpenPos(db, "ETHUSDT", PositionSide.Short, qty: 0.02m, entry: 2500m);
        loser.Close(exitPrice: 2600m, reason: "sl", now: T0.AddMinutes(10));    // -2
        var second = OpenPos(db, "BNBUSDT", PositionSide.Long, qty: 0.1m, entry: 500m);
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
        // Loop 32 fix-a: netPnl cash-grounded. Bu testte Position.Close çağırılıyor
        // ama fill oluşturulmuyor; StubDbContext'te cash (VirtualBalance.CurrentBalance)
        // 100 kalıyor, trueEquity=100, starting=100 → netPnl=0. Bu senaryo tam olarak
        // ADR-0020'nin adresleyeceği gap'i sergiliyor: RealizedPnl 3.5 component'i
        // hero'ya değil, UI'a "RealizedPnlAllTime" alanından gidiyor. ADR-0020 ile
        // Position.Close cash etkisi simulator içinde netleşince netPnl bu senaryoda
        // da 3.5m'e yaklaşacak.
        dto.NetPnl.Should().Be(0m);
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
        var pos = OpenPos(db, "XRPUSDT", PositionSide.Long, qty: 10m, entry: 2m);
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

        var closed = OpenPos(db, "BTCUSDT", PositionSide.Long, qty: 0.001m, entry: 30000m);
        closed.Close(exitPrice: 31000m, reason: "tp", now: T0.AddMinutes(5));

        var openPos = OpenPos(db, "ETHUSDT", PositionSide.Long, qty: 0.05m, entry: 2500m);
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
    /// ADR-0019 §19.9 — the commission total surfaced by
    /// <see cref="GetPortfolioSummaryQuery"/> must be the literal SUM of
    /// <c>OrderFills.Commission</c>, not a hardcoded 0.10% projection. Loop 23
    /// reviewer BLOCKER proved the old implementation returned a 33% overshoot
    /// whenever <c>UseBnbFeeDiscount = true</c>. This test stages 3 paper fills
    /// with the BNB-discount rate (0.075%) applied and asserts the aggregate
    /// matches the research §5 back-of-envelope: 3 × $5.10 × 0.00075 = $0.011475.
    /// </summary>
    [Fact]
    public async Task TotalCommissionPaid_SumsFromOrderFills_WithBnbDiscount()
    {
        var db = NewDb();
        SeedPaper(db, 1000m);

        // 3 paper fills — each at the ADR-0018 §18.10 floor notional of $5.10
        // (0.001 BTC × 5100). BNB-discount commission = 5.10 × 0.00075 = 0.003825.
        const decimal expectedPerFillCommission = 0.003825m;
        for (var i = 0; i < 3; i++)
        {
            var order = Order.Place(
                clientOrderId: $"cid-fee-{i}",
                Symbol.From("BTCUSDT"),
                OrderSide.Sell,
                OrderType.Market,
                TimeInForce.Ioc,
                quantity: 0.001m,
                price: null,
                stopPrice: null,
                strategyId: null,
                mode: TradingMode.Paper,
                now: T0);
            order.RegisterFill(
                exchangeTradeId: 1000 + i,
                price: 5100m,
                quantity: 0.001m,
                commission: expectedPerFillCommission,
                commissionAsset: "USDT",
                filledAt: T0);
            db.Orders.Add(order);
        }
        db.SaveChanges();

        // Handler joins OrderFills to Orders on f.OrderId == o.Id. StubDbContext
        // Ignore()s Order.Fills (nav collection), so we stamp fills into their
        // own DbSet via EF's change-tracker property API — this doesn't reach
        // through the domain setter so Order's invariant stays intact.
        long syntheticFillId = 1;
        foreach (var o in db.Orders.ToList())
        {
            var f = o.Fills.First();
            var entry = db.OrderFills.Add(f);
            entry.Property(nameof(OrderFill.OrderId)).CurrentValue = o.Id;
            entry.Property(nameof(OrderFill.Id)).CurrentValue = syntheticFillId++;
        }
        db.SaveChanges();

        var sut = new GetPortfolioSummaryQueryHandler(db, StubClock().Object);
        var result = await sut.Handle(
            new GetPortfolioSummaryQuery(TradingMode.Paper), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        // 3 × 0.003825 = 0.011475 — the ADR-0019 §19.9 expected aggregate.
        result.Value.TotalCommissionPaid.Should().Be(0.011475m);
    }
}
