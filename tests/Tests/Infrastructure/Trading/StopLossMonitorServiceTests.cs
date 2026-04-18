using Ardalis.Result;
using BinanceBot.Application.Abstractions;
using BinanceBot.Application.Positions.Commands.ClosePosition;
using BinanceBot.Domain.Common;
using BinanceBot.Domain.MarketData;
using BinanceBot.Domain.Positions;
using BinanceBot.Domain.ValueObjects;
using BinanceBot.Infrastructure.Trading;
using BinanceBot.Tests.Infrastructure.Strategies;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Reflection;

namespace BinanceBot.Tests.Infrastructure.Trading;

/// <summary>
/// ADR-0012 §12.3 — StopLossMonitorService walks open positions with a persisted
/// <see cref="Position.StopPrice"/> and dispatches <see cref="CloseSignalPositionCommand"/>
/// when the mark price has crossed the stop. Triggers exclude LiveMainnet (defensive guard).
/// </summary>
public class StopLossMonitorServiceTests
{
    private static IServiceScopeFactory BuildScope(StubDbContext db, IMediator mediator, IClock? clock = null)
    {
        var sc = new ServiceCollection();
        sc.AddSingleton<IApplicationDbContext>(db);
        sc.AddSingleton(mediator);
        // ADR-0014 §14.5: monitor now resolves IClock to drive the time-stop branch.
        sc.AddSingleton(clock ?? new FixedClock(DateTimeOffset.UtcNow));
        return sc.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset now) { UtcNow = now; }
        public DateTimeOffset UtcNow { get; }
        public long BinanceServerTimeMs => UtcNow.ToUnixTimeMilliseconds();
        public long DriftMs => 0;
    }

    private static StubDbContext NewDb()
    {
        var opts = new DbContextOptionsBuilder<StubDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new StubDbContext(opts);
    }

    private static Position SeedOpenPosition(
        StubDbContext db,
        PositionSide side,
        decimal stop,
        TradingMode mode = TradingMode.Paper)
    {
        var pos = Position.Open(
            Symbol.From("BTCUSDT"),
            side,
            quantity: 0.01m,
            entryPrice: 30000m,
            stopPrice: stop,
            strategyId: 1,
            mode: mode,
            now: DateTimeOffset.UtcNow);
        db.Positions.Add(pos);
        db.SaveChanges();
        return pos;
    }

    private static void SeedTicker(StubDbContext db, decimal bid, decimal ask)
    {
        db.BookTickers.Add(BookTicker.Create(
            Symbol.From("BTCUSDT"),
            bidPrice: bid, bidQty: 1m,
            askPrice: ask, askQty: 1m,
            updateId: 1, updatedAt: DateTimeOffset.UtcNow));
        db.SaveChanges();
    }

    /// <summary>
    /// Invoke the private <c>TickOnceAsync</c> via reflection so the test does not depend on
    /// the BackgroundService loop or its 30s delay.
    /// </summary>
    private static Task InvokeTickAsync(StopLossMonitorService svc, CancellationToken ct)
    {
        var method = typeof(StopLossMonitorService)
            .GetMethod("TickOnceAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (Task)method.Invoke(svc, new object[] { ct })!;
    }

    [Fact]
    public async Task LongPosition_BidBelowStop_TriggersClose()
    {
        using var db = NewDb();
        var pos = SeedOpenPosition(db, PositionSide.Long, stop: 29500m);
        SeedTicker(db, bid: 29400m, ask: 29410m);

        var mediator = new Mock<IMediator>(MockBehavior.Strict);
        CloseSignalPositionCommand? captured = null;
        mediator
            .Setup(m => m.Send(It.IsAny<CloseSignalPositionCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<Result<ClosedSignalPositionDto>>, CancellationToken>(
                (cmd, _) => captured = (CloseSignalPositionCommand)cmd)
            .ReturnsAsync(Result.Success(new ClosedSignalPositionDto(
                pos.Id, 0m, "stop_loss", "stop-1-x")));

        var sut = new StopLossMonitorService(
            BuildScope(db, mediator.Object),
            NullLogger<StopLossMonitorService>.Instance);

        await InvokeTickAsync(sut, CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.Symbol.Should().Be("BTCUSDT");
        captured.Mode.Should().Be(TradingMode.Paper);
        captured.Reason.Should().StartWith("stop_loss_triggered@");
        captured.CorrelationCidPrefix.Should().StartWith($"stop-{pos.Id}-");
    }

    [Fact]
    public async Task LongPosition_BidAboveStop_DoesNotTrigger()
    {
        using var db = NewDb();
        SeedOpenPosition(db, PositionSide.Long, stop: 29500m);
        SeedTicker(db, bid: 29800m, ask: 29810m);

        var mediator = new Mock<IMediator>(MockBehavior.Strict);

        var sut = new StopLossMonitorService(
            BuildScope(db, mediator.Object),
            NullLogger<StopLossMonitorService>.Instance);

        await InvokeTickAsync(sut, CancellationToken.None);

        mediator.Verify(m => m.Send(It.IsAny<CloseSignalPositionCommand>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ShortPosition_AskAboveStop_TriggersClose()
    {
        using var db = NewDb();
        var pos = SeedOpenPosition(db, PositionSide.Short, stop: 30500m);
        SeedTicker(db, bid: 30590m, ask: 30600m);

        var mediator = new Mock<IMediator>(MockBehavior.Strict);
        var sent = 0;
        mediator
            .Setup(m => m.Send(It.IsAny<CloseSignalPositionCommand>(), It.IsAny<CancellationToken>()))
            .Callback(() => sent++)
            .ReturnsAsync(Result.Success(new ClosedSignalPositionDto(
                pos.Id, 0m, "stop_loss", "stop-1-x")));

        var sut = new StopLossMonitorService(
            BuildScope(db, mediator.Object),
            NullLogger<StopLossMonitorService>.Instance);

        await InvokeTickAsync(sut, CancellationToken.None);

        sent.Should().Be(1);
    }

    [Fact]
    public async Task PositionWithNullStop_IsSkipped()
    {
        using var db = NewDb();
        var pos = Position.Open(
            Symbol.From("BTCUSDT"),
            PositionSide.Long,
            quantity: 0.01m,
            entryPrice: 30000m,
            stopPrice: null,
            strategyId: 1,
            mode: TradingMode.Paper,
            now: DateTimeOffset.UtcNow);
        db.Positions.Add(pos);
        db.SaveChanges();
        SeedTicker(db, bid: 100m, ask: 110m);

        var mediator = new Mock<IMediator>(MockBehavior.Strict);

        var sut = new StopLossMonitorService(
            BuildScope(db, mediator.Object),
            NullLogger<StopLossMonitorService>.Instance);

        await InvokeTickAsync(sut, CancellationToken.None);

        mediator.Verify(m => m.Send(It.IsAny<CloseSignalPositionCommand>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Loop 7 bug #18 lock-in: a paused parent <see cref="Domain.Strategies.Strategy"/>
    /// must NOT prevent its open positions from being stop-loss-protected. Pausing
    /// only halts new signal evaluation, never risk-management exits.
    /// </summary>
    [Fact]
    public async Task PausedStrategy_PositionStillTriggersStopLoss()
    {
        using var db = NewDb();
        // Note: StubDbContext ignores Strategy entity; the monitor query never joins it.
        // The contract under test is "tick scans positions independent of strategy state".
        var pos = SeedOpenPosition(db, PositionSide.Long, stop: 76200m);
        // BTC scenario from Loop 6: entry 76618, stop 76200 (~-0.55%), mark fell to 75876.
        SeedTicker(db, bid: 75876m, ask: 75880m);

        var mediator = new Mock<IMediator>(MockBehavior.Strict);
        var sent = 0;
        mediator
            .Setup(m => m.Send(It.IsAny<CloseSignalPositionCommand>(), It.IsAny<CancellationToken>()))
            .Callback(() => sent++)
            .ReturnsAsync(Result.Success(new ClosedSignalPositionDto(
                pos.Id, 0m, "stop_loss", "stop-1-x")));

        var sut = new StopLossMonitorService(
            BuildScope(db, mediator.Object),
            NullLogger<StopLossMonitorService>.Instance);

        await InvokeTickAsync(sut, CancellationToken.None);

        sent.Should().Be(1, "stop-loss must fire regardless of parent strategy status");
    }

    [Fact]
    public async Task LiveMainnetPosition_IsSkippedDefensively()
    {
        using var db = NewDb();
        SeedOpenPosition(db, PositionSide.Long, stop: 29500m, mode: TradingMode.LiveMainnet);
        SeedTicker(db, bid: 29400m, ask: 29410m);

        var mediator = new Mock<IMediator>(MockBehavior.Strict);

        var sut = new StopLossMonitorService(
            BuildScope(db, mediator.Object),
            NullLogger<StopLossMonitorService>.Instance);

        await InvokeTickAsync(sut, CancellationToken.None);

        mediator.Verify(m => m.Send(It.IsAny<CloseSignalPositionCommand>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
