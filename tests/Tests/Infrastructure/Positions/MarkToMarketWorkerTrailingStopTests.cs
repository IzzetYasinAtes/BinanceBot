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
/// Loop 76 — trailing-stop integration with <see cref="MarkToMarketWorker"/>.
/// BE move'la zincirleme: BE applied → trailing peak track → drop'ta exit dispatch.
/// MediatR strict mock CloseSignalPositionCommand'i yakalar, dispatch payload'ı doğrular.
/// </summary>
public class MarkToMarketWorkerTrailingStopTests
{
    private const string Sym = "BTCUSDT";
    private const decimal Entry = 95000m;

    private static readonly DateTimeOffset T0 = new(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);

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

    private static IOptionsMonitor<BreakEvenOptions> BeOpts(BreakEvenOptions value)
        => new StaticOptionsMonitor<BreakEvenOptions>(value);

    private static IOptionsMonitor<TrailingStopOptions> TrailOpts(TrailingStopOptions value)
        => new StaticOptionsMonitor<TrailingStopOptions>(value);

    /// <summary>
    /// Loop 109 — varsayılan safety net opts (trailing testleri redundancy
    /// dalını tetiklemesin diye <c>Enabled=false</c>).
    /// </summary>
    private static IOptionsMonitor<PositionSafetyOptions> SafetyOptsDisabled()
        => new StaticOptionsMonitor<PositionSafetyOptions>(
            new PositionSafetyOptions { Enabled = false });

