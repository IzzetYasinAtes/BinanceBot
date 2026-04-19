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
using BinanceBot.Domain.SystemEvents.Events;
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
        // Loop 19 — sizing now reads GetSizingEquityAsync (realized-only equity).
        // Tests stub both so future swaps stay covered.
        equityProvider.Setup(e => e.GetSizingEquityAsync(It.IsAny<TradingMode>(), It.IsAny<CancellationToken>()))
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
    ///
    /// ADR-0017 §17.8: the seeded open positions share (strategyId=1, BTCUSDT)
    /// with the incoming signal, so the per-(strategy, symbol) duplicate guard
    /// is the first gate the handler trips — a <see cref="StrategySignalSkippedEvent"/>
    /// publish is expected before the open-count branch would have run.
    /// </summary>
    [Fact]
    public async Task Long_SkipsAllModes_WhenMaxOpenPositionsAlreadyReached()
    {
        var (scopeFactory, mediator, _, _, _) = BuildHarness(
            maxOpenPositionsOverride: 2,
            seededOpenPositionsPerMode: 2);

        mediator.Setup(m => m.Send(It.IsAny<PlaceOrderCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new PlacedOrderDto("x", "BTCUSDT", "Filled", SizedQty, null, TradingMode.Paper)));
        mediator.Setup(m => m.Publish(It.IsAny<StrategySignalSkippedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

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

    // --- ADR-0017 §17.8 duplicate signal protection ---------------------------------------

    /// <summary>
    /// ADR-0017 §17.8: a strategy that already owns an open Position on the same
    /// symbol must not double-enter when a second signal arrives. The duplicate
    /// pre-check skips every mode whose DB state satisfies
    /// (strategyId, symbol, status=Open, mode) and publishes a
    /// <see cref="StrategySignalSkippedEvent"/> with reason <c>duplicate_open_position</c>.
    /// </summary>
    [Fact]
    public async Task Long_SkipsAndPublishesDuplicateEvent_WhenSameStrategySymbolOpen()
    {
        var (scopeFactory, mediator, _, _, db) = BuildHarness();

        // Seed: mode=Paper already has an open BTCUSDT position for strategyId=1.
        db.Positions.Add(Position.Open(
            Symbol.From("BTCUSDT"), PositionSide.Long,
            quantity: 0.01m, entryPrice: 29500m, stopPrice: null,
            strategyId: 1, mode: TradingMode.Paper, now: DateTimeOffset.UtcNow));
        db.SaveChanges();

        var placed = new List<PlaceOrderCommand>();
        mediator.Setup(m => m.Send(It.IsAny<PlaceOrderCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<Result<PlacedOrderDto>>, CancellationToken>((cmd, _) => placed.Add((PlaceOrderCommand)cmd))
            .ReturnsAsync(Result.Success(new PlacedOrderDto("x", "BTCUSDT", "Filled", SizedQty, null, TradingMode.Paper)));

        var skipped = new List<StrategySignalSkippedEvent>();
        mediator.Setup(m => m.Publish(It.IsAny<StrategySignalSkippedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<object, CancellationToken>((n, _) => skipped.Add((StrategySignalSkippedEvent)n))
            .Returns(Task.CompletedTask);

        var sut = new StrategySignalToOrderHandler(
            scopeFactory, NullLogger<StrategySignalToOrderHandler>.Instance);

        await sut.Handle(new StrategySignalEmittedEvent(
            StrategyId: 1, Symbol: "BTCUSDT",
            Direction: StrategySignalDirection.Long,
            BarOpenTime: DateTimeOffset.UtcNow,
            SuggestedStopPrice: 29500m),
            CancellationToken.None);

        // Paper is skipped with a duplicate event; LiveTestnet and LiveMainnet
        // are still eligible (they have no open position) so two orders fan out.
        placed.Select(c => c.Mode).Should().BeEquivalentTo(
            new[] { TradingMode.LiveTestnet, TradingMode.LiveMainnet });
        skipped.Should().ContainSingle()
            .Which.Reason.Should().Be("duplicate_open_position");
        skipped[0].StrategyId.Should().Be(1);
        skipped[0].Symbol.Should().Be("BTCUSDT");
    }

    /// <summary>
    /// ADR-0017 §17.8: duplicate protection is scoped by (strategyId, symbol, mode).
    /// When the only open position lives in a different mode the signal must still
    /// fan out to that mode — mode isolation from ADR-0008 is preserved.
    /// </summary>
    [Fact]
    public async Task Long_DoesNotSkip_WhenOpenPositionIsOnADifferentMode()
    {
        var (scopeFactory, mediator, _, _, db) = BuildHarness();

        // Open on LiveTestnet only; Paper + LiveMainnet stay eligible.
        db.Positions.Add(Position.Open(
            Symbol.From("BTCUSDT"), PositionSide.Long,
            0.01m, 29500m, null, strategyId: 1,
            mode: TradingMode.LiveTestnet, now: DateTimeOffset.UtcNow));
        db.SaveChanges();

        var placed = new List<PlaceOrderCommand>();
        mediator.Setup(m => m.Send(It.IsAny<PlaceOrderCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<Result<PlacedOrderDto>>, CancellationToken>((cmd, _) => placed.Add((PlaceOrderCommand)cmd))
            .ReturnsAsync(Result.Success(new PlacedOrderDto("x", "BTCUSDT", "Filled", SizedQty, null, TradingMode.Paper)));
        mediator.Setup(m => m.Publish(It.IsAny<StrategySignalSkippedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = new StrategySignalToOrderHandler(
            scopeFactory, NullLogger<StrategySignalToOrderHandler>.Instance);

        await sut.Handle(new StrategySignalEmittedEvent(
            1, "BTCUSDT", StrategySignalDirection.Long, DateTimeOffset.UtcNow, 29500m),
            CancellationToken.None);

        placed.Select(c => c.Mode).Should().BeEquivalentTo(
            new[] { TradingMode.Paper, TradingMode.LiveMainnet });
    }

    /// <summary>
    /// ADR-0017 §17.8: duplicate protection is scoped by (strategyId, symbol, mode).
    /// When the only open position is on a different symbol, the new signal must
    /// fan out normally on all three modes.
    /// </summary>
    [Fact]
    public async Task Long_DoesNotSkip_WhenOpenPositionIsOnADifferentSymbol()
    {
        var (scopeFactory, mediator, _, _, db) = BuildHarness();

        // Seed a BNBUSDT instrument + open position so the BTCUSDT signal can
        // still reach the order pipeline without tripping the duplicate gate.
        db.Instruments.Add(Instrument.Create(
            Symbol.From("BNBUSDT"), "BNB", "USDT",
            InstrumentStatus.Trading,
            tickSize: 0.01m, stepSize: 0.001m,
            minNotional: 5m, minQty: 0.001m, maxQty: 9000m,
            syncedAt: DateTimeOffset.UtcNow));
        db.Positions.Add(Position.Open(
            Symbol.From("BNBUSDT"), PositionSide.Long,
            0.01m, 600m, null, strategyId: 1,
            mode: TradingMode.Paper, now: DateTimeOffset.UtcNow));
        db.SaveChanges();

        var placed = new List<PlaceOrderCommand>();
        mediator.Setup(m => m.Send(It.IsAny<PlaceOrderCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<Result<PlacedOrderDto>>, CancellationToken>((cmd, _) => placed.Add((PlaceOrderCommand)cmd))
            .ReturnsAsync(Result.Success(new PlacedOrderDto("x", "BTCUSDT", "Filled", SizedQty, null, TradingMode.Paper)));

        var sut = new StrategySignalToOrderHandler(
            scopeFactory, NullLogger<StrategySignalToOrderHandler>.Instance);

        await sut.Handle(new StrategySignalEmittedEvent(
            1, "BTCUSDT", StrategySignalDirection.Long, DateTimeOffset.UtcNow, 29500m),
            CancellationToken.None);

        placed.Should().HaveCount(3);
        placed.Select(c => c.Symbol).Should().AllBeEquivalentTo("BTCUSDT");
    }

    // --- ADR-0017 §17.9 target-notional sizing wiring -------------------------------------

    /// <summary>Lift each seeded RiskProfile.MaxPositionSizePct to the ADR-0017
    /// production default (0.40). The default factory returns 0.10 which is no
    /// longer representative of the live config — tests that verify sizing
    /// overrides use the post-Loop-14 cap.
    /// </summary>
    private static void RaiseHardCapToLoop14Default(StubDbContext db)
    {
        foreach (var profile in db.RiskProfiles)
        {
            profile.UpdateLimits(
                riskPerTradePct: 0.02m,
                maxPositionSizePct: 0.40m,
                maxDrawdown24hPct: 0.20m,
                maxDrawdownAllTimePct: 0.40m,
                maxConsecutiveLosses: 10,
                maxOpenPositions: 2,
                now: DateTimeOffset.UtcNow);
        }
        db.SaveChanges();
    }

    /// <summary>
    /// ADR-0017 §17.9: the handler must hand the sizing service a
    /// <c>MaxPositionPct</c> that maps the target notional to exactly 20% of
    /// equity (with a $20 floor when equity &lt; 100). For equity=$100 that
    /// collapses to MaxPositionPct=0.20 and MinNotional=$20 — the cap branch of
    /// <c>PositionSizingService</c> then sizes to the target, not the legacy
    /// $40 hard-cap.
    /// </summary>
    [Fact]
    public async Task Long_PassesTargetNotionalAsMaxPositionPctAndMinNotional_ToSizingService()
    {
        var (scopeFactory, mediator, sizing, _, db) = BuildHarness(equity: 100m);
        RaiseHardCapToLoop14Default(db);

        PositionSizingInput? captured = null;
        sizing.Setup(s => s.Calculate(It.IsAny<PositionSizingInput>()))
            .Callback<PositionSizingInput>(i => captured = i)
            .Returns(new PositionSizingResult(Quantity: SizedQty, NotionalEstimate: 20m, SkipReason: null));

        mediator.Setup(m => m.Send(It.IsAny<PlaceOrderCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new PlacedOrderDto("x", "BTCUSDT", "Filled", SizedQty, null, TradingMode.Paper)));

        var sut = new StrategySignalToOrderHandler(
            scopeFactory, NullLogger<StrategySignalToOrderHandler>.Instance);

        await sut.Handle(new StrategySignalEmittedEvent(
            1, "BTCUSDT", StrategySignalDirection.Long, DateTimeOffset.UtcNow, 29500m),
            CancellationToken.None);

        captured.Should().NotBeNull();
        // equity=100 -> target=max(100*0.20, 20)=20, cap=100*0.40=40, notional=min(20,40)=20
        //   MaxPositionPct override = 20 / 100 = 0.20
        //   MinNotional            = max(20, instrument.MinNotional=5) = 20
        captured!.MaxPositionPct.Should().Be(0.20m);
        captured.MinNotional.Should().Be(20m);
    }

    /// <summary>
    /// ADR-0017 §17.9: equity=$50 -> target=max(50*0.20, 20)=$20 (floor),
    /// cap=50*0.40=$20 -> chosen=$20 -> MaxPositionPct=20/50=0.40.
    /// Floor wins when equity is tiny.
    /// </summary>
    [Fact]
    public async Task Long_SizingOverride_Equity50_FloorWins()
    {
        var (scopeFactory, mediator, sizing, _, db) = BuildHarness(equity: 50m);
        RaiseHardCapToLoop14Default(db);

        PositionSizingInput? captured = null;
        sizing.Setup(s => s.Calculate(It.IsAny<PositionSizingInput>()))
            .Callback<PositionSizingInput>(i => captured = i)
            .Returns(new PositionSizingResult(SizedQty, 20m, null));

        mediator.Setup(m => m.Send(It.IsAny<PlaceOrderCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new PlacedOrderDto("x", "BTCUSDT", "Filled", SizedQty, null, TradingMode.Paper)));

        var sut = new StrategySignalToOrderHandler(
            scopeFactory, NullLogger<StrategySignalToOrderHandler>.Instance);

        await sut.Handle(new StrategySignalEmittedEvent(
            1, "BTCUSDT", StrategySignalDirection.Long, DateTimeOffset.UtcNow, 29500m),
            CancellationToken.None);

        captured!.MinNotional.Should().Be(20m);
        captured.MaxPositionPct.Should().Be(0.40m); // 20 / 50
    }

    /// <summary>
    /// ADR-0017 §17.9: equity=$200 -> target=max(200*0.20,20)=$40, cap=200*0.40=$80
    /// -> chosen=$40 -> MaxPositionPct=40/200=0.20. Snowball growth.
    /// </summary>
    [Fact]
    public async Task Long_SizingOverride_Equity200_PctFraction()
    {
        var (scopeFactory, mediator, sizing, _, db) = BuildHarness(equity: 200m);
        RaiseHardCapToLoop14Default(db);

        PositionSizingInput? captured = null;
        sizing.Setup(s => s.Calculate(It.IsAny<PositionSizingInput>()))
            .Callback<PositionSizingInput>(i => captured = i)
            .Returns(new PositionSizingResult(SizedQty, 40m, null));

        mediator.Setup(m => m.Send(It.IsAny<PlaceOrderCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new PlacedOrderDto("x", "BTCUSDT", "Filled", SizedQty, null, TradingMode.Paper)));

        var sut = new StrategySignalToOrderHandler(
            scopeFactory, NullLogger<StrategySignalToOrderHandler>.Instance);

        await sut.Handle(new StrategySignalEmittedEvent(
            1, "BTCUSDT", StrategySignalDirection.Long, DateTimeOffset.UtcNow, 29500m),
            CancellationToken.None);

        captured!.MinNotional.Should().Be(40m);
        captured.MaxPositionPct.Should().Be(0.20m);
    }

    /// <summary>
    /// ADR-0017 §17.9: equity=$500 -> target=max(500*0.20,20)=$100, cap=500*0.40=$200
    /// -> chosen=$100. MinNotional=100, MaxPositionPct=100/500=0.20.
    /// </summary>
    [Fact]
    public async Task Long_SizingOverride_Equity500_KeepsPctAtTwentyPercent()
    {
        var (scopeFactory, mediator, sizing, _, db) = BuildHarness(equity: 500m);
        RaiseHardCapToLoop14Default(db);

        PositionSizingInput? captured = null;
        sizing.Setup(s => s.Calculate(It.IsAny<PositionSizingInput>()))
            .Callback<PositionSizingInput>(i => captured = i)
            .Returns(new PositionSizingResult(SizedQty, 100m, null));

        mediator.Setup(m => m.Send(It.IsAny<PlaceOrderCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new PlacedOrderDto("x", "BTCUSDT", "Filled", SizedQty, null, TradingMode.Paper)));

        var sut = new StrategySignalToOrderHandler(
            scopeFactory, NullLogger<StrategySignalToOrderHandler>.Instance);

        await sut.Handle(new StrategySignalEmittedEvent(
            1, "BTCUSDT", StrategySignalDirection.Long, DateTimeOffset.UtcNow, 29500m),
            CancellationToken.None);

        captured!.MinNotional.Should().Be(100m);
        captured.MaxPositionPct.Should().Be(0.20m);
    }
}
