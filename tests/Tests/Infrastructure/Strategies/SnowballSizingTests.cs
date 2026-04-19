using BinanceBot.Infrastructure.Strategies;
using FluentAssertions;

namespace BinanceBot.Tests.Infrastructure.Strategies;

/// <summary>
/// ADR-0015 §15.4 + ADR-0018 §18.10 — snowball sizing invariant. MinNotional is
/// the max of <c>equity × 1%</c> and the Binance-compatible <c>$5.10</c> floor
/// (minNotional $5.00 + $0.10 precision buffer). Exchange NOTIONAL filter is
/// layered on top in the fan-out handler (not tested here).
/// </summary>
public class SnowballSizingTests
{
    [Theory]
    [InlineData(0, 5.10)]            // zero equity -> floor
    [InlineData(-50, 5.10)]          // negative equity -> floor (safety)
    [InlineData(50, 5.10)]           // equity × 1% = 0.50 < floor
    [InlineData(100, 5.10)]          // equity × 1% = 1.00 < floor
    [InlineData(510, 5.10)]          // crossover point — equity × 1% == floor
    [InlineData(1000, 10.0)]         // snowball growth kicks in
    [InlineData(5000, 50.0)]         // full snowball
    public void CalcMinNotional_AppliesMaxOfEquityPctAndFloor(double equity, double expected)
    {
        var result = SnowballSizing.CalcMinNotional((decimal)equity);
        result.Should().Be((decimal)expected);
    }

    [Fact]
    public void CalcMinNotional_EquityJustAboveCrossover_UsesPct()
    {
        // Crossover: equity × 0.01 == 5.10 -> equity = 510. Just above crosses
        // into the pct branch so the helper returns equity * 0.01.
        SnowballSizing.CalcMinNotional(600m).Should().Be(600m * 0.01m);
    }

    [Fact]
    public void Constants_MatchAdrLiterals()
    {
        // Sentinel — any future refactor must re-tune the ADR if these change.
        SnowballSizing.FloorUsd.Should().Be(5.10m);
        SnowballSizing.EquityFraction.Should().Be(0.01m);
    }
}