    /// <summary>
    /// Position'u BE applied state'iyle seed et — trailing'i hemen "armed" başlat.
    /// Üretim path'inde MarkToMarketWorker BE'yi ilk tick'te uygular; testin
    /// trailing-only odaklılığını bozmamak için domain method direkt çağrılıyor.
    /// </summary>
    private static Position SeedLongWithBeApplied(StubDbContext db, decimal stop, decimal beStop)
    {
        var pos = Position.Open(
            Symbol.From(Sym),
            TradeDirection.Long,
            quantity: 0.01m,
            entryPrice: Entry,
            stopPrice: stop,
            strategyId: 1,
            mode: TradingMode.Paper,
            now: T0);
        pos.MoveStopToBreakEven(beStop, T0.AddMinutes(10));
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

    private static void ReplaceTicker(StubDbContext db, decimal bid, decimal ask, DateTimeOffset asOf)
    {
        db.BookTickers.RemoveRange(db.BookTickers.ToList());
        db.SaveChanges();
        SeedTicker(db, bid, ask, asOf);
    }

    private static Task InvokeTickAsync(MarkToMarketWorker worker, CancellationToken ct)
    {
        var method = typeof(MarkToMarketWorker)
            .GetMethod("TickAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (Task)method.Invoke(worker, new object[] { ct })!;
    }

    [Fact]
    public async Task BeApplied_NewHigh_UpdatesPeakWithoutDispatch()
    {
        // BE applied (stop=95019). Mark=95400 → peak başlangıç. Trail eşiği:
        // 95400×(1−0.0015)=95256.9. Mark > eşik → exit dispatch yok.
        using var db = NewDb();
        var pos = SeedLongWithBeApplied(db, stop: 94240m, beStop: 95019m);
        SeedTicker(db, bid: 95400m, ask: 95400m, asOf: T0.AddMinutes(15));

        var mediator = new Mock<IMediator>(MockBehavior.Strict);
        // Strict mock — dispatch çağrılırsa test fail (peak update aşamasında dispatch beklenmiyor).

        var sut = new MarkToMarketWorker(
            BuildScope(db, new FixedClock(T0.AddMinutes(15)), mediator.Object),
            NullLogger<MarkToMarketWorker>.Instance,
            BeOpts(new BreakEvenOptions { Enabled = false }),  // BE zaten applied; bu tick BE dalını atlasın
            TrailOpts(new TrailingStopOptions { Enabled = true, TrailPct = 0.0015m }),
            SafetyOptsDisabled());

        await InvokeTickAsync(sut, CancellationToken.None);

        var saved = db.Positions.Single(p => p.Id == pos.Id);
        saved.ExtremeMarkPrice.Should().Be(95400m, "ilk eligible tick peak'i yatırır");
        mediator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task BeApplied_PeakThenDrop_DispatchesTrailingExit()
    {
        // Tick 1: mark=95600 → peak=95600. Trail eşiği=95456.4.
        // Tick 2: mark=95300 → 95300 < 95456.4 → ExitTriggered + dispatch.
        using var db = NewDb();
        var pos = SeedLongWithBeApplied(db, stop: 94240m, beStop: 95019m);
        SeedTicker(db, bid: 95600m, ask: 95600m, asOf: T0.AddMinutes(20));

        var mediator = new Mock<IMediator>(MockBehavior.Strict);
        CloseSignalPositionCommand? captured = null;
        mediator
            .Setup(m => m.Send(It.IsAny<CloseSignalPositionCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<Result<ClosedSignalPositionDto>>, CancellationToken>(
                (cmd, _) => captured = (CloseSignalPositionCommand)cmd)
            .ReturnsAsync(Result.Success(new ClosedSignalPositionDto(
                pos.Id, 0m, "trail", "trail-cid-x")));

        // Tick 1: peak yatır.
        var sut = new MarkToMarketWorker(
            BuildScope(db, new FixedClock(T0.AddMinutes(20)), mediator.Object),
            NullLogger<MarkToMarketWorker>.Instance,
            BeOpts(new BreakEvenOptions { Enabled = false }),
            TrailOpts(new TrailingStopOptions { Enabled = true, TrailPct = 0.0015m }),
            SafetyOptsDisabled());
        await InvokeTickAsync(sut, CancellationToken.None);

        var afterFirst = db.Positions.Single(p => p.Id == pos.Id);
        afterFirst.ExtremeMarkPrice.Should().Be(95600m);

        // Tick 2: mark drop.
        ReplaceTicker(db, bid: 95300m, ask: 95300m, asOf: T0.AddMinutes(25));
        var sut2 = new MarkToMarketWorker(
            BuildScope(db, new FixedClock(T0.AddMinutes(25)), mediator.Object),
            NullLogger<MarkToMarketWorker>.Instance,
            BeOpts(new BreakEvenOptions { Enabled = false }),
            TrailOpts(new TrailingStopOptions { Enabled = true, TrailPct = 0.0015m }),
            SafetyOptsDisabled());
        await InvokeTickAsync(sut2, CancellationToken.None);

        captured.Should().NotBeNull("trail eşiği aşıldı, exit dispatch zorunlu");
        captured!.Symbol.Should().Be(Sym);
        captured.Mode.Should().Be(TradingMode.Paper);
        captured.StrategyId.Should().Be(1);
        captured.Reason.Should().StartWith("trailing_stop_triggered@");
        captured.CorrelationCidPrefix.Should().StartWith($"trail-{pos.Id}-");

        // Peak değişmedi (exit'te aggregate peak'i sabit tutar — audit için).
        var afterSecond = db.Positions.Single(p => p.Id == pos.Id);
        afterSecond.ExtremeMarkPrice.Should().Be(95600m);
    }

    [Fact]
    public async Task BeNotApplied_TracksPeakButDoesNotDispatchExit()
    {
        // Loop 94 davranış değişikliği: BE applied null iken peak tracking
        // ARTIK aktif (Loop 93 t60 regresyonu fix). Mark refresh edilir AMA
        // exit dispatch yok. Mediator strict mock; trailing dalı dispatch
        // ederse test fail.
        using var db = NewDb();
        var pos = Position.Open(
            Symbol.From(Sym),
            TradeDirection.Long,
            quantity: 0.01m,
            entryPrice: Entry,
            stopPrice: 94240m,
            strategyId: 1,
            mode: TradingMode.Paper,
            now: T0);
        db.Positions.Add(pos);
        db.SaveChanges();
        SeedTicker(db, bid: 95400m, ask: 95400m, asOf: T0.AddMinutes(5));

        var mediator = new Mock<IMediator>(MockBehavior.Strict);

        var sut = new MarkToMarketWorker(
            BuildScope(db, new FixedClock(T0.AddMinutes(5)), mediator.Object),
            NullLogger<MarkToMarketWorker>.Instance,
            // BE'yi de false bırak → bu test artık "BE null → peak tracking yapar
            // ama exit yok" yolunu doğrular (Loop 94).
            BeOpts(new BreakEvenOptions { Enabled = false }),
            TrailOpts(new TrailingStopOptions { Enabled = true, TrailPct = 0.0015m }),
            SafetyOptsDisabled());

        await InvokeTickAsync(sut, CancellationToken.None);

        var saved = db.Positions.Single(p => p.Id == pos.Id);
        saved.ExtremeMarkPrice.Should().Be(95400m, "Loop 94: BE null'da peak tracking aktif");
        saved.BreakEvenAppliedAt.Should().BeNull();
        // Mediator strict mock → trailing exit dispatch ederse VerifyNoOtherCalls fail.
        mediator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task TrailingDisabled_NoOpEvenAfterBeApplied()
    {
        // TrailingStopOptions.Enabled=false → BE applied olsa bile trailing hook çalışmaz.
        using var db = NewDb();
        var pos = SeedLongWithBeApplied(db, stop: 94240m, beStop: 95019m);
        SeedTicker(db, bid: 95600m, ask: 95600m, asOf: T0.AddMinutes(20));

        var mediator = new Mock<IMediator>(MockBehavior.Strict);

        var sut = new MarkToMarketWorker(
            BuildScope(db, new FixedClock(T0.AddMinutes(20)), mediator.Object),
            NullLogger<MarkToMarketWorker>.Instance,
            BeOpts(new BreakEvenOptions { Enabled = false }),
            TrailOpts(new TrailingStopOptions { Enabled = false, TrailPct = 0.0015m }),
            SafetyOptsDisabled());

        await InvokeTickAsync(sut, CancellationToken.None);

        var saved = db.Positions.Single(p => p.Id == pos.Id);
        saved.ExtremeMarkPrice.Should().Be(0m, "trailing disable iken peak güncellenmemeli");
        mediator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task BeAppliedAndTrailingChained_SameTick_PeakSeededFromMark()
    {
        // Combo doğrulama: BE move bu tick'te uygulanır; trailing aynı tick'te
        // peak'i mark'a yatırır. Sıra (BE → trailing) korunmazsa trailing NotEligible
        // döner ve peak 0 kalır.
        using var db = NewDb();
        var pos = Position.Open(
            Symbol.From(Sym),
            TradeDirection.Long,
            quantity: 0.01m,
            entryPrice: Entry,
            stopPrice: 94240m,
            strategyId: 1,
            mode: TradingMode.Paper,
            now: T0);
        db.Positions.Add(pos);
        db.SaveChanges();

        // Mid = 95400 → BE trigger eşiği (Entry × 1.001 = 95095) aşıldı. BE move sonrası
        // trailing aynı tick'te peak=95400 yatırmalı.
        SeedTicker(db, bid: 95400m, ask: 95400m, asOf: T0.AddMinutes(15));

        var mediator = new Mock<IMediator>(MockBehavior.Strict);

        var sut = new MarkToMarketWorker(
            BuildScope(db, new FixedClock(T0.AddMinutes(15)), mediator.Object),
            NullLogger<MarkToMarketWorker>.Instance,
            BeOpts(new BreakEvenOptions { Enabled = true, TriggerPct = 0.0010m, OffsetPct = 0.0002m }),
            TrailOpts(new TrailingStopOptions { Enabled = true, TrailPct = 0.0015m }),
            SafetyOptsDisabled());

        await InvokeTickAsync(sut, CancellationToken.None);

        var saved = db.Positions.Single(p => p.Id == pos.Id);
        saved.BreakEvenAppliedAt.Should().Be(T0.AddMinutes(15), "BE bu tick'te uygulandı");
        saved.ExtremeMarkPrice.Should().Be(95400m, "trailing aynı tick'te peak'i yatırdı (sıra: BE → trail)");
        mediator.VerifyNoOtherCalls();
    }
}
