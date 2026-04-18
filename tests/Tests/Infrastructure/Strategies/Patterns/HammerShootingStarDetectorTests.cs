using BinanceBot.Application.Strategies.Patterns;
using BinanceBot.Infrastructure.Strategies.Patterns.Detectors;
using FluentAssertions;

namespace BinanceBot.Tests.Infrastructure.Strategies.Patterns;

public class HammerShootingStarDetectorTests
{
    [Fact]
    public void Hammer_HappyPath_OversoldDowntrend_LongSignal()
    {
        // Downtrend → RSI < 40 (oversold band).
        var bars = PatternTestBars.DowntrendPrefix(20, start: 130m, step: 0.6m);
        // Hammer bar: open=98.5, close=99 (small body in upper third), low=95 (long lower wick),
        // high=99.1 (tiny upper wick). Body=0.5, lower=3.5 (>2× body), upper=0.1 (<10% range=4.1).
        bars.Add(PatternTestBars.Bar(20, 98.5m, 99.1m, 95m, 99m, 150m));

        var r = new HammerDetector().Detect(bars);
        r.Should().NotBeNull();
        r!.Direction.Should().Be(PatternDirection.Long);
        r.MaxHoldBars.Should().Be(5);
        r.StopPrice.Should().BeLessThan(r.EntryPrice);
    }

    [Fact]
    public void Hammer_NoLongLowerWick_ReturnsNull()
    {
        var bars = PatternTestBars.DowntrendPrefix(20, start: 130m, step: 0.6m);
        // No long lower wick: open=98, close=99, low=97.5 (lower=0.5), high=99.2 (upper=0.2).
        bars.Add(PatternTestBars.Bar(20, 98m, 99.2m, 97.5m, 99m, 150m));
        new HammerDetector().Detect(bars).Should().BeNull();
    }

    [Fact]
    public void ShootingStar_HappyPath_OverboughtUptrend_ShortSignal()
    {
        // Uptrend → RSI > 60 (overbought band).
        var bars = PatternTestBars.UptrendPrefix(20, start: 70m, step: 0.6m);
        // Shooting star: open=82, close=81.5 (small body lower third), high=85 (long upper wick),
        // low=81.4 (tiny lower wick). body=0.5, upper=3, lower=0.1.
        bars.Add(PatternTestBars.Bar(20, 82m, 85m, 81.4m, 81.5m, 150m));

        var r = new ShootingStarDetector().Detect(bars);
        r.Should().NotBeNull();
        r!.Direction.Should().Be(PatternDirection.Short);
        r.StopPrice.Should().BeGreaterThan(r.EntryPrice);
    }

    [Fact]
    public void ShootingStar_NoLongUpperWick_ReturnsNull()
    {
        var bars = PatternTestBars.UptrendPrefix(20, start: 70m, step: 0.6m);
        bars.Add(PatternTestBars.Bar(20, 82m, 82.4m, 81.4m, 81.5m, 150m));
        new ShootingStarDetector().Detect(bars).Should().BeNull();
    }
}
