using Ardalis.Result;
using BinanceBot.Application.Abstractions;
using BinanceBot.Application.Abstractions.Trading;
using BinanceBot.Application.Orders.Commands.PlaceOrder;
using BinanceBot.Application.Positions.Commands.ClosePosition;
using BinanceBot.Domain.Common;
using BinanceBot.Domain.Instruments;
using BinanceBot.Domain.MarketData;
using BinanceBot.Domain.Positions;
using BinanceBot.Domain.RiskProfiles;
using BinanceBot.Domain.Strategies;
using BinanceBot.Domain.Strategies.Events;
using BinanceBot.Domain.ValueObjects;
using BinanceBot.Infrastructure.Strategies;
using BinanceBot.Infrastructure.Trading.Paper;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace BinanceBot.Tests.Infrastructure.Strategies;

/// <summary>
/// Behaviour tests for the strategy fan-out — covers BUG-B (no hardcoded qty),
/// CB skip, equity-zero skip, and the Exit branch routing to
/// <see cref="CloseSignalPositionCommand"/>.
/// </summary>
public class StrategySignalToOrderHandlerTests
{
    private const decimal SizedQty = 0.05m;

    private static (IServiceScopeFactory ScopeFactory,
                    Mock<IMediator> Mediator,
                    Mock<IPositionSizingService> Sizing,
                    Mock<IEquitySnapshotProvider> Equity,
                    StubDbContext Db)
        BuildHarness(
            decimal equity = 100m,
            CircuitBreakerStatus cbState = CircuitBreakerStatus.Healthy,
            decimal sizedQuantity = SizedQty,
            string? sizingSkipReason = null,
            bool seedInstrument = true,
            bool seedTicker = true,
            bool seedRiskProfile = true,
            bool seedOpenPosition = false,
            PositionSide openPositionSide = PositionSide.Long,
            int? maxOpenPositionsOverride = null,
            int seededOpenPositionsPerMode = 0)
    {
        var sc = new ServiceCollection();

        var dbName = Guid.NewGuid().ToString();
        sc.AddDbContext<StubDbContext>(o => o.UseInMemoryDatabase(dbName));
        sc.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<StubDbContext>());

        var mediator = new Mock<IMediator>(MockBehavior.Strict);
        sc.AddSingleton(mediator.Object);

        var sizing = new Mock<IPositionSizingService>();
        sizing.Setup(s => s.Calculate(It.IsAny<PositionSizingInput>()))
            .Returns(new PositionSizingResult(
                Quantity: sizingSkipReason is null ? sizedQuantity : 0m,
                NotionalEstimate: 0m,
                SkipReason: sizingSkipReason));
        sc.AddSingleton(sizing.Object);

