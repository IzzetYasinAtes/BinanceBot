using BinanceBot.Infrastructure.Trading.Paper;
using FluentAssertions;

namespace BinanceBot.Tests.Infrastructure.Trading;

/// <summary>
/// Loop 92 — Futures taker fee simulator (ADR-0025). Verifies VIP0 taker
/// %0.05 (0.0005) ve BNB-discount %0.045 (0.00045) hesaplamaları + non-positive
/// notional guard rail.
/// </summary>
public class PaperFeeSimulatorTests
{
    [Fact]
    public void FuturesTaker_FiftyBps_OfThousandDollar()
    {
        // Futures VIP0 taker %0.05 of $1000 = $0.50. Round-trip (open + close) = $1.00.
        PaperFeeSimulator.CalculateTakerFee(1000m, bnbDiscount: false)
            .Should().Be(0.50m);
    }

    [Fact]
    public void FuturesTaker_OnHundredDollarNotional_IsFiveCents()
    {
        PaperFeeSimulator.CalculateTakerFee(100m, bnbDiscount: false)
            .Should().Be(0.05m);
    }

    [Fact]
    public void BnbDiscountTaker_FortyFiveBps_OfHundredDollar()
    {
        // BNB indirim %0.045 of $100 = $0.045.
        PaperFeeSimulator.CalculateTakerFee(100m, bnbDiscount: true)
            .Should().Be(0.045m);
    }

    [Fact]
    public void FuturesTaker_OnFiveDollarMinNotional_IsQuarterCent()
    {
        // Futures min notional $5: 0.05% = $0.0025.
        PaperFeeSimulator.CalculateTakerFee(5m, bnbDiscount: false)
            .Should().Be(0.0025m);
    }

    [Fact]
    public void BnbDiscount_SavesHalfBps_EveryTrade()
    {
        // Delta = (0.0005 - 0.00045) * notional = 0.00005 * notional.
        var normal = PaperFeeSimulator.CalculateTakerFee(1000m, bnbDiscount: false);
        var discount = PaperFeeSimulator.CalculateTakerFee(1000m, bnbDiscount: true);
        (normal - discount).Should().Be(0.05m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    [InlineData(-0.01)]
    public void NonPositiveNotional_ReturnsZero_SilentGuard(double notional)
    {
        PaperFeeSimulator.CalculateTakerFee((decimal)notional, bnbDiscount: true)
            .Should().Be(0m);
        PaperFeeSimulator.CalculateTakerFee((decimal)notional, bnbDiscount: false)
            .Should().Be(0m);
    }

    [Fact]
    public void Constants_MatchBinanceExpertSpec_Loop92()
    {
        // binance-expert spec §13: Futures VIP0 taker %0.05, BNB-discount %0.045.
        PaperFeeSimulator.FuturesTakerFeeRate.Should().Be(0.0005m);
        PaperFeeSimulator.FuturesTakerFeeRateBnb.Should().Be(0.00045m);
        PaperFeeSimulator.FuturesMakerFeeRate.Should().Be(0.0002m);
    }

    [Fact]
    public void CalculateCommission_BackwardCompat_DelegatesToTakerFee()
    {
        // Eski Spot adı "CalculateCommission" şimdi taker fee'ye delegate ediyor.
        var taker = PaperFeeSimulator.CalculateTakerFee(500m, bnbDiscount: false);
        var legacy = PaperFeeSimulator.CalculateCommission(500m, bnbDiscount: false);
        legacy.Should().Be(taker);
    }
}
