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
/// Loop 10 take-profit fix — TakeProfitMonitorService walks open positions with a persisted
/// <see cref="Position.TakeProfit"/> and dispatches <see cref="CloseSignalPositionCommand"/>
/// when the mark price has reached the profit target. Symmetric counterpart of
/// <see cref="StopLossMonitorService"/>.
/// </summary>
public class TakeProfitMonitorServiceTests
{
    private static IServiceScopeFactory BuildScope(StubDbContext db, IMediator mediator)
    {
        var sc = new ServiceCollection();
        sc.AddSingleton<IApplicationDbContext>(db);
        sc.AddSingleton(mediator);
        return sc.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
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
        decimal? takeProfit,
        TradingMode mode = TradingMode.Paper)
    {
        var pos = Position.Open(
            Symbol.From("BTCUSDT"),
            side,
            quantity: 0.01m,
            entryPrice: 30000m,
            stopPrice: null,
            strategyId: 1,
            mode: mode,
            now: DateTimeOffset.UtcNow,
            takeProfit: takeProfit);
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

    private static Task InvokeTickAsync(TakeProfitMonitorService svc, CancellationToken ct)
    {
        var method = typeof(TakeProfitMonitorService)
            .GetMethod("TickOnceAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (Task)method.Invoke(svc, new object[] { ct })!;
    }

    [Fact]
    public async Task LongPosition_BidAtOrAboveTakeProfit_TriggersClose()
    {
        using var db = NewDb();
        var pos = SeedOpenPosition(db, PositionSide.Long, takeProfit: 30500m);
        SeedTicker(db, bid: 30600m, ask: 30610m);

        var mediator = new Mock<IMediator>(MockBehavior.Strict);
        CloseSignalPositionCommand? captured = null;
        mediator
            .Setup(m => m.Send(It.IsAny<CloseSignalPositionCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<Result<ClosedSignalPositionDto>>, CancellationToken>(
                (cmd, _) => captured = (CloseSignalPositionCommand)cmd)
            .ReturnsAsync(Result.Success(new ClosedSignalPositionDto(
                pos.Id, 50m, "take_profit", "tp-1-x")));

        var sut = new TakeProfitMonitorService(
            BuildScope(db, mediator.Object),
            NullLogger<TakeProfitMonitorService>.Instance);

        await InvokeTickAsync(sut, CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.Symbol.Should().Be("BTCUSDT");
        captured.Mode.Should().Be(TradingMode.Paper);
        captured.Reason.Should().StartWith("take_profit_triggered@");
        captured.CorrelationCidPrefix.Should().StartWith($"tp-{pos.Id}-");
    }

    [Fact]
    public async Task ShortPosition_AskAtOrBelowTakeProfit_TriggersClose()
    {
        using var db = NewDb();
        var pos = SeedOpenPosition(db, PositionSide.Short, takeProfit: 29500m);
        SeedTicker(db, bid: 29390m, ask: 29400m);

        var mediator = new Mock<IMediator>(MockBehavior.Strict);
        var sent = 0;
        mediator
            .Setup(m => m.Send(It.IsAny<CloseSignalPositionCommand>(), It.IsAny<CancellationToken>()))
            .Callback(() => sent++)
            .ReturnsAsync(Result.Success(new ClosedSignalPositionDto(
                pos.Id, 60m, "take_profit", "tp-1-x")));

        var sut = new TakeProfitMonitorService(
            BuildScope(db, mediator.Object),
            NullLogger<TakeProfitMonitorService>.Instance);

        await InvokeTickAsync(sut, CancellationToken.None);

        sent.Should().Be(1, "short TP fires when ask falls to/below the target");
    }

    [Fact]
    public async Task PositionWithNullTakeProfit_IsSkipped()
    {
        using var db = NewDb();
        SeedOpenPosition(db, PositionSide.Long, takeProfit: null);
        SeedTicker(db, bid: 99999m, ask: 100000m);

        var mediator = new Mock<IMediator>(MockBehavior.Strict);

        var sut = new TakeProfitMonitorService(
            BuildScope(db, mediator.Object),
            NullLogger<TakeProfitMonitorService>.Instance);

        await InvokeTickAsync(sut, CancellationToken.None);

        mediator.Verify(m => m.Send(It.IsAny<CloseSignalPositionCommand>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
