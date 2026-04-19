using BinanceBot.Application.Abstractions;
using BinanceBot.Domain.BacktestRuns;
using BinanceBot.Domain.Balances;
using BinanceBot.Domain.Common;
using BinanceBot.Domain.Instruments;
using BinanceBot.Domain.MarketData;
using BinanceBot.Domain.Orders;
using BinanceBot.Domain.Orders.Events;
using BinanceBot.Domain.Positions;
using BinanceBot.Domain.RiskProfiles;
using BinanceBot.Domain.Strategies;
using BinanceBot.Domain.SystemEvents;
using BinanceBot.Domain.ValueObjects;
using BinanceBot.Infrastructure.Orders;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace BinanceBot.Tests.Infrastructure.Orders;

/// <summary>
/// Behaviour tests for <see cref="OrderFilledPositionHandler"/> focused on the
/// ADR-0017 §17.7 ContextJson key extension: the handler must honour both
/// <c>maxHoldMinutes</c> (ADR-0016 VWAP-EMA V2) and the legacy <c>maxHoldBars</c>
/// (ADR-0014 pattern scalping) when mapping StrategySignal.ContextJson into
/// Position.MaxHoldDuration. Loop 21 regression: evaluator was emitting
/// <c>maxHoldMinutes</c> but the handler only checked <c>maxHoldBars</c>, so
/// every resulting Position persisted MaxHoldDurationSeconds=NULL and the time
/// stop branch of the monitor never triggered.
/// </summary>
public class OrderFilledPositionHandlerTests
{
    private sealed class FixedClock : IClock
    {
        private readonly DateTimeOffset _now;
        public FixedClock(DateTimeOffset now) => _now = now;
        public DateTimeOffset UtcNow => _now;
        public long BinanceServerTimeMs => _now.ToUnixTimeMilliseconds();
        public long DriftMs => 0;
    }

