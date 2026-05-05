using Ardalis.Result;
using BinanceBot.Application.Abstractions;
using BinanceBot.Application.Positions.Commands.ClosePosition;
using BinanceBot.Domain.Common;
using BinanceBot.Domain.MarketData;
using BinanceBot.Domain.Positions;
using BinanceBot.Domain.ValueObjects;
using BinanceBot.Infrastructure.Positions;
using BinanceBot.Tests.Infrastructure.Strategies;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using System.Reflection;

namespace BinanceBot.Tests.Infrastructure.Positions;

/// <summary>
/// Loop 109 — defansif safety net regresyon testleri. Loop 108'de tespit edilen
/// senaryo: ETH Long pos BE applied, mark $2376 &lt; SL $2395 ama pozisyon
/// hala açık + 79dk hold (MaxHold=60 geçti). Primary <c>StopLossMonitorService</c>
/// bilinmeyen sebeple atlanmış olsa bile <see cref="MarkToMarketWorker"/>
/// safety net buradan kapatmalı:
/// <list type="bullet">
///   <item>SL hit redundancy — Long: <c>mark &lt;= StopPrice</c>.</item>
///   <item>TP hit redundancy — Long: <c>mark &gt;= TakeProfit</c>.</item>
///   <item>Hard max-hold — pos.OpenedAt + HardMaxHoldMinutes &lt;= now (MaxHoldDuration null olsa bile).</item>
/// </list>
/// </summary>
public class MarkToMarketWorkerSafetyNetTests
{
    private const string Sym = "ETHUSDT";
    private static readonly DateTimeOffset T0 = new(2026, 5, 3, 12, 0, 0, TimeSpan.Zero);

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset now) { UtcNow = now; }
        public DateTimeOffset UtcNow { get; }
        public long BinanceServerTimeMs => UtcNow.ToUnixTimeMilliseconds();
        public long DriftMs => 0;
    }

    private static IServiceScopeFactory BuildScope(StubDbContext db, IClock clock, IMediator mediator)
    {
        var sc = new ServiceCollection();
        sc.AddSingleton<IApplicationDbContext>(db);
        sc.AddSingleton(clock);
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

    private static IOptionsMonitor<BreakEvenOptions> BeDisabled()
        => new StaticOptionsMonitor<BreakEvenOptions>(new BreakEvenOptions { Enabled = false });

    private static IOptionsMonitor<TrailingStopOptions> TrailDisabled()
        => new StaticOptionsMonitor<TrailingStopOptions>(new TrailingStopOptions { Enabled = false });

    private static IOptionsMonitor<PositionSafetyOptions> Safety(PositionSafetyOptions value)
        => new StaticOptionsMonitor<PositionSafetyOptions>(value);

    private static Position SeedLong(
        StubDbContext db,
        decimal entry,
        decimal stop,
        decimal? takeProfit = null,
        TimeSpan? maxHoldDuration = null,
        DateTimeOffset? openedAt = null)
    {
        var pos = Position.Open(
            Symbol.From(Sym),
            TradeDirection.Long,
            quantity: 0.5m,
            entryPrice: entry,
            stopPrice: stop,
            strategyId: 1,
            mode: TradingMode.Paper,
            now: openedAt ?? T0,
            takeProfit: takeProfit,
            maxHoldDuration: maxHoldDuration);
        db.Positions.Add(pos);
        db.SaveChanges();
        return pos;
    }

    private static void SeedTicker(StubDbContext db, decimal bid, decimal ask, DateTimeOffset asOf)
    {
        db.BookTickers.Add(BookTicker.Create(
            Symbol.From(Sym),
            bidPrice: bid, bidQty: 1m,
            askPrice: ask, askQty: 1m,
            updateId: 1, updatedAt: asOf));
        db.SaveChanges();
    }

    private static Task InvokeTickAsync(MarkToMarketWorker worker, CancellationToken ct)
    {
        var method = typeof(MarkToMarketWorker)
            .GetMethod("TickAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (Task)method.Invoke(worker, new object[] { ct })!;
    }

    /// <summary>
    /// Loop 108 ETH bug regresyon: Long pos entry=2390.89, BE armed → SL=2395.68
    /// (entry × 1.002), mark=2376.89 (entry-%0.59). Long pos için <c>mark &lt;= SL</c>
    /// ⇒ SL hit. Primary <c>StopLossMonitorService</c> 5sn cycle'da tetiklemediyse
    /// <see cref="MarkToMarketWorker"/> safety net aynı tick'te kapatmalı.
    /// </summary>
    [Fact]
    public async Task LongPosition_BeArmed_MarkBelowSl_TriggersSafetyExit()
    {
        using var db = NewDb();
        var pos = SeedLong(db, entry: 2390.89m, stop: 2395.68m);  // BE armed equivalent
        SeedTicker(db, bid: 2376.89m, ask: 2376.89m, asOf: T0.AddMinutes(30));

        CloseSignalPositionCommand? captured = null;
        var mediator = new Mock<IMediator>(MockBehavior.Strict);
        mediator
            .Setup(m => m.Send(It.IsAny<CloseSignalPositionCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<Result<ClosedSignalPositionDto>>, CancellationToken>(
                (cmd, _) => captured = (CloseSignalPositionCommand)cmd)
            .ReturnsAsync(Result.Success(new ClosedSignalPositionDto(
                pos.Id, 0m, "safety_stop_loss", "safe-sl-1-x")));

        var sut = new MarkToMarketWorker(
            BuildScope(db, new FixedClock(T0.AddMinutes(30)), mediator.Object),
            NullLogger<MarkToMarketWorker>.Instance,
            BeDisabled(), TrailDisabled(),
            Safety(new PositionSafetyOptions { Enabled = true, StopLossRedundancyEnabled = true }));

        await InvokeTickAsync(sut, CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.Symbol.Should().Be(Sym);
        captured.Reason.Should().StartWith("safety_stop_loss@");
        captured.CorrelationCidPrefix.Should().StartWith($"safe-sl-{pos.Id}-");
    }

    /// <summary>
    /// Short pos için SL hit redundancy: <c>mark &gt;= StopPrice</c> ⇒ exit.
    /// </summary>
    [Fact]
    public async Task ShortPosition_MarkAboveSl_TriggersSafetyExit()
    {
        using var db = NewDb();
        var pos = Position.Open(
            Symbol.From(Sym),
            TradeDirection.Short,
            quantity: 0.5m,
            entryPrice: 2400m,
            stopPrice: 2412m,
            strategyId: 1,
            mode: TradingMode.Paper,
            now: T0);
        db.Positions.Add(pos);
        db.SaveChanges();
        SeedTicker(db, bid: 2415m, ask: 2415m, asOf: T0.AddMinutes(10));

        var sent = 0;
        var mediator = new Mock<IMediator>(MockBehavior.Strict);
        mediator
            .Setup(m => m.Send(It.IsAny<CloseSignalPositionCommand>(), It.IsAny<CancellationToken>()))
            .Callback(() => sent++)
            .ReturnsAsync(Result.Success(new ClosedSignalPositionDto(pos.Id, 0m, "x", "safe-sl-1-x")));

        var sut = new MarkToMarketWorker(
            BuildScope(db, new FixedClock(T0.AddMinutes(10)), mediator.Object),
            NullLogger<MarkToMarketWorker>.Instance,
            BeDisabled(), TrailDisabled(),
            Safety(new PositionSafetyOptions { Enabled = true, StopLossRedundancyEnabled = true }));

        await InvokeTickAsync(sut, CancellationToken.None);

        sent.Should().Be(1);
    }

    /// <summary>
    /// TP hit redundancy: Long pos mark &gt;= TakeProfit ⇒ safety_take_profit exit.
    /// </summary>
    [Fact]
    public async Task LongPosition_MarkAtTakeProfit_TriggersSafetyExit()
    {
        using var db = NewDb();
        var pos = SeedLong(db, entry: 2400m, stop: 2380m, takeProfit: 2440m);
        SeedTicker(db, bid: 2445m, ask: 2445m, asOf: T0.AddMinutes(5));

        CloseSignalPositionCommand? captured = null;
        var mediator = new Mock<IMediator>(MockBehavior.Strict);
        mediator
            .Setup(m => m.Send(It.IsAny<CloseSignalPositionCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<Result<ClosedSignalPositionDto>>, CancellationToken>(
                (cmd, _) => captured = (CloseSignalPositionCommand)cmd)
            .ReturnsAsync(Result.Success(new ClosedSignalPositionDto(pos.Id, 0m, "x", "safe-tp-1-x")));

        var sut = new MarkToMarketWorker(
            BuildScope(db, new FixedClock(T0.AddMinutes(5)), mediator.Object),
            NullLogger<MarkToMarketWorker>.Instance,
            BeDisabled(), TrailDisabled(),
            Safety(new PositionSafetyOptions
            {
                Enabled = true,
                StopLossRedundancyEnabled = false,  // sadece TP yolunu test et
                TakeProfitRedundancyEnabled = true,
            }));

        await InvokeTickAsync(sut, CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.Reason.Should().StartWith("safety_take_profit@");
    }

    /// <summary>
    /// Loop 108 MaxHold timeout regresyon: <c>MaxHoldDuration</c> NULL olsa bile
    /// (signal ContextJson freshness window dışı) HardMaxHoldMinutes ile zorla
    /// kapatılır. Pos 79dk açık, hard cap 60dk → trigger.
    /// </summary>
    [Fact]
    public async Task PositionExceedsHardMaxHold_TriggersSafetyExit_EvenWithoutMaxHoldDuration()
    {
        using var db = NewDb();
        // OpenedAt 79dk önce; MaxHoldDuration null (signal ContextJson eksikti).
        // StopPrice de null — yalnızca hard max-hold tetiklemeli.
        var pos = SeedLong(db, entry: 2400m, stop: 2380m, maxHoldDuration: null,
            openedAt: T0.AddMinutes(-79));
        SeedTicker(db, bid: 2398m, ask: 2398m, asOf: T0);  // mark > stop, SL trigger değil

        CloseSignalPositionCommand? captured = null;
        var mediator = new Mock<IMediator>(MockBehavior.Strict);
        mediator
            .Setup(m => m.Send(It.IsAny<CloseSignalPositionCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<Result<ClosedSignalPositionDto>>, CancellationToken>(
                (cmd, _) => captured = (CloseSignalPositionCommand)cmd)
            .ReturnsAsync(Result.Success(new ClosedSignalPositionDto(pos.Id, 0m, "x", "safe-mh-1-x")));

        var sut = new MarkToMarketWorker(
            BuildScope(db, new FixedClock(T0), mediator.Object),
            NullLogger<MarkToMarketWorker>.Instance,
            BeDisabled(), TrailDisabled(),
            Safety(new PositionSafetyOptions
            {
                Enabled = true,
                StopLossRedundancyEnabled = true,
                TakeProfitRedundancyEnabled = true,
                HardMaxHoldMinutes = 60,
            }));

        await InvokeTickAsync(sut, CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.Reason.Should().StartWith("safety_hard_max_hold@");
        captured.CorrelationCidPrefix.Should().StartWith($"safe-mh-{pos.Id}-");
    }

    /// <summary>
    /// Hard max-hold disable (HardMaxHoldMinutes=0) iken yaşlı pos da kapanmaz.
    /// </summary>
    [Fact]
    public async Task HardMaxHoldZero_DoesNotTrigger()
    {
        using var db = NewDb();
        var pos = SeedLong(db, entry: 2400m, stop: 2380m, openedAt: T0.AddHours(-3));
        SeedTicker(db, bid: 2398m, ask: 2398m, asOf: T0);

        var mediator = new Mock<IMediator>(MockBehavior.Strict);
        // strict mock — Send çağrılırsa fail (no setup).

        var sut = new MarkToMarketWorker(
            BuildScope(db, new FixedClock(T0), mediator.Object),
            NullLogger<MarkToMarketWorker>.Instance,
            BeDisabled(), TrailDisabled(),
            Safety(new PositionSafetyOptions
            {
                Enabled = true,
                StopLossRedundancyEnabled = true,
                HardMaxHoldMinutes = 0,
            }));

        await InvokeTickAsync(sut, CancellationToken.None);

        mediator.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Master switch <c>Enabled=false</c> tüm safety dalını atlar.
    /// </summary>
    [Fact]
    public async Task SafetyDisabled_DoesNotTriggerEvenWhenAllConditionsMet()
    {
        using var db = NewDb();
        var pos = SeedLong(db, entry: 2400m, stop: 2410m, openedAt: T0.AddMinutes(-200));
        SeedTicker(db, bid: 2380m, ask: 2380m, asOf: T0);  // mark < stop AND yaş > hard cap

        var mediator = new Mock<IMediator>(MockBehavior.Strict);

        var sut = new MarkToMarketWorker(
            BuildScope(db, new FixedClock(T0), mediator.Object),
            NullLogger<MarkToMarketWorker>.Instance,
            BeDisabled(), TrailDisabled(),
            Safety(new PositionSafetyOptions
            {
                Enabled = false,
                StopLossRedundancyEnabled = true,
                HardMaxHoldMinutes = 60,
            }));

        await InvokeTickAsync(sut, CancellationToken.None);

        mediator.VerifyNoOtherCalls();
    }

    /// <summary>
    /// LiveMainnet defansif guard: safety net mainnet pos'ları atlar (audit pollution
    /// olmasın). ADR-0006 paterni.
    /// </summary>
    [Fact]
    public async Task LiveMainnetPosition_IsSkippedDefensively()
    {
        using var db = NewDb();
        var pos = Position.Open(
            Symbol.From(Sym),
            TradeDirection.Long,
            quantity: 0.5m,
            entryPrice: 2400m,
            stopPrice: 2410m,
            strategyId: 1,
            mode: TradingMode.LiveMainnet,
            now: T0);
        db.Positions.Add(pos);
        db.SaveChanges();
        SeedTicker(db, bid: 2380m, ask: 2380m, asOf: T0.AddMinutes(5));

        var mediator = new Mock<IMediator>(MockBehavior.Strict);
        // strict — Send mainnet pos için çağrılırsa fail.

        var sut = new MarkToMarketWorker(
            BuildScope(db, new FixedClock(T0.AddMinutes(5)), mediator.Object),
            NullLogger<MarkToMarketWorker>.Instance,
            BeDisabled(), TrailDisabled(),
            Safety(new PositionSafetyOptions { Enabled = true, StopLossRedundancyEnabled = true }));

        await InvokeTickAsync(sut, CancellationToken.None);

        mediator.VerifyNoOtherCalls();
    }
}
