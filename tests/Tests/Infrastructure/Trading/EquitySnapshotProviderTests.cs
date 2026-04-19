using BinanceBot.Domain.Balances;
using BinanceBot.Domain.Common;
using BinanceBot.Domain.Positions;
using BinanceBot.Domain.ValueObjects;
using BinanceBot.Infrastructure.Trading;
using BinanceBot.Tests.Infrastructure.Strategies;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BinanceBot.Tests.Infrastructure.Trading;

/// <summary>
/// Loop 17 reform — <see cref="EquitySnapshotProvider.GetRealizedEquityAsync"/>
/// returns PnL-based realized equity: <c>StartingBalance + sum(closed
/// position RealizedPnl)</c>.
///
/// Previous formulations (cash, then cash + open cost-basis) drifted upward
/// across loops because partial fills, fee accruals, and AddFill averaging
/// raced cash mutations. Loop 15 cash+cost reached $178, Loop 16 hit $263 —
/// inflating PeakEquity and arming a permanent fake drawdown.
///
/// PnL-based tracking is timing-immune:
///   - <c>StartingBalance</c> is constant per iteration (only <c>ResetForIteration</c>
///     mutates it, and that resets the iteration).
///   - <c>RealizedPnl</c> is written exactly once on <c>Position.Close</c>.
///
/// Therefore PeakEquity can never exceed <c>StartingBalance + true realized
/// gains</c>, and drawdown only fires on real settled losses.
/// </summary>
public class EquitySnapshotProviderTests
{
    private static readonly DateTimeOffset T0 = new(2026, 4, 17, 12, 0, 0, TimeSpan.Zero);

    private static StubDbContext NewDb()
    {
        var opts = new DbContextOptionsBuilder<StubDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new StubDbContext(opts);
    }

    private static VirtualBalance SeedPaperBalance(StubDbContext db, decimal startingBalance)
    {
        var vb = VirtualBalance.CreateDefault(TradingMode.Paper, startingBalance, T0);
        db.VirtualBalances.Add(vb);
        db.SaveChanges();
        return vb;
    }

    private static Position OpenPosition(
        StubDbContext db,
        string symbol,
        PositionSide side,
        decimal qty,
        decimal entryPrice)
    {
        var pos = Position.Open(
            Symbol.From(symbol),
            side,
            qty,
            entryPrice,
            stopPrice: null,
            strategyId: null,
            mode: TradingMode.Paper,
            now: T0);
        db.Positions.Add(pos);
        db.SaveChanges();
        return pos;
    }

    [Fact]
    public async Task GetRealizedEquityAsync_NoPositions_ReturnsStartingBalance()
    {
        var db = NewDb();
        SeedPaperBalance(db, startingBalance: 100m);
        var sut = new EquitySnapshotProvider(db);

        var realized = await sut.GetRealizedEquityAsync(TradingMode.Paper, CancellationToken.None);

        realized.Should().Be(100m);
    }

    /// <summary>
    /// Loop 17 invariant — open positions (any cost-basis, any mark price)
    /// must NOT move the realized equity. Only closed PnL counts.
    /// Loop 14 t30 replay: BUY $39.97, position open, equity proxy stays
    /// exactly at the $100 baseline (no drawdown trigger).
    /// </summary>
    [Fact]
    public async Task GetRealizedEquityAsync_OpenPositionOnly_ReturnsStartingBalance()
    {
        var db = NewDb();
        SeedPaperBalance(db, startingBalance: 100m);
        OpenPosition(db, "XRPUSDT", PositionSide.Long, qty: 0.5m, entryPrice: 79.94m);
        var sut = new EquitySnapshotProvider(db);

        var realized = await sut.GetRealizedEquityAsync(TradingMode.Paper, CancellationToken.None);

        realized.Should().Be(100m);
    }

