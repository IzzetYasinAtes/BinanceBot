using BinanceBot.Application.Abstractions;
using BinanceBot.Application.Abstractions.Trading;
using BinanceBot.Domain.Common;
using BinanceBot.Domain.RiskProfiles;
using BinanceBot.Infrastructure.Risk;
using BinanceBot.Tests.Infrastructure.Strategies;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace BinanceBot.Tests.Infrastructure.Risk;

/// <summary>
/// Loop 7 bug #17 — <see cref="EquityPeakTrackerService"/> walks every non-Mainnet
/// <see cref="TradingMode"/> on a tick, asks <see cref="IEquitySnapshotProvider"/>
/// for the live equity, and ratchets <see cref="RiskProfile.PeakEquity"/> upward.
/// LiveMainnet is skipped (provider returns 0); zero-equity ticks are no-ops.
/// </summary>
public class EquityPeakTrackerServiceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 4, 17, 12, 0, 0, TimeSpan.Zero);

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; }
        public long BinanceServerTimeMs => UtcNow.ToUnixTimeMilliseconds();
        public long DriftMs => 0;
        public FixedClock(DateTimeOffset now) => UtcNow = now;
    }

    private static StubDbContext NewDb()
    {
        var opts = new DbContextOptionsBuilder<StubDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new StubDbContext(opts);
    }

    private static (EquityPeakTrackerService Svc, Mock<IEquitySnapshotProvider> Equity, StubDbContext Db)
        BuildHarness(Action<Mock<IEquitySnapshotProvider>>? setup = null)
    {
        var db = NewDb();
        // Seed all three RiskProfile rows (mirrors RiskProfileSeeder boot).
        db.RiskProfiles.Add(RiskProfile.CreateDefault(TradingMode.Paper, T0));
        db.RiskProfiles.Add(RiskProfile.CreateDefault(TradingMode.LiveTestnet, T0));
        db.RiskProfiles.Add(RiskProfile.CreateDefault(TradingMode.LiveMainnet, T0));
        db.SaveChanges();

        var equity = new Mock<IEquitySnapshotProvider>(MockBehavior.Strict);
        setup?.Invoke(equity);

        var sc = new ServiceCollection();
        sc.AddSingleton<IApplicationDbContext>(db);
        sc.AddSingleton(equity.Object);
        sc.AddSingleton<IClock>(new FixedClock(T0.AddMinutes(30)));
        var sp = sc.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();

        var svc = new EquityPeakTrackerService(
            scopeFactory,
            NullLogger<EquityPeakTrackerService>.Instance);
        return (svc, equity, db);
    }

    [Fact]
    public async Task Tick_PaperEquityAboveZero_RatchetsPeakEquity()
    {
        // Arrange: Loop 6 t30 spike to $195 — no trade closed but equity surged.
        var (svc, equity, db) = BuildHarness(e =>
        {
            e.Setup(x => x.GetEquityAsync(TradingMode.Paper, It.IsAny<CancellationToken>()))
                .ReturnsAsync(195.25m);
            e.Setup(x => x.GetEquityAsync(TradingMode.LiveTestnet, It.IsAny<CancellationToken>()))
                .ReturnsAsync(0m);
        });

        // Act
        await svc.TickOnceAsync(CancellationToken.None);

        // Assert
        var paper = await db.RiskProfiles.AsNoTracking()
            .FirstAsync(r => r.Id == (int)TradingMode.Paper);
        paper.PeakEquity.Should().Be(195.25m);
        paper.CurrentDrawdownPct.Should().Be(0m);
        equity.Verify(e => e.GetEquityAsync(TradingMode.Paper, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Tick_EquityBelowExistingPeak_RebaseDrawdown_DoesNotLowerPeak()
    {
        var (svc, _, db) = BuildHarness(e =>
        {
            // First tick will record a peak of $195, second will drop to $56.10.
            var seq = e.SetupSequence(x =>
                x.GetEquityAsync(TradingMode.Paper, It.IsAny<CancellationToken>()));
            seq.ReturnsAsync(195.25m).ReturnsAsync(56.10m);

            e.Setup(x => x.GetEquityAsync(TradingMode.LiveTestnet, It.IsAny<CancellationToken>()))
                .ReturnsAsync(0m);
        });

        await svc.TickOnceAsync(CancellationToken.None); // peak = 195.25
        await svc.TickOnceAsync(CancellationToken.None); // unwind to 56.10

        var paper = await db.RiskProfiles.AsNoTracking()
            .FirstAsync(r => r.Id == (int)TradingMode.Paper);
        paper.PeakEquity.Should().Be(195.25m);
        paper.CurrentDrawdownPct.Should().BeApproximately(0.7128m, 0.001m);
    }

    [Fact]
    public async Task Tick_LiveMainnetIsNeverQueried()
    {
        var (svc, equity, _) = BuildHarness(e =>
        {
            e.Setup(x => x.GetEquityAsync(TradingMode.Paper, It.IsAny<CancellationToken>()))
                .ReturnsAsync(100m);
            e.Setup(x => x.GetEquityAsync(TradingMode.LiveTestnet, It.IsAny<CancellationToken>()))
                .ReturnsAsync(0m);
        });

        await svc.TickOnceAsync(CancellationToken.None);

        equity.Verify(
            x => x.GetEquityAsync(TradingMode.LiveMainnet, It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Tick_ZeroEquity_DoesNotTouchPeak()
    {
        var (svc, _, db) = BuildHarness(e =>
        {
            e.Setup(x => x.GetEquityAsync(It.IsAny<TradingMode>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(0m);
        });

        await svc.TickOnceAsync(CancellationToken.None);

        var paper = await db.RiskProfiles.AsNoTracking()
            .FirstAsync(r => r.Id == (int)TradingMode.Paper);
        paper.PeakEquity.Should().Be(0m);
        paper.CurrentDrawdownPct.Should().Be(0m);
    }

    /// <summary>
    /// Loop 8 bug #19 — sequel: drawdown trip evaluation is now part of
    /// RecordPeakEquitySnapshot, so an intraday equity slide that crosses the
    /// 24h ceiling MUST flip the CB to Tripped during a tracker tick. Prior
    /// version of this test asserted the opposite (Loop 7 contract);
    /// rewritten to lock in the fixed behaviour.
    /// </summary>
    [Fact]
    public async Task Tick_DrawdownAcross24hCeiling_TripsCircuitBreaker()
    {
        var (svc, _, db) = BuildHarness(e =>
        {
            // Peak ratchets to 100, then unwinds to 70.75 (29.25% dd > 5% ceiling).
            var seq = e.SetupSequence(x =>
                x.GetEquityAsync(TradingMode.Paper, It.IsAny<CancellationToken>()));
            seq.ReturnsAsync(100m).ReturnsAsync(70.75m);
            e.Setup(x => x.GetEquityAsync(TradingMode.LiveTestnet, It.IsAny<CancellationToken>()))
                .ReturnsAsync(0m);
        });

        await svc.TickOnceAsync(CancellationToken.None);
        await svc.TickOnceAsync(CancellationToken.None);

        var paper = await db.RiskProfiles.AsNoTracking()
            .FirstAsync(r => r.Id == (int)TradingMode.Paper);
        paper.CircuitBreakerStatus.Should().Be(CircuitBreakerStatus.Tripped);
        paper.CircuitBreakerReason.Should().Contain("24h");
        paper.CurrentDrawdownPct.Should().BeApproximately(0.2925m, 0.0001m);
    }

    /// <summary>
    /// Loop 8 bug #19 — defence-in-depth: a small dip below the live peak must NOT
    /// trip the CB. Confirms the trip path only fires when the configured ceiling
    /// is actually breached, not merely whenever drawdown is non-zero.
    /// </summary>
    [Fact]
    public async Task Tick_SmallDrawdownBelowCeiling_KeepsCbHealthy()
    {
        var (svc, _, db) = BuildHarness(e =>
        {
            var seq = e.SetupSequence(x =>
                x.GetEquityAsync(TradingMode.Paper, It.IsAny<CancellationToken>()));
            seq.ReturnsAsync(100m).ReturnsAsync(97m); // 3% dd < 5% ceiling
            e.Setup(x => x.GetEquityAsync(TradingMode.LiveTestnet, It.IsAny<CancellationToken>()))
                .ReturnsAsync(0m);
        });

        await svc.TickOnceAsync(CancellationToken.None);
        await svc.TickOnceAsync(CancellationToken.None);

        var paper = await db.RiskProfiles.AsNoTracking()
            .FirstAsync(r => r.Id == (int)TradingMode.Paper);
        paper.CircuitBreakerStatus.Should().Be(CircuitBreakerStatus.Healthy);
        paper.CurrentDrawdownPct.Should().BeApproximately(0.03m, 0.0001m);
    }
}