        var equityProvider = new Mock<IEquitySnapshotProvider>();
        equityProvider.Setup(e => e.GetEquityAsync(It.IsAny<TradingMode>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(equity);
        sc.AddSingleton(equityProvider.Object);

        sc.AddSingleton<IOptions<PaperFillOptions>>(
            Options.Create(new PaperFillOptions { FixedSlippagePct = 0.0005m }));

        var sp = sc.BuildServiceProvider();
        var db = sp.GetRequiredService<StubDbContext>();

        if (seedInstrument)
        {
            db.Instruments.Add(Instrument.Create(
                Symbol.From("BTCUSDT"), "BTC", "USDT",
                InstrumentStatus.Trading,
                tickSize: 0.01m, stepSize: 0.00001m,
                minNotional: 5m, minQty: 0.00001m, maxQty: 9000m,
                syncedAt: DateTimeOffset.UtcNow));
        }
        if (seedTicker)
        {
            db.BookTickers.Add(BookTicker.Create(
                Symbol.From("BTCUSDT"),
                bidPrice: 29990m, bidQty: 1m,
                askPrice: 30000m, askQty: 1m,
                updateId: 1, updatedAt: DateTimeOffset.UtcNow));
        }
        if (seedRiskProfile)
        {
            foreach (var mode in new[] { TradingMode.Paper, TradingMode.LiveTestnet, TradingMode.LiveMainnet })
            {
                var profile = RiskProfile.CreateDefault(mode, DateTimeOffset.UtcNow);
                if (maxOpenPositionsOverride is int max)
                {
                    profile.UpdateLimits(0.02m, 0.40m, 0.20m, 0.40m, 10, max, DateTimeOffset.UtcNow);
                }
                if (cbState == CircuitBreakerStatus.Tripped)
                {
                    profile.TripCircuitBreaker("test", 0.5m, DateTimeOffset.UtcNow);
                }
                db.RiskProfiles.Add(profile);
            }
        }
        if (seedOpenPosition)
        {
            var pos = Position.Open(
                Symbol.From("BTCUSDT"),
                openPositionSide,
                quantity: 0.01m,
                entryPrice: 29500m,
                stopPrice: null,
                strategyId: 1,
                mode: TradingMode.Paper,
                now: DateTimeOffset.UtcNow);
            db.Positions.Add(pos);
        }
        if (seededOpenPositionsPerMode > 0)
        {
            foreach (var mode in new[] { TradingMode.Paper, TradingMode.LiveTestnet, TradingMode.LiveMainnet })
            {
                for (var i = 0; i < seededOpenPositionsPerMode; i++)
                {
                    db.Positions.Add(Position.Open(
                        Symbol.From("BTCUSDT"),
                        PositionSide.Long,
                        quantity: 0.01m,
                        entryPrice: 29500m,
                        stopPrice: null,
                        strategyId: 1,
                        mode: mode,
                        now: DateTimeOffset.UtcNow));
                }
            }
        }
        db.SaveChanges();

        return (sp.GetRequiredService<IServiceScopeFactory>(), mediator, sizing, equityProvider, db);
    }

    [Fact]
    public async Task Long_FansOutThreeOrders_QtyComesFromSizingService_NotHardcoded()
    {
        var (scopeFactory, mediator, _, _, _) = BuildHarness();

        var captured = new List<PlaceOrderCommand>();
        mediator.Setup(m => m.Send(It.IsAny<PlaceOrderCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<Result<PlacedOrderDto>>, CancellationToken>((cmd, _) => captured.Add((PlaceOrderCommand)cmd))
            .ReturnsAsync(Result.Success(new PlacedOrderDto(
                "x", "BTCUSDT", "Filled", SizedQty, null, TradingMode.Paper)));

        var sut = new StrategySignalToOrderHandler(
            scopeFactory, NullLogger<StrategySignalToOrderHandler>.Instance);

        var evt = new StrategySignalEmittedEvent(
            StrategyId: 1, Symbol: "BTCUSDT",
            Direction: StrategySignalDirection.Long,
            BarOpenTime: DateTimeOffset.UtcNow,
            SuggestedStopPrice: 29500m);

        await sut.Handle(evt, CancellationToken.None);

        captured.Should().HaveCount(3);
        captured.Should().AllSatisfy(c => c.Quantity.Should().Be(SizedQty));
        captured.Select(c => c.Mode).Should().BeEquivalentTo(
            new[] { TradingMode.Paper, TradingMode.LiveTestnet, TradingMode.LiveMainnet });
        captured.Should().AllSatisfy(c => c.ClientOrderId.Should().StartWith("sig-1-"));
    }

    [Fact]
    public async Task Long_SkipsModeWhenCircuitBreakerTripped()
    {
        var (scopeFactory, mediator, _, _, _) =
            BuildHarness(cbState: CircuitBreakerStatus.Tripped);

        mediator.Setup(m => m.Send(It.IsAny<PlaceOrderCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new PlacedOrderDto("x", "BTCUSDT", "Filled", SizedQty, null, TradingMode.Paper)));

        var sut = new StrategySignalToOrderHandler(
            scopeFactory, NullLogger<StrategySignalToOrderHandler>.Instance);

        await sut.Handle(new StrategySignalEmittedEvent(
            1, "BTCUSDT", StrategySignalDirection.Long, DateTimeOffset.UtcNow, 29500m),
            CancellationToken.None);

        mediator.Verify(m => m.Send(It.IsAny<PlaceOrderCommand>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Long_SkipsModeWhenEquityZero()
    {
        var (scopeFactory, mediator, _, _, _) = BuildHarness(equity: 0m);

        mediator.Setup(m => m.Send(It.IsAny<PlaceOrderCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new PlacedOrderDto("x", "BTCUSDT", "Filled", SizedQty, null, TradingMode.Paper)));

        var sut = new StrategySignalToOrderHandler(
            scopeFactory, NullLogger<StrategySignalToOrderHandler>.Instance);

        await sut.Handle(new StrategySignalEmittedEvent(
            1, "BTCUSDT", StrategySignalDirection.Long, DateTimeOffset.UtcNow, 29500m),
            CancellationToken.None);

        mediator.Verify(m => m.Send(It.IsAny<PlaceOrderCommand>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Long_SkipsModeWhenSizingReturnsZero()
    {
        var (scopeFactory, mediator, _, _, _) =
            BuildHarness(sizingSkipReason: "min_notional_floor", sizedQuantity: 0m);

        mediator.Setup(m => m.Send(It.IsAny<PlaceOrderCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new PlacedOrderDto("x", "BTCUSDT", "Filled", 0m, null, TradingMode.Paper)));

        var sut = new StrategySignalToOrderHandler(
            scopeFactory, NullLogger<StrategySignalToOrderHandler>.Instance);

        await sut.Handle(new StrategySignalEmittedEvent(
            1, "BTCUSDT", StrategySignalDirection.Long, DateTimeOffset.UtcNow, 29500m),
            CancellationToken.None);

        mediator.Verify(m => m.Send(It.IsAny<PlaceOrderCommand>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Exit_FansOutThreeCloseCommands_NoPlaceOrderCommandSentDirectly()
    {
        var (scopeFactory, mediator, _, _, _) = BuildHarness();

        var closes = new List<CloseSignalPositionCommand>();
        mediator.Setup(m => m.Send(It.IsAny<CloseSignalPositionCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<Result<ClosedSignalPositionDto>>, CancellationToken>(
                (cmd, _) => closes.Add((CloseSignalPositionCommand)cmd))
            .ReturnsAsync(Result<ClosedSignalPositionDto>.NotFound("no open position"));

        var sut = new StrategySignalToOrderHandler(
            scopeFactory, NullLogger<StrategySignalToOrderHandler>.Instance);

        await sut.Handle(new StrategySignalEmittedEvent(
            1, "BTCUSDT", StrategySignalDirection.Exit, DateTimeOffset.UtcNow),
            CancellationToken.None);

        closes.Should().HaveCount(3);
        closes.Select(c => c.Mode).Should().BeEquivalentTo(
            new[] { TradingMode.Paper, TradingMode.LiveTestnet, TradingMode.LiveMainnet });
        closes.Should().AllSatisfy(c => c.Reason.Should().Be("exit_signal"));
        closes.Should().AllSatisfy(c => c.CorrelationCidPrefix.Should().StartWith("sig-1-"));

        mediator.Verify(m => m.Send(It.IsAny<PlaceOrderCommand>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Regression for ADR-0011 review round 2 blocker: with <c>StrategyId = long.MaxValue</c>
    /// and a far-future bar timestamp, the fan-out CID (<c>sig-{StrategyId}-{barUnix}-{modeSuffix}</c>)
    /// must stay within the 64-char DB column. Exit-branch correlation prefix must also stay
    /// within the validator limit (54). Worst case before fix: entry CID = 37 chars vs old
    /// <c>HasMaxLength(36)</c> ⇒ DbUpdateException. Worst case for prefix: 34 chars vs old
    /// validator <c>MaximumLength(28)</c> ⇒ exit never placed.
    /// </summary>
    [Fact]
    public async Task LongMaxStrategyId_WithFarFutureBar_FanOutCidStaysWithin64Chars()
    {
        var (scopeFactory, mediator, _, _, _) = BuildHarness();

        var captured = new List<PlaceOrderCommand>();
        mediator.Setup(m => m.Send(It.IsAny<PlaceOrderCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<Result<PlacedOrderDto>>, CancellationToken>((cmd, _) => captured.Add((PlaceOrderCommand)cmd))
            .ReturnsAsync(Result.Success(new PlacedOrderDto(
                "x", "BTCUSDT", "Filled", SizedQty, null, TradingMode.Paper)));

        var sut = new StrategySignalToOrderHandler(
            scopeFactory, NullLogger<StrategySignalToOrderHandler>.Instance);

        // DateTimeOffset.MaxValue → unix seconds = 253402300799 (12 digits) — within long range.
        var farFuture = DateTimeOffset.MaxValue;
        var evt = new StrategySignalEmittedEvent(
            StrategyId: long.MaxValue,
            Symbol: "BTCUSDT",
            Direction: StrategySignalDirection.Long,
            BarOpenTime: farFuture,
            SuggestedStopPrice: 29500m);

        await sut.Handle(evt, CancellationToken.None);

        captured.Should().HaveCount(3);
        // 64 = OrderConfiguration.HasMaxLength after the fix; everything must fit comfortably.
        captured.Should().AllSatisfy(c => c.ClientOrderId.Length.Should().BeLessThanOrEqualTo(64));
        captured.Should().AllSatisfy(c => c.ClientOrderId.Should().StartWith($"sig-{long.MaxValue}-"));

        // Exit branch shares the same cidPrefix builder; validate it satisfies the
        // CloseSignalPositionCommand validator (MaximumLength(54) after the fix).
        var barUnix = farFuture.ToUnixTimeSeconds();
        var cidPrefix = $"sig-{long.MaxValue}-{barUnix}";
        cidPrefix.Length.Should().BeLessThanOrEqualTo(54);
        // Exit CID = `{prefix}-x-{suffix}` (suffix max 2 chars) — must also fit DB column.
        (cidPrefix.Length + "-x-lm".Length).Should().BeLessThanOrEqualTo(64);
    }

    /// <summary>
    /// Loop 14 (research-paper-live-and-sizing.md §C5/§6): when the per-mode
    /// MaxOpenPositions ceiling is already reached, the fan-out must skip the
    /// entry without sending a PlaceOrderCommand. We seed two open positions
    /// per mode and set MaxOpenPositions=2, so every mode hits the throttle.
    /// </summary>
    [Fact]
    public async Task Long_SkipsAllModes_WhenMaxOpenPositionsAlreadyReached()
    {
        var (scopeFactory, mediator, _, _, _) = BuildHarness(
            maxOpenPositionsOverride: 2,
            seededOpenPositionsPerMode: 2);

        mediator.Setup(m => m.Send(It.IsAny<PlaceOrderCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new PlacedOrderDto("x", "BTCUSDT", "Filled", SizedQty, null, TradingMode.Paper)));

        var sut = new StrategySignalToOrderHandler(
            scopeFactory, NullLogger<StrategySignalToOrderHandler>.Instance);

        await sut.Handle(new StrategySignalEmittedEvent(
            1, "BTCUSDT", StrategySignalDirection.Long, DateTimeOffset.UtcNow, 29500m),
            CancellationToken.None);

        mediator.Verify(m => m.Send(It.IsAny<PlaceOrderCommand>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Long_DoesNotInvokeOrderForUnknownInstrument()
    {
        var (scopeFactory, mediator, _, _, _) = BuildHarness(seedInstrument: false);

        mediator.Setup(m => m.Send(It.IsAny<PlaceOrderCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new PlacedOrderDto("x", "BTCUSDT", "Filled", SizedQty, null, TradingMode.Paper)));

        var sut = new StrategySignalToOrderHandler(
            scopeFactory, NullLogger<StrategySignalToOrderHandler>.Instance);

        await sut.Handle(new StrategySignalEmittedEvent(
            1, "BTCUSDT", StrategySignalDirection.Long, DateTimeOffset.UtcNow, 29500m),
            CancellationToken.None);

        mediator.Verify(m => m.Send(It.IsAny<PlaceOrderCommand>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
