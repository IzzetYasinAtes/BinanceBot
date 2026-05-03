using BinanceBot.Domain.Balances;
using BinanceBot.Domain.Balances.Events;
using BinanceBot.Domain.Common;
using FluentAssertions;

namespace BinanceBot.Tests.Domain.Balances;

/// <summary>
/// Loop 92 — VirtualBalance Futures genişlemesi (ADR-0025). WalletBalance/AllocatedMargin/UnrealizedPnl
/// ayrı tutulur, AllocateMarginForPosition + ReturnMarginAndApplyPnl + ApplyFundingFee davranışları.
/// </summary>
public class VirtualBalanceFuturesTests
{
    [Fact]
    public void AllocateMarginForPosition_LocksMargin_AndRaisesEvent()
    {
        var vb = VirtualBalance.CreateDefault(TradingMode.Paper, 500m, DateTimeOffset.UtcNow);

        vb.AllocateMarginForPosition(margin: 50m, now: DateTimeOffset.UtcNow);

        vb.WalletBalance.Should().Be(500m);
        vb.AllocatedMargin.Should().Be(50m);
        vb.AvailableBalance.Should().Be(450m);
        vb.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<PositionMarginAllocatedEvent>();
    }

    [Fact]
    public void ReturnMarginAndApplyPnl_PositivePnl_IncreasesWallet()
    {
        var vb = VirtualBalance.CreateDefault(TradingMode.Paper, 500m, DateTimeOffset.UtcNow);
        vb.AllocateMarginForPosition(50m, DateTimeOffset.UtcNow);
        vb.ClearDomainEvents();

        vb.ReturnMarginAndApplyPnl(margin: 50m, realizedPnl: 5m, now: DateTimeOffset.UtcNow);

        vb.WalletBalance.Should().Be(505m);
        vb.AllocatedMargin.Should().Be(0m);
        vb.AvailableBalance.Should().Be(505m);
        vb.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<PositionMarginReturnedEvent>();
    }

    [Fact]
    public void ReturnMarginAndApplyPnl_NegativePnl_DecreasesWallet()
    {
        var vb = VirtualBalance.CreateDefault(TradingMode.Paper, 500m, DateTimeOffset.UtcNow);
        vb.AllocateMarginForPosition(50m, DateTimeOffset.UtcNow);

        vb.ReturnMarginAndApplyPnl(margin: 50m, realizedPnl: -3m, now: DateTimeOffset.UtcNow);

        vb.WalletBalance.Should().Be(497m);
        vb.AllocatedMargin.Should().Be(0m);
    }

    [Fact]
    public void ApplyFundingFee_NegativeDelta_DecreasesWallet()
    {
        var vb = VirtualBalance.CreateDefault(TradingMode.Paper, 500m, DateTimeOffset.UtcNow);

        vb.ApplyFundingFee(fundingDelta: -0.25m, now: DateTimeOffset.UtcNow);

        vb.WalletBalance.Should().Be(499.75m);
        vb.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<FundingFeeAppliedEvent>();
    }

    [Fact]
    public void ApplyUnrealized_TracksFloatingPnl_EquityReflectsBoth()
    {
        var vb = VirtualBalance.CreateDefault(TradingMode.Paper, 500m, DateTimeOffset.UtcNow);
        vb.AllocateMarginForPosition(50m, DateTimeOffset.UtcNow);

        vb.ApplyUnrealized(unrealizedPnl: 2.5m, now: DateTimeOffset.UtcNow);

        vb.WalletBalance.Should().Be(500m);
        vb.UnrealizedPnl.Should().Be(2.5m);
        vb.Equity.Should().Be(502.5m);
    }

    [Theory]
    [InlineData(TradingMode.LiveTestnet)]
    [InlineData(TradingMode.LiveMainnet)]
    public void AllocateMarginForPosition_NonPaper_Throws(TradingMode mode)
    {
        var vb = VirtualBalance.CreateDefault(mode, 0m, DateTimeOffset.UtcNow);

        var act = () => vb.AllocateMarginForPosition(50m, DateTimeOffset.UtcNow);

        act.Should().Throw<DomainException>()
            .WithMessage("*only valid for Paper*");
    }

    [Fact]
    public void Reset_ClearsAllocatedMarginAndUnrealizedPnl()
    {
        var vb = VirtualBalance.CreateDefault(TradingMode.Paper, 500m, DateTimeOffset.UtcNow);
        vb.AllocateMarginForPosition(100m, DateTimeOffset.UtcNow);
        vb.ApplyUnrealized(5m, DateTimeOffset.UtcNow);

        vb.ResetForIteration(500m, DateTimeOffset.UtcNow);

        vb.AllocatedMargin.Should().Be(0m);
        vb.UnrealizedPnl.Should().Be(0m);
        vb.AvailableBalance.Should().Be(500m);
    }
}
