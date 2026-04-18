using BinanceBot.Application.Strategies.Patterns;
using BinanceBot.Domain.MarketData;
using BinanceBot.Infrastructure.Strategies.Patterns.Detectors;
using FluentAssertions;

namespace BinanceBot.Tests.Infrastructure.Strategies.Patterns;

public class FlagTriangleDetectorTests
{
    [Fact]
    public void BullFlag_HappyPath_LongSignal()
    {
        // bars[^9..^5] = idx 13..17. Pole start (idx 13).Low → pole end (idx 17).High >= +1%.
        // bars[^4..^2] = idx 18..20: shallow consolidation.
        // last bar (idx 21) breaks above consolidation max with vol spike.
        var bars = new List<Kline>();
        // 13 warmup bars (indices 0..12), flat around 100.
        for (var i = 0; i < 13; i++)
        {
            bars.Add(PatternTestBars.Bar(i, 100m, 100.5m, 99.5m, 100m, 100m));
        }
        // Pole: idx 13 starts ~100, idx 17 reaches ~104 (+4%).
        bars.Add(PatternTestBars.Bar(13, 100m, 100.5m, 100m, 100.5m, 100m));    // pole start (low=100)
        bars.Add(PatternTestBars.Bar(14, 100.5m, 102m, 100.4m, 101.8m, 100m));
        bars.Add(PatternTestBars.Bar(15, 101.8m, 103m, 101.5m, 102.8m, 100m));
        bars.Add(PatternTestBars.Bar(16, 102.8m, 103.5m, 102.5m, 103.3m, 100m));
        bars.Add(PatternTestBars.Bar(17, 103.3m, 104.2m, 103.2m, 104m, 100m));   // pole end (high=104.2)
        // Consolidation: shallow drift, retrace 0.5 < 50% of pole 4.2.
        bars.Add(PatternTestBars.Bar(18, 104m, 104.2m, 103.5m, 103.8m, 100m));
        bars.Add(PatternTestBars.Bar(19, 103.8m, 104.1m, 103.4m, 103.7m, 100m));
        bars.Add(PatternTestBars.Bar(20, 103.7m, 104m, 103.5m, 103.9m, 100m));   // consMax = 104.2
        // Breakout above consMax 104.2 with volume spike.
        bars.Add(PatternTestBars.Bar(21, 103.9m, 105.5m, 103.8m, 105m, 250m));

        var r = new BullFlagDetector().Detect(bars);
        r.Should().NotBeNull();
        r!.Direction.Should().Be(PatternDirection.Long);
        r.MaxHoldBars.Should().Be(10);
    }

    [Fact]
    public void BullFlag_NoBreakout_ReturnsNull()
    {
        var bars = new List<Kline>();
        for (var i = 0; i < 22; i++)
        {
            bars.Add(PatternTestBars.Bar(i, 100m, 100.2m, 99.8m, 100m, 100m));
        }
        new BullFlagDetector().Detect(bars).Should().BeNull();
    }

    [Fact]
    public void BearFlag_HappyPath_ShortSignal()
    {
        var bars = new List<Kline>();
        for (var i = 0; i < 13; i++)
        {
            bars.Add(PatternTestBars.Bar(i, 100m, 100.5m, 99.5m, 100m, 100m));
        }
        // Pole: high 100 → low 96 (-4%).
        bars.Add(PatternTestBars.Bar(13, 100m, 100m, 99.5m, 99.5m, 100m));    // pole start (high=100)
        bars.Add(PatternTestBars.Bar(14, 99.5m, 99.6m, 98m, 98.2m, 100m));
        bars.Add(PatternTestBars.Bar(15, 98.2m, 98.5m, 97m, 97.2m, 100m));
        bars.Add(PatternTestBars.Bar(16, 97.2m, 97.5m, 96.5m, 96.7m, 100m));
        bars.Add(PatternTestBars.Bar(17, 96.7m, 96.8m, 95.8m, 96m, 100m));     // pole end (low=95.8)
        // Consolidation: shallow drift up, retrace small.
        bars.Add(PatternTestBars.Bar(18, 96m, 96.5m, 95.9m, 96.3m, 100m));
        bars.Add(PatternTestBars.Bar(19, 96.3m, 96.6m, 96m, 96.4m, 100m));
        bars.Add(PatternTestBars.Bar(20, 96.4m, 96.5m, 96.1m, 96.2m, 100m));   // consMin = 95.9
        // Breakdown below consMin 95.9 with volume spike.
        bars.Add(PatternTestBars.Bar(21, 96.2m, 96.3m, 94.5m, 94.8m, 250m));

        var r = new BearFlagDetector().Detect(bars);
        r.Should().NotBeNull();
        r!.Direction.Should().Be(PatternDirection.Short);
    }

    [Fact]
    public void BearFlag_NoBreakdown_ReturnsNull()
    {
        var bars = new List<Kline>();
        for (var i = 0; i < 22; i++)
        {
            bars.Add(PatternTestBars.Bar(i, 100m, 100.2m, 99.8m, 100m, 100m));
        }
        new BearFlagDetector().Detect(bars).Should().BeNull();
    }

