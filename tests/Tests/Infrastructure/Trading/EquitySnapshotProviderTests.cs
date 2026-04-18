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
/// Loop 14/15 fix — <see cref="EquitySnapshotProvider.GetRealizedEquityAsync"/>
/// must return realized notional equity = cash + cost-basis of currently
/// open positions, NOT cash alone. Cash-only false-tripped the drawdown
/// circuit-breaker the moment any position opened (Loop 14 t30 trace:
/// $100 -> BUY $39.97 -> $60.03 cash -> 40% sahte DD -> CB Tripped).
///
/// Loop 12 invariant preserved: cost-basis uses
/// <see cref="Position.AverageEntryPrice"/>, never <c>MarkPrice</c>, so
/// unrealized PnL is still excluded from peak-tracker input.
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

    private static VirtualBalance SeedPaperBalance(StubDbContext db, decimal cash)
    {
        var vb = VirtualBalance.CreateDefault(TradingMode.Paper, cash, T0);
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
    public async Task GetRealizedEquityAsync_NoOpenPositions_ReturnsCashOnly()
    {
        var db = NewDb();
        SeedPaperBalance(db, cash: 100m);
        var sut = new EquitySnapshotProvider(db);

        var realized = await sut.GetRealizedEquityAsync(TradingMode.Paper, CancellationToken.None);

        realized.Should().Be(100m);
    }

    /// <summary>
    /// Loop 14 t30 replay — the exact false-DD scenario.
    /// Start: $100 cash. BUY XRP $39.97 -> cash $60.03, cost-basis $39.97
    /// locked in the open position. Realized equity must still be $100
    /// (no trade has closed, no PnL realized).
    /// </summary>
    [Fact]
    public async Task GetRealizedEquityAsync_OneLongPosition_AddsCostBasisToCash()
    {
        var db = NewDb();
        SeedPaperBalance(db, cash: 60.03m);
        // Cost basis = 0.5m XRP * $79.94 = $39.97
        OpenPosition(db, "XRPUSDT", PositionSide.Long, qty: 0.5m, entryPrice: 79.94m);
        var sut = new EquitySnapshotProvider(db);

        var realized = await sut.GetRealizedEquityAsync(TradingMode.Paper, CancellationToken.None);

        realized.Should().Be(100m);
    }

    /// <summary>
    /// Short positions also lock cost-basis (margin reservation in spot, or
    /// notional in derivatives). Cost-basis = AverageEntryPrice * Quantity
    /// regardless of side — the realized notional equity is symmetric.
    /// </summary>
    [Fact]
    public async Task GetRealizedEquityAsync_ShortPosition_AddsCostBasisToCash()
    {
        var db = NewDb();
        SeedPaperBalance(db, cash: 70m);
        // Short 0.001 BTC @ $30,000 -> cost basis $30
        OpenPosition(db, "BTCUSDT", PositionSide.Short, qty: 0.001m, entryPrice: 30_000m);
        var sut = new EquitySnapshotProvider(db);

        var realized = await sut.GetRealizedEquityAsync(TradingMode.Paper, CancellationToken.None);

        realized.Should().Be(100m);
    }

    /// <summary>
    /// Multiple open positions sum their cost-basis. Closed positions are
    /// excluded — they have already settled into <c>CurrentBalance</c>.
    /// </summary>
    [Fact]
    public async Task GetRealizedEquityAsync_MultipleOpen_SumsCostBasis_ExcludesClosed()
    {
        var db = NewDb();
        SeedPaperBalance(db, cash: 20m);
        OpenPosition(db, "BTCUSDT", PositionSide.Long, qty: 0.001m, entryPrice: 30_000m); // $30
        OpenPosition(db, "ETHUSDT", PositionSide.Long, qty: 0.02m, entryPrice: 2_500m);   // $50
        var closed = OpenPosition(db, "BNBUSDT", PositionSide.Long, qty: 1m, entryPrice: 500m);
        closed.Close(exitPrice: 510m, reason: "test", now: T0.AddMinutes(5));
        db.SaveChanges();

        var sut = new EquitySnapshotProvider(db);

        var realized = await sut.GetRealizedEquityAsync(TradingMode.Paper, CancellationToken.None);

        // 20 cash + 30 BTC + 50 ETH = 100. Closed BNB pos is ignored
        // (its realized PnL would already be reflected in cash by ApplyFill).
        realized.Should().Be(100m);
    }

    /// <summary>
    /// Loop 12 invariant — unrealized PnL must NOT contaminate the realized
    /// read. After MarkToMarket pushes UnrealizedPnl far above zero, the
    /// realized equity is still cash + cost-basis (entry price), unchanged.
    /// </summary>
    [Fact]
    public async Task GetRealizedEquityAsync_UnrealizedPump_DoesNotInflateRealized()
    {
        var db = NewDb();
        SeedPaperBalance(db, cash: 60m);
        var pos = OpenPosition(db, "XRPUSDT", PositionSide.Long, qty: 0.5m, entryPrice: 80m);
        // Mark price doubles -> UnrealizedPnl jumps, but cost-basis is still 80*0.5=40.
        pos.MarkToMarket(markPrice: 160m, now: T0.AddMinutes(1));
        db.SaveChanges();

        var sut = new EquitySnapshotProvider(db);

        var realized = await sut.GetRealizedEquityAsync(TradingMode.Paper, CancellationToken.None);

        realized.Should().Be(100m); // 60 cash + 40 cost-basis, unrealized excluded
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
