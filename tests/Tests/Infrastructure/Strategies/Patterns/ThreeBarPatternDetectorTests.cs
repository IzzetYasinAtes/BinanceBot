using BinanceBot.Application.Strategies.Patterns;
using BinanceBot.Infrastructure.Strategies.Patterns.Detectors;
using FluentAssertions;

namespace BinanceBot.Tests.Infrastructure.Strategies.Patterns;

public class ThreeBarPatternDetectorTests
{
    [Fact]
    public void ThreeWhiteSoldiers_HappyPath_LongSignal()
    {
        var bars = PatternTestBars.RangePrefix(20, centre: 100m, volume: 100m);
        // Three bullish bars, each opens inside prior body, closes higher.
        bars.Add(PatternTestBars.Bar(20, 99.5m, 100.5m, 99.4m, 100.3m, 100m)); // b1
        bars.Add(PatternTestBars.Bar(21, 100m, 101m, 99.9m, 100.8m, 110m));    // b2 opens within b1 body, closes higher
        bars.Add(PatternTestBars.Bar(22, 100.5m, 101.5m, 100.4m, 101.3m, 130m)); // b3

        var r = new ThreeWhiteSoldiersDetector().Detect(bars);
        r.Should().NotBeNull();
        r!.Direction.Should().Be(PatternDirection.Long);
        r.MaxHoldBars.Should().Be(5);
    }

    [Fact]
    public void ThreeWhiteSoldiers_OneBearishBar_ReturnsNull()
    {
        var bars = PatternTestBars.RangePrefix(20, centre: 100m, volume: 100m);
        bars.Add(PatternTestBars.Bar(20, 99.5m, 100.5m, 99.4m, 100.3m, 100m));
        bars.Add(PatternTestBars.Bar(21, 100m, 101m, 99.9m, 100.8m, 110m));
        bars.Add(PatternTestBars.Bar(22, 100.8m, 100.9m, 99.5m, 99.7m, 130m)); // b3 bearish
        new ThreeWhiteSoldiersDetector().Detect(bars).Should().BeNull();
    }

    [Fact]
    public void ThreeBlackCrows_HappyPath_ShortSignal()
    {
        var bars = PatternTestBars.RangePrefix(20, centre: 100m, volume: 100m);
        bars.Add(PatternTestBars.Bar(20, 100.5m, 100.6m, 99.5m, 99.7m, 100m));  // b1 bearish
        bars.Add(PatternTestBars.Bar(21, 100m, 100.1m, 99m, 99.2m, 110m));      // b2 opens within b1 body, closes lower
        bars.Add(PatternTestBars.Bar(22, 99.5m, 99.6m, 98.5m, 98.7m, 130m));    // b3

        var r = new ThreeBlackCrowsDetector().Detect(bars);
        r.Should().NotBeNull();
        r!.Direction.Should().Be(PatternDirection.Short);
    }

    [Fact]
    public void ThreeBlackCrows_OneBullishBar_ReturnsNull()
    {
        var bars = PatternTestBars.RangePrefix(20, centre: 100m, volume: 100m);
        bars.Add(PatternTestBars.Bar(20, 100.5m, 100.6m, 99.5m, 99.7m, 100m));
        bars.Add(PatternTestBars.Bar(21, 100m, 100.1m, 99m, 99.2m, 110m));
        bars.Add(PatternTestBars.Bar(22, 99m, 100m, 98.8m, 99.8m, 130m)); // bullish
        new ThreeBlackCrowsDetector().Detect(bars).Should().BeNull();
    }

    [Fact]
    public void MorningStar_HappyPath_LongSignal()
    {
        var bars = PatternTestBars.DowntrendPrefix(20, start: 110m, step: 0.5m);
        // b1 bearish big body: open=100, close=98.5, range 100..98.4 → body=1.5, range=1.6 → body 94% of range OK.
        bars.Add(PatternTestBars.Bar(20, 100m, 100.1m, 98.4m, 98.5m, 100m));
        // b2 small body (< 30% of b1 body 1.5 = 0.45): open=98.6, close=98.55, body=0.05.
        bars.Add(PatternTestBars.Bar(21, 98.6m, 98.7m, 98.4m, 98.55m, 80m));
        // b3 bullish closing above b1 mid (= (100+98.5)/2 = 99.25): close=99.5.
        bars.Add(PatternTestBars.Bar(22, 98.6m, 99.6m, 98.5m, 99.5m, 150m));

        var r = new MorningStarDetector().Detect(bars);
        r.Should().NotBeNull();
        r!.Direction.Should().Be(PatternDirection.Long);
        r.MaxHoldBars.Should().Be(7);
    }

    [Fact]
    public void MorningStar_B3DoesNotCloseAboveB1Mid_ReturnsNull()
    {
        var bars = PatternTestBars.DowntrendPrefix(20, start: 110m, step: 0.5m);
        bars.Add(PatternTestBars.Bar(20, 100m, 100.1m, 98.4m, 98.5m, 100m));
        bars.Add(PatternTestBars.Bar(21, 98.6m, 98.7m, 98.4m, 98.55m, 80m));
        // b3 bullish but closes only at 98.9, below b1 mid 99.25.
        bars.Add(PatternTestBars.Bar(22, 98.6m, 99m, 98.5m, 98.9m, 150m));
        new MorningStarDetector().Detect(bars).Should().BeNull();
    }

    [Fact]
    public void EveningStar_HappyPath_ShortSignal()
    {
        var bars = PatternTestBars.UptrendPrefix(20, start: 90m, step: 0.5m);
        // b1 bullish big body: open=100, close=101.5, range 100..101.6 → body 1.5, range 1.6 OK.
        bars.Add(PatternTestBars.Bar(20, 100m, 101.6m, 99.9m, 101.5m, 100m));
        // b2 small body: open=101.4, close=101.45.
        bars.Add(PatternTestBars.Bar(21, 101.4m, 101.6m, 101.3m, 101.45m, 80m));
        // b3 bearish closing below b1 mid (= (100+101.5)/2 = 100.75): close=100.5.
        bars.Add(PatternTestBars.Bar(22, 101.4m, 101.5m, 100.4m, 100.5m, 150m));

        var r = new EveningStarDetector().Detect(bars);
        r.Should().NotBeNull();
        r!.Direction.Should().Be(PatternDirection.Short);
    }

    [Fact]
    public void EveningStar_B3DoesNotCloseBelowB1Mid_ReturnsNull()
    {
        var bars = PatternTestBars.UptrendPrefix(20, start: 90m, step: 0.5m);
        bars.Add(PatternTestBars.Bar(20, 100m, 101.6m, 99.9m, 101.5m, 100m));
        bars.Add(PatternTestBars.Bar(21, 101.4m, 101.6m, 101.3m, 101.45m, 80m));
        bars.Add(PatternTestBars.Bar(22, 101.4m, 101.5m, 101m, 101.1m, 150m));
        new EveningStarDetector().Detect(bars).Should().BeNull();
    }
}
