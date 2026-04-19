using BinanceBot.Domain.MarketData;
using BinanceBot.Domain.ValueObjects;
using BinanceBot.Infrastructure.Strategies.Evaluators;
using FluentAssertions;

namespace BinanceBot.Tests.Infrastructure.Strategies;

/// <summary>
/// ADR-0015 §15.6 — correctness of the VWAP / VolumeSma / SwingHigh helpers the
/// <see cref="BinanceBot.Infrastructure.Strategies.Indicators.MarketIndicatorService"/>
/// relies on. The service itself is a hosted composition of REST warmup + WS
/// consumer + lock-serialised rolling buffers; the numerical correctness is
/// localised here so shape regressions are caught without spinning up the hosted
/// pipeline.
/// </summary>
public class MarketIndicatorIndicatorsTests
{
    private static Kline Bar(int seq, decimal close, decimal high, decimal low, decimal volume)
    {
        var open = DateTimeOffset.UnixEpoch.AddMinutes(seq);
        return Kline.Ingest(
            Symbol.From("BTCUSDT"),
            KlineInterval.OneMinute,
            openTime: open,
            closeTime: open.AddMinutes(1),
            open: close,
            high: high,
            low: low,
            close: close,
            volume: volume,
            quoteVolume: close * volume,
            tradeCount: 1,
            takerBuyBase: 0m,
            takerBuyQuote: 0m,
            isClosed: true);
    }

    [Fact]
    public void Vwap_TwoBars_WeightedByVolume()
    {
        // Bar 1: typical = (11+9+10)/3 = 10, volume 1
        // Bar 2: typical = (22+18+20)/3 = 20, volume 3
        // VWAP = (10×1 + 20×3) / (1+3) = 70 / 4 = 17.5
        var bars = new List<Kline>
        {
            Bar(0, close: 10m, high: 11m, low: 9m, volume: 1m),
            Bar(1, close: 20m, high: 22m, low: 18m, volume: 3m),
        };

        Indicators.Vwap(bars).Should().Be(17.5m);
    }

    [Fact]
    public void Vwap_ZeroVolume_ReturnsZero()
    {
        var bars = Enumerable.Range(0, 5)
            .Select(i => Bar(i, 100m, 100m, 100m, 0m))
            .ToList();

        Indicators.Vwap(bars).Should().Be(0m);
    }

    [Fact]
    public void Vwap_EmptyBars_ReturnsZero()
    {
        Indicators.Vwap(Array.Empty<Kline>()).Should().Be(0m);
    }

    [Fact]
    public void VolumeSma_TwentyBars_AveragesLastTwenty()
    {
        // 30 bars total; last 20 carry volume = i+10 (10..29); sum = 390; avg = 19.5
        var bars = Enumerable.Range(0, 30)
            .Select(i => Bar(i, 100m, 100m, 100m, volume: i + 10))
            .ToList();

        // Last 20 = volumes 20..39 actually (i from 10 to 29, volume i+10).
        // sum = (20+39)*20/2 = 590; avg = 29.5
        Indicators.VolumeSma(bars, 20).Should().Be(29.5m);
    }

    [Fact]
    public void VolumeSma_InsufficientBars_ReturnsZero()
    {
        var bars = Enumerable.Range(0, 5)
            .Select(i => Bar(i, 100m, 100m, 100m, 1m))
            .ToList();

        Indicators.VolumeSma(bars, 20).Should().Be(0m);
    }

    [Fact]
    public void SwingHigh_TwentyBarsAscending_ReturnsMostRecentHigh()
    {
        // bar i has high = 100 + i; last 20 = highs 20..39 -> max 139 (i=29 → 129).
        var bars = Enumerable.Range(0, 30)
            .Select(i => Bar(i, 100m + i, 100m + i, 100m, 1m))
            .ToList();

        Indicators.SwingHigh(bars, 20).Should().Be(129m);
    }

    [Fact]
    public void SwingHigh_PeakOutsideWindow_IgnoresOldSpike()
    {
        // Spike at index 3 (outside the last-20 window). Last 20 bars (index 10..29) max high = 100.
        var bars = new List<Kline>();
        for (var i = 0; i < 30; i++)
        {
            var high = i == 3 ? 500m : 100m;
            bars.Add(Bar(i, 100m, high, 100m, 1m));
        }

        Indicators.SwingHigh(bars, 20).Should().Be(100m);
    }

    [Fact]
    public void SwingHigh_InsufficientBars_ReturnsZero()
    {
        var bars = Enumerable.Range(0, 5)
            .Select(i => Bar(i, 100m, 100m, 100m, 1m))
            .ToList();

        Indicators.SwingHigh(bars, 20).Should().Be(0m);
    }
}
