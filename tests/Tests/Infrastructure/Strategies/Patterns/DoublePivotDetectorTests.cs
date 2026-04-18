using BinanceBot.Application.Strategies.Patterns;
using BinanceBot.Domain.MarketData;
using BinanceBot.Infrastructure.Strategies.Patterns.Detectors;
using FluentAssertions;

namespace BinanceBot.Tests.Infrastructure.Strategies.Patterns;

public class DoublePivotDetectorTests
{
    /// <summary>
    /// Compose a clear double-bottom: two equal lows separated by a higher pivot
    /// (the neckline), then a final close above the neckline with a volume spike.
    /// </summary>
    [Fact]
    public void DoubleBottom_HappyPath_BreakoutWithVolume_LongSignal()
    {
        var bars = new List<Kline>();
        // Index 0..3 — flat warmup so detector minBars=22 condition passes.
        bars.Add(PatternTestBars.Bar(-1, 105.5m, 106m, 105m, 105.5m, 100m));
        bars.Add(PatternTestBars.Bar(0, 105m, 105.5m, 104.8m, 105.2m, 100m));
        bars.Add(PatternTestBars.Bar(1, 105m, 105.4m, 104.7m, 105m, 100m));
        bars.Add(PatternTestBars.Bar(2, 104.5m, 104.8m, 104m, 104.3m, 100m));
        // Pivot low #1 at idx 4: low 100 surrounded by 102 / 102.
        bars.Add(PatternTestBars.Bar(3, 103m, 103.2m, 102m, 102.5m, 100m));
        bars.Add(PatternTestBars.Bar(4, 102m, 102m, 100m, 100.5m, 100m));   // PIVOT LOW #1
        bars.Add(PatternTestBars.Bar(5, 100.5m, 102.5m, 100.4m, 102m, 100m));
        // Climb to neckline ~103 then drop again (with intermediate pivot HIGH at idx 7).
        bars.Add(PatternTestBars.Bar(6, 102m, 103m, 102m, 103m, 100m));
        bars.Add(PatternTestBars.Bar(7, 103m, 103.4m, 102.5m, 102.8m, 100m)); // neckline-area
        bars.Add(PatternTestBars.Bar(8, 102.5m, 102.8m, 101m, 101.5m, 100m));
        // Pivot low #2 at idx 10: low 100.1 surrounded by 101 / 101.
        bars.Add(PatternTestBars.Bar(9, 101.5m, 101.8m, 101m, 101.2m, 100m));
        bars.Add(PatternTestBars.Bar(10, 101m, 101.2m, 100.1m, 100.4m, 100m)); // PIVOT LOW #2 (within 0.5% of #1)
        bars.Add(PatternTestBars.Bar(11, 100.4m, 102.5m, 100.3m, 102m, 100m));
        // Build to breakout — five bars approaching 103.
        bars.Add(PatternTestBars.Bar(12, 102m, 102.7m, 102m, 102.5m, 100m));
        bars.Add(PatternTestBars.Bar(13, 102.5m, 103m, 102.4m, 102.9m, 100m));
        bars.Add(PatternTestBars.Bar(14, 102.9m, 103.2m, 102.8m, 103m, 100m));
        bars.Add(PatternTestBars.Bar(15, 103m, 103.3m, 102.9m, 103.1m, 100m));
        bars.Add(PatternTestBars.Bar(16, 103.1m, 103.3m, 103m, 103.2m, 100m));
        bars.Add(PatternTestBars.Bar(17, 103.2m, 103.4m, 103.1m, 103.3m, 100m));
        bars.Add(PatternTestBars.Bar(18, 103.3m, 103.5m, 103.2m, 103.4m, 100m));
        bars.Add(PatternTestBars.Bar(19, 103.4m, 103.6m, 103.3m, 103.5m, 100m));
        // Breakout bar: closes well above neckline (max high in p1..p2 = 103.4 at idx 7), volume spike.
        bars.Add(PatternTestBars.Bar(20, 103.5m, 105m, 103.4m, 104.8m, 250m));

        var r = new DoubleBottomDetector().Detect(bars);
        r.Should().NotBeNull();
        r!.Direction.Should().Be(PatternDirection.Long);
        r.MaxHoldBars.Should().Be(10);
    }