    // A self-contained InMemory context that does NOT ignore StrategySignal, so
    // the handler can read ContextJson from the Strategies table as in prod.
    internal sealed class TestDbContext : DbContext, IApplicationDbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }

        public DbSet<Instrument> Instruments => Set<Instrument>();
        public DbSet<Kline> Klines => Set<Kline>();
        public DbSet<BookTicker> BookTickers => Set<BookTicker>();
        public DbSet<OrderBookSnapshot> OrderBookSnapshots => Set<OrderBookSnapshot>();
        public DbSet<Order> Orders => Set<Order>();
        public DbSet<OrderFill> OrderFills => Set<OrderFill>();
        public DbSet<Position> Positions => Set<Position>();
        public DbSet<Strategy> Strategies => Set<Strategy>();
        public DbSet<StrategySignal> StrategySignals => Set<StrategySignal>();
        public DbSet<RiskProfile> RiskProfiles => Set<RiskProfile>();
        public DbSet<BacktestRun> BacktestRuns => Set<BacktestRun>();
        public DbSet<BacktestTrade> BacktestTrades => Set<BacktestTrade>();
        public DbSet<SystemEvent> SystemEvents => Set<SystemEvent>();
        public DbSet<VirtualBalance> VirtualBalances => Set<VirtualBalance>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Order>(b =>
            {
                b.HasKey(x => x.Id);
                b.Property(x => x.Symbol).HasConversion(s => s.Value, v => Symbol.From(v));
                b.Property(x => x.Side).HasConversion<int>();
                b.Property(x => x.Type).HasConversion<int>();
                b.Property(x => x.TimeInForce).HasConversion<int>();
                b.Property(x => x.Status).HasConversion<int>();
                b.Property(x => x.Mode).HasConversion<int>();
                b.Ignore(x => x.Fills);
                b.Ignore(x => x.DomainEvents);
            });

            modelBuilder.Entity<Position>(b =>
            {
                b.HasKey(x => x.Id);
                b.Property(x => x.Symbol).HasConversion(s => s.Value, v => Symbol.From(v));
                b.Property(x => x.Side).HasConversion<int>();
                b.Property(x => x.Status).HasConversion<int>();
                b.Property(x => x.Mode).HasConversion<int>();
                b.Ignore(x => x.DomainEvents);
            });

            modelBuilder.Entity<StrategySignal>(b =>
            {
                b.HasKey(x => x.Id);
                b.Property(x => x.Symbol).HasConversion(s => s.Value, v => Symbol.From(v));
                b.Property(x => x.Direction).HasConversion<int>();
            });

            modelBuilder.Ignore<Instrument>();
            modelBuilder.Ignore<Kline>();
            modelBuilder.Ignore<BookTicker>();
            modelBuilder.Ignore<OrderBookSnapshot>();
            modelBuilder.Ignore<OrderFill>();
            modelBuilder.Ignore<Strategy>();
            modelBuilder.Ignore<RiskProfile>();
            modelBuilder.Ignore<BacktestRun>();
            modelBuilder.Ignore<BacktestTrade>();
            modelBuilder.Ignore<SystemEvent>();
            modelBuilder.Ignore<VirtualBalance>();

            base.OnModelCreating(modelBuilder);
        }

        Task<int> IApplicationDbContext.SaveChangesAsync(CancellationToken cancellationToken) =>
            base.SaveChangesAsync(cancellationToken);
    }

    private static (IServiceScopeFactory ScopeFactory, TestDbContext Db) BuildHarness(DateTimeOffset now)
    {
        var dbName = Guid.NewGuid().ToString();
        var sc = new ServiceCollection();
        // IMPORTANT: the dbName is captured outside the lambda so every scope
        // resolves the *same* in-memory store. Otherwise each new scope
        // minted by the handler would see an empty database.
        sc.AddDbContext<TestDbContext>(o => o.UseInMemoryDatabase(dbName));
        sc.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<TestDbContext>());
        sc.AddSingleton<IClock>(new FixedClock(now));
        var sp = sc.BuildServiceProvider();
        return (sp.GetRequiredService<IServiceScopeFactory>(), sp.GetRequiredService<TestDbContext>());
    }

    private static Order SeedFilledOrder(
        TestDbContext db,
        string cid,
        long strategyId,
        decimal qty,
        decimal fillPrice,
        DateTimeOffset now,
        decimal? stopPrice = null,
        decimal? takeProfit = null)
    {
        var order = Order.Place(
            cid, Symbol.From("BTCUSDT"),
            OrderSide.Buy, OrderType.Market, TimeInForce.Ioc,
            qty, price: null, stopPrice: stopPrice,
            strategyId: strategyId, mode: TradingMode.Paper, now: now,
            takeProfit: takeProfit);
        order.RegisterFill(
            exchangeTradeId: 1,
            price: fillPrice,
            quantity: qty,
            commission: 0m, commissionAsset: "USDT",
            filledAt: now);
        db.Orders.Add(order);
        db.SaveChanges();
        return order;
    }

    private static void SeedSignal(TestDbContext db, long strategyId, string contextJson, DateTimeOffset emittedAt)
    {
        var signal = StrategySignal.Emit(
            Symbol.From("BTCUSDT"),
            barOpenTime: emittedAt,
            direction: StrategySignalDirection.Long,
            quantity: 0.001m,
            price: null,
            stopPrice: null,
            contextJson: contextJson,
            emittedAt: emittedAt);

        // Strategy.EmitSignal sets StrategyId via the aggregate's FK relationship;
        // tests bypass the aggregate so we stamp it via reflection.
        typeof(StrategySignal)
            .GetProperty(nameof(StrategySignal.StrategyId))!
            .SetValue(signal, strategyId);

        db.StrategySignals.Add(signal);
        db.SaveChanges();
    }

    [Fact]
    public async Task Fill_ReadsMaxHoldMinutes_FromContextJson_AndMapsToPositionDuration()
    {
        var now = DateTimeOffset.UtcNow;
        var (scopeFactory, db) = BuildHarness(now);

        SeedFilledOrder(db, cid: "sig-1-100-p", strategyId: 1,
            qty: 0.001m, fillPrice: 30000m, now: now);
        SeedSignal(db, strategyId: 1,
            contextJson: "{\"maxHoldMinutes\":10}",
            emittedAt: now.AddSeconds(-5));

        var sut = new OrderFilledPositionHandler(
            scopeFactory,
            new FixedClock(now),
            NullLogger<OrderFilledPositionHandler>.Instance);

        await sut.Handle(new OrderFilledEvent(
            "sig-1-100-p", "BTCUSDT", 0.001m, 30m, TradingMode.Paper),
            CancellationToken.None);

        db.Positions.Should().ContainSingle()
            .Which.MaxHoldDuration.Should().Be(TimeSpan.FromMinutes(10));
    }

    [Fact]
    public async Task Fill_FallsBackToMaxHoldBars_WhenMaxHoldMinutesAbsent()
    {
        var now = DateTimeOffset.UtcNow;
        var (scopeFactory, db) = BuildHarness(now);

        SeedFilledOrder(db, "sig-2-100-p", strategyId: 2,
            0.001m, 30000m, now);
        SeedSignal(db, strategyId: 2,
            contextJson: "{\"maxHoldBars\":15}",
            emittedAt: now.AddSeconds(-5));

        var sut = new OrderFilledPositionHandler(
            scopeFactory, new FixedClock(now),
            NullLogger<OrderFilledPositionHandler>.Instance);

        await sut.Handle(new OrderFilledEvent(
            "sig-2-100-p", "BTCUSDT", 0.001m, 30m, TradingMode.Paper),
            CancellationToken.None);

        // 1 bar = 1 minute (BinanceBot 1m bar convention).
        db.Positions.Single().MaxHoldDuration.Should().Be(TimeSpan.FromMinutes(15));
    }

    [Fact]
    public async Task Fill_PrefersMaxHoldMinutes_WhenBothKeysPresent()
    {
        var now = DateTimeOffset.UtcNow;
        var (scopeFactory, db) = BuildHarness(now);

        SeedFilledOrder(db, "sig-3-100-p", 3, 0.001m, 30000m, now);
        SeedSignal(db, strategyId: 3,
            contextJson: "{\"maxHoldMinutes\":10,\"maxHoldBars\":15}",
            emittedAt: now.AddSeconds(-5));

        var sut = new OrderFilledPositionHandler(
            scopeFactory, new FixedClock(now),
            NullLogger<OrderFilledPositionHandler>.Instance);

        await sut.Handle(new OrderFilledEvent(
            "sig-3-100-p", "BTCUSDT", 0.001m, 30m, TradingMode.Paper),
            CancellationToken.None);

        // Preference is for the VWAP-EMA-style minutes field.
        db.Positions.Single().MaxHoldDuration.Should().Be(TimeSpan.FromMinutes(10));
    }

    [Fact]
    public async Task Fill_LeavesMaxHoldDurationNull_WhenNeitherKeyPresent()
    {
        var now = DateTimeOffset.UtcNow;
        var (scopeFactory, db) = BuildHarness(now);

        SeedFilledOrder(db, "sig-4-100-p", 4, 0.001m, 30000m, now);
        SeedSignal(db, strategyId: 4,
            contextJson: "{\"type\":\"vwap-ema-hybrid-v2\"}",
            emittedAt: now.AddSeconds(-5));

        var sut = new OrderFilledPositionHandler(
            scopeFactory, new FixedClock(now),
            NullLogger<OrderFilledPositionHandler>.Instance);

        await sut.Handle(new OrderFilledEvent(
            "sig-4-100-p", "BTCUSDT", 0.001m, 30m, TradingMode.Paper),
            CancellationToken.None);

        db.Positions.Single().MaxHoldDuration.Should().BeNull();
    }

    [Fact]
    public async Task Fill_HandlesInvalidJson_Gracefully_MaxHoldStaysNull()
    {
        var now = DateTimeOffset.UtcNow;
        var (scopeFactory, db) = BuildHarness(now);

        SeedFilledOrder(db, "sig-5-100-p", 5, 0.001m, 30000m, now);
        SeedSignal(db, strategyId: 5,
            contextJson: "not-json",
            emittedAt: now.AddSeconds(-5));

        var sut = new OrderFilledPositionHandler(
            scopeFactory, new FixedClock(now),
            NullLogger<OrderFilledPositionHandler>.Instance);

        await sut.Handle(new OrderFilledEvent(
            "sig-5-100-p", "BTCUSDT", 0.001m, 30m, TradingMode.Paper),
            CancellationToken.None);

        db.Positions.Single().MaxHoldDuration.Should().BeNull();
    }

    [Fact]
    public async Task Fill_SkipsNonPositiveMaxHold_AndLeavesDurationNull()
    {
        var now = DateTimeOffset.UtcNow;
        var (scopeFactory, db) = BuildHarness(now);

        SeedFilledOrder(db, "sig-6-100-p", 6, 0.001m, 30000m, now);
        SeedSignal(db, strategyId: 6,
            contextJson: "{\"maxHoldMinutes\":0}",
            emittedAt: now.AddSeconds(-5));

        var sut = new OrderFilledPositionHandler(
            scopeFactory, new FixedClock(now),
            NullLogger<OrderFilledPositionHandler>.Instance);

        await sut.Handle(new OrderFilledEvent(
            "sig-6-100-p", "BTCUSDT", 0.001m, 30m, TradingMode.Paper),
            CancellationToken.None);

        db.Positions.Single().MaxHoldDuration.Should().BeNull();
    }
}
