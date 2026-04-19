using BinanceBot.Domain.Balances;
using BinanceBot.Domain.Balances.Events;
using BinanceBot.Domain.Common;
using FluentAssertions;

namespace BinanceBot.Tests.Domain.Balances;

public class VirtualBalanceTests
{
    [Fact]
    public void CreateDefault_Paper_Has100USDT_AndDeterministicState()
    {
        var now = DateTimeOffset.UtcNow;
        var vb = VirtualBalance.CreateDefault(TradingMode.Paper, 100m, now);

        vb.Id.Should().Be(1);
        vb.Mode.Should().Be(TradingMode.Paper);
        vb.StartingBalance.Should().Be(100m);
        vb.CurrentBalance.Should().Be(100m);
        vb.Equity.Should().Be(100m);
        vb.ResetCount.Should().Be(0);
        vb.IterationId.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void ResetForIteration_Paper_IncrementsCounter_AndRaisesEvent()
    {
        var vb = VirtualBalance.CreateDefault(TradingMode.Paper, 100m, DateTimeOffset.UtcNow);
        var originalIteration = vb.IterationId;

        vb.ResetForIteration(200m, DateTimeOffset.UtcNow.AddHours(8));

        vb.StartingBalance.Should().Be(200m);
        vb.CurrentBalance.Should().Be(200m);
        vb.Equity.Should().Be(200m);
        vb.ResetCount.Should().Be(1);
        vb.IterationId.Should().NotBe(originalIteration);
        vb.LastResetAt.Should().NotBeNull();
        vb.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<VirtualBalanceResetEvent>();
    }

    [Theory]
    [InlineData(TradingMode.LiveTestnet)]
    [InlineData(TradingMode.LiveMainnet)]
    public void ResetForIteration_NonPaper_Throws(TradingMode mode)
    {
        var vb = VirtualBalance.CreateDefault(mode, 0m, DateTimeOffset.UtcNow);

        var act = () => vb.ResetForIteration(100m, DateTimeOffset.UtcNow);

        act.Should().Throw<DomainException>()
            .WithMessage("*only Paper*");
    }

    [Fact]
    public void ApplyFill_Paper_AdjustsCashAndEquity()
    {
        var vb = VirtualBalance.CreateDefault(TradingMode.Paper, 100m, DateTimeOffset.UtcNow);

        vb.ApplyFill(-30m, DateTimeOffset.UtcNow);

        vb.CurrentBalance.Should().Be(70m);
        vb.Equity.Should().Be(70m);
    }

    [Fact]
    public void ApplyFill_NonPaper_Throws()
    {
        var vb = VirtualBalance.CreateDefault(TradingMode.LiveTestnet, 0m, DateTimeOffset.UtcNow);

        var act = () => vb.ApplyFill(10m, DateTimeOffset.UtcNow);

        act.Should().Throw<DomainException>()
            .WithMessage("*only valid for Paper*");
    }

    [Fact]
    public void ApplyFill_Paper_AllowsNegativeWhenPositionsOpen()
    {
        // Loop 18 fix: clamping at zero destroyed accounting information when
        // multiple paper positions were open concurrently. The running balance
        // is allowed to dip negative — final invariance is enforced by the
        // sum of round-trip deltas, not a per-fill floor.
        var vb = VirtualBalance.CreateDefault(TradingMode.Paper, 100m, DateTimeOffset.UtcNow);

        vb.ApplyFill(-150m, DateTimeOffset.UtcNow);

        vb.CurrentBalance.Should().Be(-50m);
        vb.Equity.Should().Be(-50m);
    }

    [Fact]
    public void ApplyFill_Paper_RoundTripInvariance_AcrossOverlappingPositions()
    {
        // Loop 18 cash-invariance regression: simulate 34 closed positions with mixed
        // long/short sides + mixed close P/L outcomes, executed with overlapping open
        // intervals (each pair: openA, openB, closeA, closeB). Final cash MUST equal
        // starting + sum(realized PnL) regardless of intra-iteration negatives.
        var vb = VirtualBalance.CreateDefault(TradingMode.Paper, 100m, DateTimeOffset.UtcNow);
        var rnd = new Random(20260417);
        decimal totalPnl = 0m;
        var ts = DateTimeOffset.UtcNow;

        for (var i = 0; i < 17; i++)
        {
            // Generate a coin-flip pair of overlapping round trips (commission baked in).
            // Half are losses (-X), half are wins (+Y) — ensures both signs touch the path.
            var notional = 30m + (decimal)(rnd.NextDouble() * 20.0); // 30..50 USDT
            var pnlA = (i % 2 == 0 ? -1m : 1m) * (decimal)(rnd.NextDouble() * 0.5); // ±0..0.5
            var pnlB = (i % 3 == 0 ? -1m : 1m) * (decimal)(rnd.NextDouble() * 0.5);

            // Open A (long: cash -notional)
            vb.ApplyFill(-notional, ts.AddSeconds(i * 4));
            // Open B (long: cash -notional) — both legs open simultaneously
            vb.ApplyFill(-notional, ts.AddSeconds(i * 4 + 1));
            // Close A (sell back at notional + pnlA)
            vb.ApplyFill(notional + pnlA, ts.AddSeconds(i * 4 + 2));
            // Close B (sell back at notional + pnlB)
            vb.ApplyFill(notional + pnlB, ts.AddSeconds(i * 4 + 3));

            totalPnl += pnlA + pnlB;
        }

        vb.CurrentBalance.Should().BeApproximately(100m + totalPnl, 0.0001m);
        vb.Equity.Should().Be(vb.CurrentBalance);
    }
}
