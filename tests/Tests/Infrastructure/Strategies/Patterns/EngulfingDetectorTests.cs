using BinanceBot.Application.Strategies.Patterns;
using BinanceBot.Infrastructure.Strategies.Patterns.Detectors;
using FluentAssertions;

namespace BinanceBot.Tests.Infrastructure.Strategies.Patterns;

public class EngulfingDetectorTests
{
    [Fact]
    public void Bullish_HappyPath_BalancedRsi_VolumeSpike_Returns()
    {
        // Range-bound prefix (RSI ~50 in [40,60] band) → bearish bar → bullish engulfing bar.
        var bars = PatternTestBars.RangePrefix(20, centre: 100m, volume: 100m);
        // bearish b1
        bars.Add(PatternTestBars.Bar(20, 102m, 102.2m, 99.5m, 100m, 100m));
        // bullish b2 engulfs b1 (open <= b1.close=100, close >= b1.open=102) with vol spike.
        bars.Add(PatternTestBars.Bar(21, 99.5m, 103m, 99m, 102.5m, 200m));

        var sut = new BullishEngulfingDetector();
        var r = sut.Detect(bars);

        r.Should().NotBeNull();
        r!.Type.Should().Be(PatternType.BullishEngulfing);
        r.Direction.Should().Be(PatternDirection.Long);
        r.Confidence.Should().BeGreaterThanOrEqualTo(0.5m);
        r.StopPrice.Should().BeLessThan(r.EntryPrice);
        r.TakeProfit.Should().BeGreaterThan(r.EntryPrice);
        r.MaxHoldBars.Should().Be(5);
    }

    [Fact]
    public void Bullish_NoVolumeSpike_ReturnsNull()
    {
        var bars = PatternTestBars.RangePrefix(20, centre: 100m, volume: 100m);
        bars.Add(PatternTestBars.Bar(20, 102m, 102.2m, 99.5m, 100m, 100m));
        bars.Add(PatternTestBars.Bar(21, 99.5m, 103m, 99m, 102.5m, 80m)); // below avg
        var sut = new BullishEngulfingDetector();
        sut.Detect(bars).Should().BeNull();
    }

    [Fact]
    public void Bullish_InsufficientBars_ReturnsNull()
    {
        var bars = PatternTestBars.RangePrefix(5, centre: 100m);
        new BullishEngulfingDetector().Detect(bars).Should().BeNull();
    }

    [Fact]
    public void Bearish_HappyPath_BalancedRsi_VolumeSpike_Returns()
    {
        var bars = PatternTestBars.RangePrefix(20, centre: 100m, volume: 100m);
        // bullish b1
        bars.Add(PatternTestBars.Bar(20, 99m, 101m, 98.8m, 100.5m, 100m));
        // bearish b2 engulfs (open >= b1.close=100.5, close <= b1.open=99) with vol spike.
        bars.Add(PatternTestBars.Bar(21, 100.8m, 101.2m, 97.5m, 98m, 200m));
        var r = new BearishEngulfingDetector().Detect(bars);

        r.Should().NotBeNull();
        r!.Direction.Should().Be(PatternDirection.Short);
        r.StopPrice.Should().BeGreaterThan(r.EntryPrice);
        r.TakeProfit.Should().BeLessThan(r.EntryPrice);
    }

    [Fact]
    public void Bearish_NoVolumeSpike_ReturnsNull()
    {
        var bars = PatternTestBars.RangePrefix(20, centre: 100m, volume: 100m);
        bars.Add(PatternTestBars.Bar(20, 99m, 101m, 98.8m, 100.5m, 100m));
        bars.Add(PatternTestBars.Bar(21, 100.8m, 101.2m, 97.5m, 98m, 80m));
        new BearishEngulfingDetector().Detect(bars).Should().BeNull();
    }
}
