using BinanceBot.Application.Abstractions;
using BinanceBot.Domain.Common;
using BinanceBot.Domain.MarketData;
using BinanceBot.Domain.Positions;
using BinanceBot.Domain.ValueObjects;
using BinanceBot.Infrastructure.Positions;
using BinanceBot.Tests.Infrastructure.Strategies;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Reflection;

namespace BinanceBot.Tests.Infrastructure.Positions;

/// <summary>
/// Loop 75 — break-even SL move integration with <see cref="MarkToMarketWorker"/>.
/// Trigger geometry, idempotency between consecutive ticks, ve options.Enabled toggle.
/// </summary>
public class MarkToMarketWorkerBreakEvenTests
{
    private const string Sym = "BTCUSDT";
    private const decimal Entry = 30000m;

    private static readonly DateTimeOffset T0 = new(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset now) { UtcNow = now; }
        public DateTimeOffset UtcNow { get; }
        public long BinanceServerTimeMs => UtcNow.ToUnixTimeMilliseconds();
        public long DriftMs => 0;
    }

    private static IServiceScopeFactory BuildScope(StubDbContext db, IClock clock)
    {
        var sc = new ServiceCollection();
        sc.AddSingleton<IApplicationDbContext>(db);
        sc.AddSingleton(clock);
        // Loop 76 — MarkToMarketWorker artık IMediator scope üzerinden çözüyor.
        // BE testleri trailing exit dispatch'i tetiklemediği için Loose mock yeterli
        // (TrailingStopOptions.Enabled=false → mediator hiç çağrılmaz).
        sc.AddSingleton<MediatR.IMediator>(new Moq.Mock<MediatR.IMediator>().Object);
        return sc.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private static StubDbContext NewDb()
    {
        var opts = new DbContextOptionsBuilder<StubDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new StubDbContext(opts);
    }

    private static IOptionsMonitor<BreakEvenOptions> Opts(BreakEvenOptions value)
    {
        return new StaticOptionsMonitor<BreakEvenOptions>(value);
    }

    private static IOptionsMonitor<TrailingStopOptions> TrailOpts(TrailingStopOptions value)
    {
        return new StaticOptionsMonitor<TrailingStopOptions>(value);
    }

    private static Position SeedLong(StubDbContext db, decimal stop)
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
        db.Positions.Add(pos);
        db.SaveChanges();
        return pos;
    }

    private static void SeedTicker(StubDbContext db, decimal bid, decimal ask)
    {
        db.BookTickers.Add(BookTicker.Create(
            Symbol.From(Sym),
            bidPrice: bid, bidQty: 1m,
            askPrice: ask, askQty: 1m,
            updateId: 1, updatedAt: T0));
        db.SaveChanges();
    }

    private static Task InvokeTickAsync(MarkToMarketWorker worker, CancellationToken ct)
    {
        var method = typeof(MarkToMarketWorker)
            .GetMethod("TickAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (Task)method.Invoke(worker, new object[] { ct })!;
    }

    [Fact]
    public async Task MarkAboveTrigger_AppliesBreakEvenMove()
    {
        // Setup: Long pos entry=30000, stop=29700 (-1.0% SL). Mid=30030 → mark/entry = +0.10%
        // (=TriggerPct default 0.0010). Beklenen BE: stop = 30000 × (1+0.0002) = 30006.
        using var db = NewDb();
        var pos = SeedLong(db, stop: 29700m);
        SeedTicker(db, bid: 30030m, ask: 30030m);

        var sut = new MarkToMarketWorker(
            BuildScope(db, new FixedClock(T0.AddSeconds(90))),
            NullLogger<MarkToMarketWorker>.Instance,
            Opts(new BreakEvenOptions
            {
                Enabled = true,
                TriggerPct = 0.0010m,
                OffsetPct = 0.0002m,
            }),
            TrailOpts(new TrailingStopOptions { Enabled = false }));

        await InvokeTickAsync(sut, CancellationToken.None);

        var saved = db.Positions.Single(p => p.Id == pos.Id);
        saved.StopPrice.Should().Be(30006m);
        saved.BreakEvenAppliedAt.Should().Be(T0.AddSeconds(90));
    }

    [Fact]
    public async Task MarkBelowTrigger_DoesNotMoveStop()
    {
        // Mid=30015 → mark/entry = +0.05%, eşik (0.10%) altında, BE move yok.
        using var db = NewDb();
        var pos = SeedLong(db, stop: 29700m);
        SeedTicker(db, bid: 30015m, ask: 30015m);

        var sut = new MarkToMarketWorker(
            BuildScope(db, new FixedClock(T0.AddSeconds(90))),
            NullLogger<MarkToMarketWorker>.Instance,
            Opts(new BreakEvenOptions
            {
                Enabled = true,
                TriggerPct = 0.0010m,
                OffsetPct = 0.0002m,
            }),
            TrailOpts(new TrailingStopOptions { Enabled = false }));

        await InvokeTickAsync(sut, CancellationToken.None);

        var saved = db.Positions.Single(p => p.Id == pos.Id);
        saved.StopPrice.Should().Be(29700m, "trigger eşiği aşılmadı");
        saved.BreakEvenAppliedAt.Should().BeNull();
    }

    [Fact]
    public async Task SecondTick_DoesNotReapplyBreakEven()
    {
        // İlk tick BE uygular; ikinci tick (mark çok daha yukarı bile olsa)
        // BreakEvenAppliedAt zaten set olduğu için no-op.
        using var db = NewDb();
        var pos = SeedLong(db, stop: 29700m);
        SeedTicker(db, bid: 30030m, ask: 30030m);

        var sut = new MarkToMarketWorker(
            BuildScope(db, new FixedClock(T0.AddSeconds(90))),
            NullLogger<MarkToMarketWorker>.Instance,
            Opts(new BreakEvenOptions
            {
                Enabled = true,
                TriggerPct = 0.0010m,
                OffsetPct = 0.0002m,
            }),
            TrailOpts(new TrailingStopOptions { Enabled = false }));

        await InvokeTickAsync(sut, CancellationToken.None);

        var afterFirst = db.Positions.Single(p => p.Id == pos.Id);
        var firstStamp = afterFirst.BreakEvenAppliedAt;
        afterFirst.StopPrice.Should().Be(30006m);

        // İkinci tick — mark daha yukarı (30090 = +0.30%) ama BE bir kez uygulandı.
        db.BookTickers.RemoveRange(db.BookTickers.ToList());
        db.SaveChanges();
        SeedTicker(db, bid: 30090m, ask: 30090m);

        // Yeni clock, yeni worker (clock advance — ikinci tick farklı zamanda).
        var sut2 = new MarkToMarketWorker(
            BuildScope(db, new FixedClock(T0.AddMinutes(5))),
            NullLogger<MarkToMarketWorker>.Instance,
            Opts(new BreakEvenOptions
            {
                Enabled = true,
                TriggerPct = 0.0010m,
                OffsetPct = 0.0002m,
            }),
            TrailOpts(new TrailingStopOptions { Enabled = false }));

        await InvokeTickAsync(sut2, CancellationToken.None);

        var afterSecond = db.Positions.Single(p => p.Id == pos.Id);
        afterSecond.StopPrice.Should().Be(30006m, "BE bir kez uygulanır");
        afterSecond.BreakEvenAppliedAt.Should().Be(firstStamp, "audit timestamp değişmez");
    }

    [Fact]
    public async Task DisabledOption_DoesNotApplyEvenWhenAboveTrigger()
    {
        using var db = NewDb();
        var pos = SeedLong(db, stop: 29700m);
        SeedTicker(db, bid: 30030m, ask: 30030m);

        var sut = new MarkToMarketWorker(
            BuildScope(db, new FixedClock(T0.AddSeconds(90))),
            NullLogger<MarkToMarketWorker>.Instance,
            Opts(new BreakEvenOptions
            {
                Enabled = false,
                TriggerPct = 0.0010m,
                OffsetPct = 0.0002m,
            }),
            TrailOpts(new TrailingStopOptions { Enabled = false }));

        await InvokeTickAsync(sut, CancellationToken.None);

        var saved = db.Positions.Single(p => p.Id == pos.Id);
        saved.StopPrice.Should().Be(29700m);
        saved.BreakEvenAppliedAt.Should().BeNull();
    }
}

/// <summary>
/// Minimal IOptionsMonitor stub — testler farklı option set'leri için
/// IConfiguration/Snapshot bağımlılığını aşmadan inject eder.
/// </summary>
internal sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
{
    public StaticOptionsMonitor(T value) { CurrentValue = value; }
    public T CurrentValue { get; }
    public T Get(string? name) => CurrentValue;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}