    [Fact]
    public void AscendingTriangle_HappyPath_LongSignal()
    {
        var bars = new List<Kline>();
        // 4 warmup bars to satisfy minBars >= Window+5 = 20.
        for (var i = 0; i < 4; i++)
        {
            bars.Add(PatternTestBars.Bar(i, 99m, 99.5m, 98.5m, 99m, 100m));
        }
        // Window: idx 5..19 (15 bars). Resistance ~100, support rising 96 → 99.
        // Resistance touches: bars[5], bars[10], bars[15] highs ~100.
        bars.Add(PatternTestBars.Bar(4, 96m, 100m, 96m, 99m, 100m));     // touch 1
        bars.Add(PatternTestBars.Bar(5, 99m, 99.5m, 96.5m, 97m, 100m));
        bars.Add(PatternTestBars.Bar(6, 97m, 98m, 96.8m, 97.5m, 100m));
        bars.Add(PatternTestBars.Bar(7, 97.5m, 99m, 97.5m, 98.5m, 100m));
        bars.Add(PatternTestBars.Bar(8, 98.5m, 100m, 98m, 99m, 100m));   // touch 2 (high=100)
        bars.Add(PatternTestBars.Bar(9, 99m, 99.5m, 97.5m, 97.8m, 100m));
        bars.Add(PatternTestBars.Bar(10, 97.8m, 98m, 97.5m, 97.8m, 100m));
        bars.Add(PatternTestBars.Bar(11, 97.8m, 99m, 97.7m, 98.5m, 100m));
        bars.Add(PatternTestBars.Bar(12, 98.5m, 100m, 98.4m, 99.2m, 100m)); // touch 3 (high=100)
        bars.Add(PatternTestBars.Bar(13, 99.2m, 99.5m, 98m, 98.5m, 100m));
        bars.Add(PatternTestBars.Bar(14, 98.5m, 99m, 98.4m, 98.8m, 100m));
        bars.Add(PatternTestBars.Bar(15, 98.8m, 99.5m, 98.6m, 99.2m, 100m));
        bars.Add(PatternTestBars.Bar(16, 99.2m, 100m, 99m, 99.5m, 100m));   // touch 4
        bars.Add(PatternTestBars.Bar(17, 99.5m, 99.8m, 99.2m, 99.5m, 100m));
        bars.Add(PatternTestBars.Bar(18, 99.5m, 99.8m, 99.3m, 99.6m, 100m));
        bars.Add(PatternTestBars.Bar(19, 99.6m, 99.9m, 99.4m, 99.8m, 100m));
        // Breakout above 100 with volume.
        bars.Add(PatternTestBars.Bar(20, 99.8m, 102m, 99.7m, 101.5m, 250m));

        var r = new AscendingTriangleDetector().Detect(bars);
        r.Should().NotBeNull();
        r!.Direction.Should().Be(PatternDirection.Long);
    }

    [Fact]
    public void AscendingTriangle_FlatRange_ReturnsNull()
    {
        var bars = new List<Kline>();
        for (var i = 0; i < 22; i++)
        {
            bars.Add(PatternTestBars.Bar(i, 100m, 100.2m, 99.8m, 100m, 100m));
        }
        new AscendingTriangleDetector().Detect(bars).Should().BeNull();
    }

    [Fact]
    public void DescendingTriangle_HappyPath_ShortSignal()
    {
        var bars = new List<Kline>();
        for (var i = 0; i < 4; i++)
        {
            bars.Add(PatternTestBars.Bar(i, 101m, 101.5m, 100.5m, 101m, 100m));
        }
        // Mirror: support ~96, falling resistance.
        bars.Add(PatternTestBars.Bar(4, 100m, 100m, 96m, 96.5m, 100m));   // touch 1 (low=96)
        bars.Add(PatternTestBars.Bar(5, 96.5m, 99m, 96.5m, 98m, 100m));
        bars.Add(PatternTestBars.Bar(6, 98m, 99m, 97m, 97.5m, 100m));
        bars.Add(PatternTestBars.Bar(7, 97.5m, 98m, 96.5m, 97m, 100m));
        bars.Add(PatternTestBars.Bar(8, 97m, 98m, 96m, 96.5m, 100m));     // touch 2 (low=96)
        bars.Add(PatternTestBars.Bar(9, 96.5m, 98m, 96.5m, 97.5m, 100m));
        bars.Add(PatternTestBars.Bar(10, 97.5m, 98m, 97m, 97.2m, 100m));
        bars.Add(PatternTestBars.Bar(11, 97.2m, 97.5m, 96.5m, 96.8m, 100m));
        bars.Add(PatternTestBars.Bar(12, 96.8m, 97m, 96m, 96.5m, 100m));   // touch 3 (low=96)
        bars.Add(PatternTestBars.Bar(13, 96.5m, 97.5m, 96.5m, 97m, 100m));
        bars.Add(PatternTestBars.Bar(14, 97m, 97.2m, 96.8m, 97m, 100m));
        bars.Add(PatternTestBars.Bar(15, 97m, 97.2m, 96.5m, 96.8m, 100m));
        bars.Add(PatternTestBars.Bar(16, 96.8m, 97m, 96m, 96.5m, 100m));   // touch 4
        bars.Add(PatternTestBars.Bar(17, 96.5m, 96.8m, 96.3m, 96.5m, 100m));
        bars.Add(PatternTestBars.Bar(18, 96.5m, 96.7m, 96.2m, 96.4m, 100m));
        bars.Add(PatternTestBars.Bar(19, 96.4m, 96.6m, 96.1m, 96.2m, 100m));
        // Breakdown below support 96 with volume.
        bars.Add(PatternTestBars.Bar(20, 96.2m, 96.3m, 94m, 94.5m, 250m));

        var r = new DescendingTriangleDetector().Detect(bars);
        r.Should().NotBeNull();
        r!.Direction.Should().Be(PatternDirection.Short);
    }

    [Fact]
    public void DescendingTriangle_FlatRange_ReturnsNull()
    {
        var bars = new List<Kline>();
        for (var i = 0; i < 22; i++)
        {
            bars.Add(PatternTestBars.Bar(i, 100m, 100.2m, 99.8m, 100m, 100m));
        }
        new DescendingTriangleDetector().Detect(bars).Should().BeNull();
    }
}
