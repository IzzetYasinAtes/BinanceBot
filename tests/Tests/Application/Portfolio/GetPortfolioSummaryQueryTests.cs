using BinanceBot.Application.Abstractions;
using BinanceBot.Application.Portfolio.Queries.GetPortfolioSummary;
using BinanceBot.Domain.Balances;
using BinanceBot.Domain.Common;
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
        dto.NetPnl.Should().Be(5m);                         // 0 realized + 5 unrealized
        dto.NetPnlPct.Should().Be(0.05m);
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
        dto.NetPnl.Should().Be(3.5m);
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
}
