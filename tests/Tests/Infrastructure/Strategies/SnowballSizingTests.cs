using BinanceBot.Infrastructure.Strategies;
using FluentAssertions;

namespace BinanceBot.Tests.Infrastructure.Strategies;

/// <summary>
/// ADR-0015 §15.4 + ADR-0018 §18.10 superseded by ADR-0021 — snowball sizing
/// invariant. MinNotional is the max of <c>equity × 20%</c> and the user-intent
/// floor <c>$20.10</c> (Binance minNotional $5.00 + user-intent $15 gap + $0.10
/// precision buffer). Exchange NOTIONAL filter is layered on top in the fan-out
/// handler (not tested here).
/// </summary>
public class SnowballSizingTests
{
    [Theory]
    [InlineData(0, 20.10)]            // zero equity -> floor
    [InlineData(-50, 20.10)]          // negative equity -> floor (safety)
    [InlineData(50, 20.10)]           // equity × 20% = 10.00 < floor
    [InlineData(100, 20.10)]          // equity × 20% = 20.00 < floor (marjinal)
    [InlineData(100.5, 20.10)]        // crossover point — equity × 0.20 == floor
    [InlineData(200, 40.0)]           // pct branch aktif (200 × 0.20 = 40 > 20.10)
    [InlineData(500, 100.0)]          // snowball growth kicks in
    [InlineData(1000, 200.0)]         // full snowball
    public void CalcMinNotional_AppliesMaxOfEquityPctAndFloor(double equity, double expected)
    {
        var result = SnowballSizing.CalcMinNotional((decimal)equity);
        result.Should().Be((decimal)expected);
    }

    [Fact]
    public void CalcMinNotional_EquityJustAboveCrossover_UsesPct()
    {
        // Crossover: equity × 0.20 == 20.10 -> equity = 100.5. Just above crosses
        // into the pct branch so the helper returns equity * 0.20.
        SnowballSizing.CalcMinNotional(110m).Should().Be(110m * 0.20m);
    }

    [Fact]
    public void Constants_MatchAdrLiterals()
    {
        // Sentinel — any future refactor must re-tune the ADR if these change.
        SnowballSizing.FloorUsd.Should().Be(20.10m);
        SnowballSizing.EquityFraction.Should().Be(0.20m);
    }
}
