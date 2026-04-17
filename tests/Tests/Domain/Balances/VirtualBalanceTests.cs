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
    public void ApplyFill_Paper_CashClampedAtZero()
    {
        var vb = VirtualBalance.CreateDefault(TradingMode.Paper, 100m, DateTimeOffset.UtcNow);

        vb.ApplyFill(-150m, DateTimeOffset.UtcNow);

        vb.CurrentBalance.Should().Be(0m);
    }
}
