using System.Text.Json;
using Ardalis.Result;
using BinanceBot.Application.Abstractions;
using BinanceBot.Application.Balances.Commands.ResetPaperBalance;
using BinanceBot.Domain.Balances;
using BinanceBot.Domain.Common;
using BinanceBot.Domain.Orders;
using BinanceBot.Domain.Positions;
using BinanceBot.Domain.RiskProfiles;
using BinanceBot.Domain.SystemEvents;
using BinanceBot.Domain.ValueObjects;
using BinanceBot.Infrastructure.Persistence;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace BinanceBot.Tests.Application.Balances;

/// <summary>
/// Loop 81 — full paper iteration reset must wipe the trade ledger that fed the
/// UI "Toplam Net K/Z" hero, otherwise <see cref="Infrastructure.Trading.EquitySnapshotProvider"/>
/// keeps summing closed Positions from the prior iteration over a freshly-anchored
/// <c>StartingBalance</c> and the UI shows phantom PnL (live trace: +$171 over an
/// actual -$9.66 ledger).
///
/// Test surface focuses on the Loop 81 deltas:
/// 1. Closed Positions from the prior iteration are deleted.
/// 2. Open Positions are force-closed AND deleted.
/// 3. Paper Orders + their cascading OrderFills are deleted.
/// 4. Trade-related SystemEvents prior to the reset are purged; non-trade events survive.
/// 5. RiskProfile counters (RealizedPnl{24h,AllTime} + PeakEquity + ConsecutiveLosses + CB)
///    are reset so the new iteration does not inherit phantom drawdown.
/// 6. The audit row <c>VirtualBalanceFullReset</c> survives the purge with diagnostic counts.
/// 7. LiveTestnet Positions/Orders are NOT touched (mode-scoped).
/// </summary>
public sealed class ResetPaperBalanceCommandHandlerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);

    private static ApplicationDbContext NewDb()
    {
        var publisher = new Mock<IPublisher>();
        publisher
            .Setup(p => p.Publish(It.IsAny<INotification>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        publisher
            .Setup(p => p.Publish(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"reset-paper-{Guid.NewGuid():N}")
            // InMemory does not implement check constraints / cascade FKs the same as
            // SQL Server, but Position/Order delete + DbSet RemoveRange works as expected.
            .Options;
        return new ApplicationDbContext(options, publisher.Object);
    }

    private static IClock StubClock(DateTimeOffset now)
    {
        var clock = new Mock<IClock>();
        clock.SetupGet(c => c.UtcNow).Returns(now);
        return clock.Object;
    }

    private static ICorrelationIdAccessor StubCorrelation()
    {
        var corr = new Mock<ICorrelationIdAccessor>();
        corr.SetupGet(c => c.CorrelationId).Returns(Guid.Empty);
        return corr.Object;
    }

    private static ResetPaperBalanceCommandHandler NewSut(
        ApplicationDbContext db, DateTimeOffset now) =>
        new(db, StubClock(now), StubCorrelation(),
            NullLogger<ResetPaperBalanceCommandHandler>.Instance);

    private static VirtualBalance SeedPaperBalance(
        ApplicationDbContext db, decimal starting, DateTimeOffset seededAt)
    {
        var vb = VirtualBalance.CreateDefault(TradingMode.Paper, starting, seededAt);
        db.VirtualBalances.Add(vb);
        db.SaveChanges();
        return vb;
    }

    private static RiskProfile SeedPaperRiskProfile(
        ApplicationDbContext db, DateTimeOffset seededAt)
    {
        var rp = RiskProfile.CreateDefault(TradingMode.Paper, seededAt);
        db.RiskProfiles.Add(rp);
        db.SaveChanges();
        return rp;
    }

    private static Position SeedClosedPosition(
        ApplicationDbContext db, string symbol, decimal entry, decimal exit,
        TradingMode mode, DateTimeOffset openedAt)
    {
        var pos = Position.Open(
            Symbol.From(symbol), TradeDirection.Long, quantity: 0.01m,
            entryPrice: entry, stopPrice: null, strategyId: null,
            mode: mode, now: openedAt);
        pos.Close(exit, "tp", openedAt.AddMinutes(5));
        db.Positions.Add(pos);
        db.SaveChanges();
        return pos;
    }

    private static Position SeedOpenPosition(
        ApplicationDbContext db, string symbol, decimal entry,
        TradingMode mode, DateTimeOffset openedAt, decimal markPrice)
    {
        var pos = Position.Open(
            Symbol.From(symbol), TradeDirection.Long, quantity: 0.01m,
            entryPrice: entry, stopPrice: null, strategyId: null,
            mode: mode, now: openedAt);
        pos.MarkToMarket(markPrice, openedAt.AddSeconds(30));
        db.Positions.Add(pos);
        db.SaveChanges();
        return pos;
    }

    private static Order SeedPaperOrder(
        ApplicationDbContext db, string clientOrderId, string symbol, DateTimeOffset placedAt)
    {
        var order = Order.Place(
            clientOrderId, Symbol.From(symbol), OrderSide.Buy, OrderType.Market,
            TimeInForce.Gtc, quantity: 0.01m, price: null, stopPrice: null,
            strategyId: null, mode: TradingMode.Paper, now: placedAt);
        // Register a fill so OrderFills cascade is exercised.
        order.RegisterFill(
            exchangeTradeId: Random.Shared.NextInt64(1, long.MaxValue),
            price: 50_000m, quantity: 0.01m,
            commission: 0.0001m, commissionAsset: "USDT",
            filledAt: placedAt.AddSeconds(1));
        db.Orders.Add(order);
        db.SaveChanges();
        return order;
    }

    private static SystemEvent SeedSystemEvent(
        ApplicationDbContext db, string eventType, DateTimeOffset occurredAt)
    {
        var evt = SystemEvent.Record(
            eventType: eventType,
            severity: SystemEventSeverity.Info,
            payloadJson: "{}",
            source: "test",
            correlationId: null,
            occurredAt: occurredAt);
        db.SystemEvents.Add(evt);
        db.SaveChanges();
        return evt;
    }

    [Fact]
    public async Task Handle_NoBalanceSeed_ReturnsNotFound()
    {
        await using var db = NewDb();
        var sut = NewSut(db, T0.AddHours(1));

        var result = await sut.Handle(new ResetPaperBalanceCommand(100m), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task Handle_DeletesAllPaperPositions_BothOpenAndClosed()
    {
        // Arrange: 3 closed + 2 open Paper positions from a prior iteration.
        await using var db = NewDb();
        SeedPaperBalance(db, 500m, T0);
        SeedClosedPosition(db, "BTCUSDT", entry: 50_000m, exit: 51_000m, TradingMode.Paper, T0);
        SeedClosedPosition(db, "ETHUSDT", entry: 3_000m, exit: 2_980m, TradingMode.Paper, T0);
        SeedClosedPosition(db, "BNBUSDT", entry: 600m, exit: 605m, TradingMode.Paper, T0);
        SeedOpenPosition(db, "XRPUSDT", entry: 0.5m, TradingMode.Paper, T0, markPrice: 0.51m);
        SeedOpenPosition(db, "ADAUSDT", entry: 0.4m, TradingMode.Paper, T0, markPrice: 0.39m);

        var resetAt = T0.AddHours(2);
        var sut = NewSut(db, resetAt);

        // Act
        var result = await sut.Handle(new ResetPaperBalanceCommand(100m), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.DeletedPositionCount.Should().Be(5);
        result.Value.ForceClosedPositionCount.Should().Be(2);

        var remaining = await db.Positions.IgnoreQueryFilters().ToListAsync();
        remaining.Should().BeEmpty("paper reset must wipe the entire Paper trade history");
    }

    [Fact]
    public async Task Handle_DoesNotDeleteLiveTestnetPositions_ModeScoped()
    {
        // Mode-scoped contract: Paper reset never reaches live trading rows.
        await using var db = NewDb();
        SeedPaperBalance(db, 100m, T0);
        SeedClosedPosition(db, "BTCUSDT", 50_000m, 51_000m, TradingMode.Paper, T0);
        SeedClosedPosition(db, "ETHUSDT", 3_000m, 3_050m, TradingMode.LiveTestnet, T0);

        var sut = NewSut(db, T0.AddHours(1));

        var result = await sut.Handle(new ResetPaperBalanceCommand(100m), CancellationToken.None);
        result.IsSuccess.Should().BeTrue();

        var liveRow = await db.Positions
            .Where(p => p.Mode == TradingMode.LiveTestnet)
            .ToListAsync();
        liveRow.Should().HaveCount(1, "LiveTestnet history must survive a Paper reset");
    }

    [Fact]
    public async Task Handle_DeletesPaperOrders_AndCascadingOrderFills()
    {
        await using var db = NewDb();
        SeedPaperBalance(db, 100m, T0);
        SeedPaperOrder(db, "cid-A", "BTCUSDT", T0);
        SeedPaperOrder(db, "cid-B", "ETHUSDT", T0.AddMinutes(1));

        var sut = NewSut(db, T0.AddHours(1));
        var result = await sut.Handle(new ResetPaperBalanceCommand(100m), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.DeletedOrderCount.Should().Be(2);

        var remainingOrders = await db.Orders.ToListAsync();
        remainingOrders.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_PurgesTradeRelatedSystemEvents_KeepsLifecycleEvents()
    {
        // EventTypes covered by the Loop 81 purge filter must vanish; lifecycle
        // events (Startup/Shutdown/WsStateChanged etc.) survive.
        await using var db = NewDb();
        SeedPaperBalance(db, 100m, T0);

        // Trade-related — must be purged.
        SeedSystemEvent(db, nameof(SystemEventType.SignalEmitted), T0.AddMinutes(5));
        SeedSystemEvent(db, nameof(SystemEventType.SignalSkipped), T0.AddMinutes(6));
        SeedSystemEvent(db, nameof(SystemEventType.OrderPlaced), T0.AddMinutes(7));
        SeedSystemEvent(db, nameof(SystemEventType.OrderFilled), T0.AddMinutes(8));
        SeedSystemEvent(db, nameof(SystemEventType.PositionOpened), T0.AddMinutes(9));
        SeedSystemEvent(db, nameof(SystemEventType.PositionClosed), T0.AddMinutes(10));
        SeedSystemEvent(db, nameof(SystemEventType.RiskAlert), T0.AddMinutes(11));

        // Lifecycle / infra — must survive.
        SeedSystemEvent(db, nameof(SystemEventType.Startup), T0.AddMinutes(1));
        SeedSystemEvent(db, nameof(SystemEventType.WsStateChanged), T0.AddMinutes(2));
        SeedSystemEvent(db, nameof(SystemEventType.WarmupCompleted), T0.AddMinutes(3));
        SeedSystemEvent(db, nameof(SystemEventType.StrategyActivated), T0.AddMinutes(4));

        var resetAt = T0.AddHours(1);
        var sut = NewSut(db, resetAt);
        var result = await sut.Handle(new ResetPaperBalanceCommand(100m), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.DeletedSystemEventCount.Should().Be(7);

        var remainingTypes = await db.SystemEvents
            .Select(e => e.EventType)
            .ToListAsync();
        remainingTypes.Should().NotContain(nameof(SystemEventType.SignalEmitted));
        remainingTypes.Should().NotContain(nameof(SystemEventType.OrderPlaced));
        remainingTypes.Should().NotContain(nameof(SystemEventType.PositionClosed));
        remainingTypes.Should().Contain(nameof(SystemEventType.Startup));
        remainingTypes.Should().Contain(nameof(SystemEventType.WsStateChanged));
        remainingTypes.Should().Contain(nameof(SystemEventType.WarmupCompleted));
        remainingTypes.Should().Contain(nameof(SystemEventType.StrategyActivated));
        // The audit row itself must survive (its OccurredAt == resetAt is NOT < resetAt).
        remainingTypes.Should().Contain("VirtualBalanceFullReset");
    }

    [Fact]
    public async Task Handle_WritesAuditEvent_WithDiagnosticCounts()
    {
        await using var db = NewDb();
        SeedPaperBalance(db, 500m, T0);
        SeedClosedPosition(db, "BTCUSDT", 50_000m, 51_000m, TradingMode.Paper, T0);
        SeedClosedPosition(db, "ETHUSDT", 3_000m, 2_980m, TradingMode.Paper, T0);
        SeedOpenPosition(db, "XRPUSDT", 0.5m, TradingMode.Paper, T0, markPrice: 0.51m);
        SeedPaperOrder(db, "cid-A", "BTCUSDT", T0);

        var sut = NewSut(db, T0.AddHours(1));
        var result = await sut.Handle(new ResetPaperBalanceCommand(200m), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        var audit = await db.SystemEvents
            .SingleAsync(e => e.EventType == "VirtualBalanceFullReset");
        audit.Source.Should().Be("ResetPaperBalanceCommand");

        using var doc = JsonDocument.Parse(audit.PayloadJson);
        var root = doc.RootElement;
        root.GetProperty("StartingBalance").GetDecimal().Should().Be(200m);
        root.GetProperty("DeletedPositions").GetInt32().Should().Be(3);
        root.GetProperty("DeletedOrders").GetInt32().Should().Be(1);
        root.GetProperty("ForceClosedPositions").GetInt32().Should().Be(1);
        root.GetProperty("ResetCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Handle_ResetsRiskProfileCounters_PaperOnly()
    {
        // Stale RiskProfile from prior iteration: positive PeakEquity, RealizedPnl,
        // ConsecutiveLosses streak. After Loop 81 reset all four must zero.
        await using var db = NewDb();
        SeedPaperBalance(db, 100m, T0);
        var rp = SeedPaperRiskProfile(db, T0);

        // Drive counters via legitimate domain calls so the fixture matches what
        // a real iteration would have persisted.
        rp.RecordTradeOutcome(realizedPnl: 5m, equityAfter: 105m, T0.AddMinutes(1));
        rp.RecordTradeOutcome(realizedPnl: -2m, equityAfter: 103m, T0.AddMinutes(2));
        rp.RecordTradeOutcome(realizedPnl: -1m, equityAfter: 102m, T0.AddMinutes(3));
        await db.SaveChangesAsync();
        rp.PeakEquity.Should().Be(105m);
        rp.ConsecutiveLosses.Should().Be(2);
        rp.RealizedPnlAllTime.Should().Be(2m);

        var sut = NewSut(db, T0.AddHours(1));
        var result = await sut.Handle(new ResetPaperBalanceCommand(100m), CancellationToken.None);
        result.IsSuccess.Should().BeTrue();

        var refreshed = await db.RiskProfiles
            .SingleAsync(r => r.Id == RiskProfile.IdFor(TradingMode.Paper));
        refreshed.PeakEquity.Should().Be(0m);
        refreshed.CurrentDrawdownPct.Should().Be(0m);
        refreshed.RealizedPnl24h.Should().Be(0m);
        refreshed.RealizedPnlAllTime.Should().Be(0m);
        refreshed.ConsecutiveLosses.Should().Be(0);
        refreshed.CircuitBreakerStatus.Should().Be(CircuitBreakerStatus.Healthy);
        refreshed.CircuitBreakerReason.Should().BeNull();
    }

    /// <summary>
    /// Defends the UI hero metric: after a reset, summing closed Position.RealizedPnl
    /// must yield 0 — exactly what <see cref="Infrastructure.Trading.EquitySnapshotProvider.GetRealizedEquityAsync"/>
    /// adds to <c>StartingBalance</c> for the "Toplam Net K/Z" calculation. Pre-Loop 81
    /// this sum was the prior iteration's $171.08 phantom delta.
    /// </summary>
    [Fact]
    public async Task Handle_RealizedPnlSumOverPaperPositions_IsZero_AfterReset()
    {
        await using var db = NewDb();
        SeedPaperBalance(db, 100m, T0);
        // Fixture mirrors the live-trace ledger in shape: mixed wins/losses, net negative.
        SeedClosedPosition(db, "BTCUSDT", 50_000m, 50_500m, TradingMode.Paper, T0); // +5 (qty 0.01)
        SeedClosedPosition(db, "ETHUSDT", 3_000m, 2_900m, TradingMode.Paper, T0);   // -1
        SeedClosedPosition(db, "BNBUSDT", 600m, 590m, TradingMode.Paper, T0);       // -0.1

        var preSum = await db.Positions
            .Where(p => p.Mode == TradingMode.Paper && p.Status == PositionStatus.Closed)
            .SumAsync(p => p.RealizedPnl);
        preSum.Should().NotBe(0m, "fixture must seed a non-zero ledger");

        var sut = NewSut(db, T0.AddHours(1));
        var result = await sut.Handle(new ResetPaperBalanceCommand(100m), CancellationToken.None);
        result.IsSuccess.Should().BeTrue();

        var postSum = await db.Positions
            .Where(p => p.Mode == TradingMode.Paper && p.Status == PositionStatus.Closed)
            .SumAsync(p => p.RealizedPnl);
        postSum.Should().Be(0m, "all closed Paper rows are deleted; UI Net K/Z reads 0");
    }

    /// <summary>
    /// Loop 111 fix #5: KeepHistory=true ile reset open pos'ları force-close eder
    /// AMA history (closed pos + orders + system events) korur. Cumulative net
    /// realized PnL takip edilebilir. Loop 110 +$0.89 net realize edilemedi
    /// çünkü reset all-delete yapıyordu.
    /// </summary>
    [Fact]
    public async Task Handle_KeepHistoryTrue_PreservesClosedPositionsAndOrders()
    {
        await using var db = NewDb();
        SeedPaperBalance(db, 500m, T0);
        SeedClosedPosition(db, "BTCUSDT", 50_000m, 51_000m, TradingMode.Paper, T0);
        SeedClosedPosition(db, "ETHUSDT", 3_000m, 2_980m, TradingMode.Paper, T0);
        SeedOpenPosition(db, "XRPUSDT", 0.5m, TradingMode.Paper, T0, markPrice: 0.51m);
        SeedPaperOrder(db, "cid-A", "BTCUSDT", T0);

        var sut = NewSut(db, T0.AddHours(1));
        var result = await sut.Handle(
            new ResetPaperBalanceCommand(StartingBalance: 100m, KeepHistory: true),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.ForceClosedPositionCount.Should().Be(1, "open pos force-close edilir");
        result.Value.DeletedPositionCount.Should().Be(0, "KeepHistory=true ⇒ silme yok");
        result.Value.DeletedOrderCount.Should().Be(0);
        result.Value.DeletedSystemEventCount.Should().Be(0);

        // Closed pos'lar + force-closed pos hepsi DB'de hala
        var allPaperPositions = await db.Positions
            .Where(p => p.Mode == TradingMode.Paper)
            .ToListAsync();
        allPaperPositions.Should().HaveCount(3, "2 closed + 1 force-closed = 3 row korundu");
        allPaperPositions.All(p => p.Status == PositionStatus.Closed).Should().BeTrue(
            "open pos force-close edildi");

        var orders = await db.Orders.Where(o => o.Mode == TradingMode.Paper).ToListAsync();
        orders.Should().HaveCount(1, "Orders KeepHistory=true ile silinmez");
    }

    /// <summary>
    /// Loop 111 fix #5: KeepHistory=true reset sonrası closed pos'ların
    /// RealizedPnl SUM'ı korunur (cumulative kar takibi). Force-closed pos'un
    /// RealizedPnl'si de buraya katılır.
    /// </summary>
    [Fact]
    public async Task Handle_KeepHistoryTrue_RealizedPnlSumIsPreserved()
    {
        await using var db = NewDb();
        SeedPaperBalance(db, 500m, T0);
        SeedClosedPosition(db, "BTCUSDT", 50_000m, 50_500m, TradingMode.Paper, T0); // +5
        SeedClosedPosition(db, "ETHUSDT", 3_000m, 2_990m, TradingMode.Paper, T0);   // -0.1

        var preSum = await db.Positions
            .Where(p => p.Mode == TradingMode.Paper && p.Status == PositionStatus.Closed)
            .SumAsync(p => p.RealizedPnl);
        preSum.Should().NotBe(0m);

        var sut = NewSut(db, T0.AddHours(1));
        var result = await sut.Handle(
            new ResetPaperBalanceCommand(StartingBalance: 100m, KeepHistory: true),
            CancellationToken.None);
        result.IsSuccess.Should().BeTrue();

        var postSum = await db.Positions
            .Where(p => p.Mode == TradingMode.Paper && p.Status == PositionStatus.Closed)
            .SumAsync(p => p.RealizedPnl);
        postSum.Should().Be(preSum,
            "Loop 111: KeepHistory=true cumulative realized PnL korunur");
    }
}
