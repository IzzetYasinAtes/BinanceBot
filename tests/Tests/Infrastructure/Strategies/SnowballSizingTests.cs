using BinanceBot.Infrastructure.Strategies;
using FluentAssertions;

namespace BinanceBot.Tests.Infrastructure.Strategies;

/// <summary>
/// ADR-0015 §15.4 — snowball sizing invariant. MinNotional is the max of
/// <c>equity × 20%</c> and the fixed <c>$20</c> user floor. Exchange NOTIONAL
/// filter is layered on top in the fan-out handler (not tested here).
/// </summary>
public class SnowballSizingTests
{
    [Theory]
    [InlineData(0, 20.0)]           // zero equity -> floor
    [InlineData(-50, 20.0)]         // negative equity -> floor (safety)
    [InlineData(50, 20.0)]          // equity × 20% = $10 < floor
    [InlineData(100, 20.0)]         // equity × 20% = $20 == floor
    [InlineData(150, 30.0)]         // equity × 20% = $30 > floor
    [InlineData(1000, 200.0)]       // snowball growth: 20% of $1000
    public void CalcMinNotional_AppliesMaxOfEquityPctAndFloor(double equity, double expected)
    {
        var result = SnowballSizing.CalcMinNotional((decimal)equity);
        result.Should().Be((decimal)expected);
    }

    [Fact]
    public void CalcMinNotional_EquityExactlyAtCrossover_UsesPct()
    {
        // Crossover: equity × 0.20 == 20.0 -> equity = 100. Pick equity = 100 +
        // tiny delta so the function exercises the pct branch.
        SnowballSizing.CalcMinNotional(100.01m).Should().Be(100.01m * 0.20m);
    }

    [Fact]
    public void Constants_MatchAdrLiterals()
    {
        // Sentinel — any future refactor must re-tune the ADR if these change.
        SnowballSizing.FloorUsd.Should().Be(20.0m);
        SnowballSizing.EquityFraction.Should().Be(0.20m);
    }
}