    [Fact]
    public void DoubleBottom_FlatRange_ReturnsNull()
    {
        var bars = new List<Kline>();
        for (var i = 0; i < 22; i++)
        {
            bars.Add(PatternTestBars.Bar(i, 100m, 100.5m, 99.5m, 100m, 100m));
        }
        new DoubleBottomDetector().Detect(bars).Should().BeNull();
    }

    [Fact]
    public void DoubleTop_HappyPath_BreakdownWithVolume_ShortSignal()
    {
        var bars = new List<Kline>();
        // Mirror of double bottom — two pivot highs with a lower trough between, breakdown.
        bars.Add(PatternTestBars.Bar(-1, 94m, 94.5m, 93.8m, 94m, 100m));
        bars.Add(PatternTestBars.Bar(0, 95m, 95.2m, 94.5m, 94.8m, 100m));
        bars.Add(PatternTestBars.Bar(1, 95m, 95.3m, 94.6m, 95m, 100m));
        bars.Add(PatternTestBars.Bar(2, 95.5m, 96m, 95.2m, 95.7m, 100m));
        bars.Add(PatternTestBars.Bar(3, 97m, 98m, 96.8m, 97.5m, 100m));
        bars.Add(PatternTestBars.Bar(4, 98m, 100m, 98m, 99.5m, 100m));    // PIVOT HIGH #1
        bars.Add(PatternTestBars.Bar(5, 99.5m, 99.6m, 97.5m, 98m, 100m));
        bars.Add(PatternTestBars.Bar(6, 98m, 98m, 97m, 97m, 100m));
        bars.Add(PatternTestBars.Bar(7, 97m, 97.6m, 96.6m, 97.2m, 100m)); // pivot low (neckline area)
        bars.Add(PatternTestBars.Bar(8, 97.5m, 98.5m, 97m, 98.5m, 100m));
        bars.Add(PatternTestBars.Bar(9, 98.5m, 98.8m, 98m, 98.5m, 100m));
        bars.Add(PatternTestBars.Bar(10, 98.5m, 99.95m, 98.4m, 99.4m, 100m)); // PIVOT HIGH #2 (within 0.5%)
        bars.Add(PatternTestBars.Bar(11, 99.4m, 99.5m, 97.5m, 97.8m, 100m));
        bars.Add(PatternTestBars.Bar(12, 97.8m, 97.9m, 97m, 97.3m, 100m));
        bars.Add(PatternTestBars.Bar(13, 97.3m, 97.5m, 96.8m, 97m, 100m));
        bars.Add(PatternTestBars.Bar(14, 97m, 97.2m, 96.7m, 96.8m, 100m));
        bars.Add(PatternTestBars.Bar(15, 96.8m, 96.9m, 96.6m, 96.7m, 100m));
        bars.Add(PatternTestBars.Bar(16, 96.7m, 96.8m, 96.5m, 96.6m, 100m));
        bars.Add(PatternTestBars.Bar(17, 96.6m, 96.7m, 96.5m, 96.55m, 100m));
        bars.Add(PatternTestBars.Bar(18, 96.55m, 96.6m, 96.4m, 96.5m, 100m));
        bars.Add(PatternTestBars.Bar(19, 96.5m, 96.55m, 96.45m, 96.5m, 100m));
        // Breakdown — close below neckline (96.6 = min low between pivots) with volume.
        bars.Add(PatternTestBars.Bar(20, 96.5m, 96.55m, 95m, 95.5m, 250m));

        var r = new DoubleTopDetector().Detect(bars);
        r.Should().NotBeNull();
        r!.Direction.Should().Be(PatternDirection.Short);
    }

    [Fact]
    public void DoubleTop_FlatRange_ReturnsNull()
    {
        var bars = new List<Kline>();
        for (var i = 0; i < 22; i++)
        {
            bars.Add(PatternTestBars.Bar(i, 100m, 100.5m, 99.5m, 100m, 100m));
        }
        new DoubleTopDetector().Detect(bars).Should().BeNull();
    }
}
