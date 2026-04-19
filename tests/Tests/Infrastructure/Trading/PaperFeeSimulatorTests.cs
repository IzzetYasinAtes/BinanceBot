using BinanceBot.Infrastructure.Trading.Paper;
using FluentAssertions;

namespace BinanceBot.Tests.Infrastructure.Trading;

/// <summary>
/// ADR-0018 §18.12 — paper-mode commission simulation. Verifies the two spot VIP 0
/// rate branches (normal taker 0.10% and BNB-discount taker 0.075%) plus the guard
/// rail for non-positive notional inputs.
/// </summary>
public class PaperFeeSimulatorTests
{
    [Fact]
    public void NormalTaker_OnePercent_OfOneThousandDollar()
    {
        // 0.10% of $1000 = $1.00. Round-trip (open + close) = $2.00.
        PaperFeeSimulator.CalculateCommission(1000m, bnbDiscount: false)
            .Should().Be(1.00m);
    }

    [Fact]
    public void NormalTaker_OnHundredDollarNotional_IsTenCents()
    {
        PaperFeeSimulator.CalculateCommission(100m, bnbDiscount: false)
            .Should().Be(0.10m);
    }

    [Fact]
    public void BnbDiscountTaker_SeventyFiveBps_OfHundredDollar()
    {
        // 0.075% of $100 = $0.075.
        PaperFeeSimulator.CalculateCommission(100m, bnbDiscount: true)
            .Should().Be(0.075m);
    }

    [Fact]
    public void NormalTaker_OnFiveDollarMinNotional_IsHalfCent()
    {
        // ADR-0018 §18.10 — $5 minNotional trade: 0.10% = $0.005.
        PaperFeeSimulator.CalculateCommission(5m, bnbDiscount: false)
            .Should().Be(0.005m);
    }

    [Fact]
    public void BnbDiscount_SaveTwoPointFiveBps_EveryTrade()
    {
        // Delta = (0.001 - 0.00075) * notional = 0.00025 * notional.
        var normal = PaperFeeSimulator.CalculateCommission(1000m, bnbDiscount: false);
        var discount = PaperFeeSimulator.CalculateCommission(1000m, bnbDiscount: true);
        (normal - discount).Should().Be(0.25m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    [InlineData(-0.01)]
    public void NonPositiveNotional_ReturnsZero_SilentGuard(double notional)
    {
        PaperFeeSimulator.CalculateCommission((decimal)notional, bnbDiscount: true)
            .Should().Be(0m);
        PaperFeeSimulator.CalculateCommission((decimal)notional, bnbDiscount: false)
            .Should().Be(0m);
    }

    [Fact]
    public void Constants_MatchAdr0018Literals()
    {
        PaperFeeSimulator.NormalFeeRate.Should().Be(0.001m);
        PaperFeeSimulator.BnbDiscountFeeRate.Should().Be(0.00075m);
    }
}
