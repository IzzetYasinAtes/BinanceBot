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
                b.Property(x => x.Direction).HasConversion<int>();
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

            // ADR-0020 §20.7 — handler now sums OrderFills for quote-denominated
            // commission aggregation, so the entity must be mapped even though
            // the tests below don't stage fills directly.
            modelBuilder.Entity<OrderFill>(b =>
            {
                b.HasKey(x => x.Id);
            });

            // Loop 94 Fix #3: VirtualBalance handler içinde Paper margin akışı için
            // sorgulanır (AllocateMarginForPosition / ReturnMarginAndApplyPnl). Test
            // harness InMemory'de map edilir; null seed'lenmediğinde handler defansif
            // log + skip yapar — testler margin assertion içermediği için davranış
            // saklı kalır.
            modelBuilder.Entity<VirtualBalance>(b =>
            {
                b.HasKey(x => x.Id);
                b.Property(x => x.Mode).HasConversion<int>();
                b.Ignore(x => x.DomainEvents);
            });

            modelBuilder.Ignore<Instrument>();
            modelBuilder.Ignore<Kline>();
            modelBuilder.Ignore<BookTicker>();
            modelBuilder.Ignore<OrderBookSnapshot>();
            modelBuilder.Ignore<Strategy>();
            modelBuilder.Ignore<RiskProfile>();
            modelBuilder.Ignore<BacktestRun>();
            modelBuilder.Ignore<BacktestTrade>();
            modelBuilder.Ignore<SystemEvent>();

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
        decimal? takeProfit = null,
        decimal commission = 0m,
        OrderSide side = OrderSide.Buy,
        string symbol = "BTCUSDT")
    {
        var order = Order.Place(
            cid, Symbol.From(symbol),
            side, OrderType.Market, TimeInForce.Ioc,
            qty, price: null, stopPrice: stopPrice,
            strategyId: strategyId, mode: TradingMode.Paper, now: now,
            takeProfit: takeProfit);
        order.RegisterFill(
            exchangeTradeId: 1,
            price: fillPrice,
            quantity: qty,
            commission: commission, commissionAsset: "USDT",
            filledAt: now);
        db.Orders.Add(order);
        db.SaveChanges();

        // Mirror the fill into the OrderFill table directly. Production EF
        // configuration owns this through the Order aggregate's Fills collection,
        // but TestDbContext maps OrderFill as a free-standing entity so the
        // handler's <c>db.OrderFills.Where(f => f.OrderId == order.Id)</c> read
        // can resolve. The Order aggregate keeps its <c>Fills</c> collection
        // private (DDD encapsulation); we use the public <c>Order.Fills</c>
        // accessor and reflection to set the FK + reset the synthetic Id so
        // the InMemory provider assigns a fresh primary key.
        if (commission > 0m)
        {
            var fill = order.Fills.Last();
            var idSetter = typeof(BinanceBot.Domain.Common.Entity<long>)
                .GetProperty(nameof(OrderFill.Id))!
                .GetSetMethod(nonPublic: true)!;
            idSetter.Invoke(fill, new object[] { 0L });

            var fkSetter = typeof(OrderFill)
                .GetProperty(nameof(OrderFill.OrderId))!
                .GetSetMethod(nonPublic: true)!;
            fkSetter.Invoke(fill, new object[] { order.Id });

            db.OrderFills.Add(fill);
            db.SaveChanges();
        }

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

    /// <summary>
    /// Loop 93 §1 regression — Position.EntryCommission must equal the
    /// quote-denominated <c>OrderFill.Commission</c> directly, NOT
    /// <c>commission × price</c>. Loop 92 t30 dump observed
    /// <c>Position.EntryCommission = $117.29</c> for an ETH BUY where the
    /// underlying <c>OrderFill.Commission = $0.0508</c>: the Spot-era
    /// multiplier (treating BUY commission as base-asset and converting to
    /// quote via × price) survived the Futures pivot. Futures contracts
    /// settle commission in QUOTE asset for both legs, so the multiplier is
    /// always wrong. This test pins the exact aggregation across the three
    /// open positions captured at halt: BTC short, ADA long, ETH long.
    /// </summary>
    [Theory]
    [InlineData("ETHUSDT", "Buy", 0.044, 2309.03, 0.0508)]
    [InlineData("ADAUSDT", "Buy", 403.0, 0.2483, 0.0500)]
    [InlineData("BTCUSDT", "Sell", 0.0013, 78315.32, 0.0509)]
    public async Task Fill_EntryCommission_EqualsRawOrderFillCommission_NoPriceMultiplier(
        string symbol, string sideText, double qtyD, double priceD, double commissionD)
    {
        var qty = (decimal)qtyD;
        var price = (decimal)priceD;
        var commission = (decimal)commissionD;
        var side = Enum.Parse<OrderSide>(sideText);
        var now = DateTimeOffset.UtcNow;
        var (scopeFactory, db) = BuildHarness(now);

        var cid = $"loop93-{symbol}-{sideText}".ToLowerInvariant();
        SeedFilledOrder(db, cid, strategyId: 100, qty: qty, fillPrice: price,
            now: now, commission: commission, side: side, symbol: symbol);

        var sut = new OrderFilledPositionHandler(
            scopeFactory, new FixedClock(now),
            NullLogger<OrderFilledPositionHandler>.Instance);

        await sut.Handle(new OrderFilledEvent(
            cid, symbol, qty, qty * price, TradingMode.Paper),
            CancellationToken.None);

        var pos = db.Positions.Single(p => p.Symbol == Symbol.From(symbol));
        // Aggregate must echo the raw fill fee (USDT, no price multiplier).
        pos.EntryCommission.Should().Be(commission);
        // Direction follows OrderSide: BUY → Long, SELL → Short.
        pos.Direction.Should().Be(side == OrderSide.Buy
            ? TradeDirection.Long
            : TradeDirection.Short);
    }

    /// <summary>
    /// Loop 94 Fix #3 — open Long pozisyonda VirtualBalance.AllocateMarginForPosition
    /// çağrılmalı. notional = fillPrice × qty, AllocatedMargin += notional. Wallet
    /// dokunulmaz (PlaceOrderCommandHandler ApplyFill ile fee zaten düşmüş kabul).
    /// </summary>
    [Fact]
    public async Task PaperLongOpen_AllocatesMargin_OnVirtualBalance()
    {
        var now = DateTimeOffset.UtcNow;
        var (scopeFactory, db) = BuildHarness(now);

        // Paper VirtualBalance seed: $500 wallet, 0 margin.
        var vb = VirtualBalance.CreateDefault(TradingMode.Paper, 500m, now);
        db.VirtualBalances.Add(vb);
        db.SaveChanges();

        SeedFilledOrder(db, cid: "open-long-margin", strategyId: 7,
            qty: 0.01m, fillPrice: 30000m, now: now, side: OrderSide.Buy);

        var sut = new OrderFilledPositionHandler(
            scopeFactory, new FixedClock(now),
            NullLogger<OrderFilledPositionHandler>.Instance);

        await sut.Handle(new OrderFilledEvent(
            "open-long-margin", "BTCUSDT", 0.01m, 300m, TradingMode.Paper),
            CancellationToken.None);

        var paper = db.VirtualBalances.AsNoTracking().Single();
        paper.AllocatedMargin.Should().Be(300m, "fillPrice × qty = 30000 × 0.01");
        // Wallet test bağlamında ApplyFill çağrılmadı — sadece margin akışı doğrulanır.
        paper.WalletBalance.Should().Be(500m);

        var pos = db.Positions.Single();
        pos.Direction.Should().Be(TradeDirection.Long);
    }

    /// <summary>
    /// Loop 94 Fix #3 — open Short pozisyonda da margin allocate edilir (Futures
    /// SELL leg de margin posts; Spot semantik "satıştan cash kazandın" YANLIŞ).
    /// </summary>
    [Fact]
    public async Task PaperShortOpen_AllocatesMargin_OnVirtualBalance()
    {
        var now = DateTimeOffset.UtcNow;
        var (scopeFactory, db) = BuildHarness(now);

        var vb = VirtualBalance.CreateDefault(TradingMode.Paper, 500m, now);
        db.VirtualBalances.Add(vb);
        db.SaveChanges();

        SeedFilledOrder(db, cid: "open-short-margin", strategyId: 8,
            qty: 0.01m, fillPrice: 30000m, now: now, side: OrderSide.Sell);

        var sut = new OrderFilledPositionHandler(
            scopeFactory, new FixedClock(now),
            NullLogger<OrderFilledPositionHandler>.Instance);

        await sut.Handle(new OrderFilledEvent(
            "open-short-margin", "BTCUSDT", 0.01m, 300m, TradingMode.Paper),
            CancellationToken.None);

        var paper = db.VirtualBalances.AsNoTracking().Single();
        paper.AllocatedMargin.Should().Be(300m, "Short open de notional margin'e gider");
        paper.WalletBalance.Should().Be(500m);

        var pos = db.Positions.Single();
        pos.Direction.Should().Be(TradeDirection.Short);
    }

    /// <summary>
    /// Loop 94 Fix #3 — close path. Long pozisyon profitle kapandığında
    /// ReturnMarginAndApplyPnl(originalMargin, gross) → wallet += gross,
    /// AllocatedMargin -= originalMargin. Profit/loss net hesabı: entry/exit fee
    /// PlaceOrderCommandHandler.ApplyFill ile düşülür (bu testte simüle edilmiyor),
    /// gross PnL wallet'a doğrudan yansır.
    /// </summary>
    [Fact]
    public async Task PaperLongClose_ReturnsMarginAndAppliesGrossPnl()
    {
        var now = DateTimeOffset.UtcNow;
        var (scopeFactory, db) = BuildHarness(now);

        // VB: 500 wallet, 300 margin (önceki long open simülasyonu).
        var vb = VirtualBalance.CreateDefault(TradingMode.Paper, 500m, now);
        vb.AllocateMarginForPosition(300m, now);
        db.VirtualBalances.Add(vb);

        // Açık Long pozisyon seed: entry $30000, qty 0.01 → margin $300.
        var openPos = Position.Open(
            Symbol.From("BTCUSDT"), TradeDirection.Long, 0.01m, 30000m,
            stopPrice: null, strategyId: 9, mode: TradingMode.Paper, now: now,
            entryCommission: 0m);
        // Reflection ile Id ata (InMemory provider auto generate ediyor ama eski entry zaten persist edilmiş kabul).
        db.Positions.Add(openPos);
        db.SaveChanges();

        // Reverse SELL fill @ $30500 → gross = (30500 - 30000) × 0.01 = +$5.
        SeedFilledOrder(db, cid: "close-long-pnl", strategyId: 9,
            qty: 0.01m, fillPrice: 30500m, now: now.AddMinutes(5), side: OrderSide.Sell);

        var sut = new OrderFilledPositionHandler(
            scopeFactory, new FixedClock(now.AddMinutes(5)),
            NullLogger<OrderFilledPositionHandler>.Instance);

        await sut.Handle(new OrderFilledEvent(
            "close-long-pnl", "BTCUSDT", 0.01m, 305m, TradingMode.Paper),
            CancellationToken.None);

        var paper = db.VirtualBalances.AsNoTracking().Single();
        paper.AllocatedMargin.Should().Be(0m, "close ile original margin geri döndü");
        paper.WalletBalance.Should().Be(505m, "wallet += gross PnL (500 + 5)");

        var closed = db.Positions.AsNoTracking().Single();
        closed.Status.Should().Be(PositionStatus.Closed);
        // Position.RealizedPnl = gross - fees. Fee 0 → RealizedPnl = 5.
        closed.RealizedPnl.Should().Be(5m);
    }

    /// <summary>
    /// Loop 94 Fix #3 — Short close negatif PnL ile. Short entry $30000, exit
    /// $30200 ⇒ gross = (30000 - 30200) × 0.01 = -$2 (zarar). Wallet -= 2,
    /// margin geri döner.
    /// </summary>
    [Fact]
    public async Task PaperShortClose_LossPnl_ReturnsMarginAndDeductsFromWallet()
    {
        var now = DateTimeOffset.UtcNow;
        var (scopeFactory, db) = BuildHarness(now);

        var vb = VirtualBalance.CreateDefault(TradingMode.Paper, 500m, now);
        vb.AllocateMarginForPosition(300m, now);
        db.VirtualBalances.Add(vb);

        var openPos = Position.Open(
            Symbol.From("BTCUSDT"), TradeDirection.Short, 0.01m, 30000m,
            stopPrice: null, strategyId: 10, mode: TradingMode.Paper, now: now,
            entryCommission: 0m);
        db.Positions.Add(openPos);
        db.SaveChanges();

        // Reverse BUY fill @ $30200 → gross = (30000 - 30200) × 0.01 = -$2.
        SeedFilledOrder(db, cid: "close-short-loss", strategyId: 10,
            qty: 0.01m, fillPrice: 30200m, now: now.AddMinutes(5), side: OrderSide.Buy);

        var sut = new OrderFilledPositionHandler(
            scopeFactory, new FixedClock(now.AddMinutes(5)),
            NullLogger<OrderFilledPositionHandler>.Instance);

        await sut.Handle(new OrderFilledEvent(
            "close-short-loss", "BTCUSDT", 0.01m, 302m, TradingMode.Paper),
            CancellationToken.None);

        var paper = db.VirtualBalances.AsNoTracking().Single();
        paper.AllocatedMargin.Should().Be(0m);
        paper.WalletBalance.Should().Be(498m, "wallet += -2 (gross loss)");

        var closed = db.Positions.AsNoTracking().Single();
        closed.Status.Should().Be(PositionStatus.Closed);
        closed.RealizedPnl.Should().Be(-2m);
    }
}