    /// <summary>
    /// Loop 17 invariant — even an extreme mark-price pump on an open
    /// position cannot inflate realized equity. This is the property that
    /// kept PeakEquity drifting in Loops 15-16; here it is provably bounded.
    /// </summary>
    [Fact]
    public async Task GetRealizedEquityAsync_OpenPositionMarkPump_DoesNotInflate()
    {
        var db = NewDb();
        SeedPaperBalance(db, startingBalance: 100m);
        var pos = OpenPosition(db, "XRPUSDT", PositionSide.Long, qty: 0.5m, entryPrice: 80m);
        // Mark price 10x — UnrealizedPnl explodes, realized must stay at 100.
        pos.MarkToMarket(markPrice: 800m, now: T0.AddMinutes(1));
        db.SaveChanges();

        var sut = new EquitySnapshotProvider(db);

        var realized = await sut.GetRealizedEquityAsync(TradingMode.Paper, CancellationToken.None);

        realized.Should().Be(100m);
    }

    /// <summary>
    /// Closed positions sum their RealizedPnl into the equity proxy.
    /// Long winner +$5 + short loser -$2 = baseline + $3.
    /// </summary>
    [Fact]
    public async Task GetRealizedEquityAsync_ClosedPositions_SumRealizedPnl()
    {
        var db = NewDb();
        SeedPaperBalance(db, startingBalance: 100m);

        var winner = OpenPosition(db, "BTCUSDT", PositionSide.Long, qty: 0.001m, entryPrice: 30_000m);
        winner.Close(exitPrice: 35_000m, reason: "tp", now: T0.AddMinutes(5)); // +$5

        var loser = OpenPosition(db, "ETHUSDT", PositionSide.Short, qty: 0.02m, entryPrice: 2_500m);
        loser.Close(exitPrice: 2_600m, reason: "sl", now: T0.AddMinutes(10)); // -$2

        db.SaveChanges();

        var sut = new EquitySnapshotProvider(db);

        var realized = await sut.GetRealizedEquityAsync(TradingMode.Paper, CancellationToken.None);

        realized.Should().Be(103m); // 100 baseline + 5 - 2
    }

    /// <summary>
    /// Mixed portfolio: open positions ignored entirely, only the closed
    /// PnL contributes. Loop 17 acceptance — PeakEquity input has a hard
    /// upper bound regardless of how many positions are mid-flight.
    /// </summary>
    [Fact]
    public async Task GetRealizedEquityAsync_OpenAndClosed_OnlyClosedCount()
    {
        var db = NewDb();
        SeedPaperBalance(db, startingBalance: 100m);

        // Two open positions (mark pumped) — must contribute zero.
        var open1 = OpenPosition(db, "BTCUSDT", PositionSide.Long, qty: 0.001m, entryPrice: 30_000m);
        open1.MarkToMarket(markPrice: 60_000m, now: T0.AddMinutes(1));
        var open2 = OpenPosition(db, "ETHUSDT", PositionSide.Long, qty: 0.02m, entryPrice: 2_500m);
        open2.MarkToMarket(markPrice: 3_000m, now: T0.AddMinutes(1));

        // One closed loss -$1.50.
        var closedLoss = OpenPosition(db, "BNBUSDT", PositionSide.Long, qty: 0.1m, entryPrice: 500m);
        closedLoss.Close(exitPrice: 485m, reason: "sl", now: T0.AddMinutes(5)); // -$1.50

        db.SaveChanges();

        var sut = new EquitySnapshotProvider(db);

        var realized = await sut.GetRealizedEquityAsync(TradingMode.Paper, CancellationToken.None);

        realized.Should().Be(98.5m); // 100 baseline + (-1.50) realized; opens excluded
    }

    [Fact]
    public async Task GetRealizedEquityAsync_LiveMainnet_ReturnsZero()
    {
        var db = NewDb();
        var sut = new EquitySnapshotProvider(db);

        var realized = await sut.GetRealizedEquityAsync(TradingMode.LiveMainnet, CancellationToken.None);

        realized.Should().Be(0m);
    }

    [Fact]
    public async Task GetRealizedEquityAsync_NoBalanceRow_ReturnsZero()
    {
        var db = NewDb();
        var sut = new EquitySnapshotProvider(db);

        var realized = await sut.GetRealizedEquityAsync(TradingMode.Paper, CancellationToken.None);

        realized.Should().Be(0m);
    }
}
