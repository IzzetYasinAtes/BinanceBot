using BinanceBot.Domain.MarketData;
using BinanceBot.Domain.ValueObjects;
using BinanceBot.Infrastructure.Strategies.Evaluators;
using FluentAssertions;

namespace BinanceBot.Tests.Infrastructure.Strategies;

/// <summary>
/// Sanity checks for the shared indicator helper extracted in ADR-0012 §12.5 (DRY refactor
/// for trend + meanrev evaluators).
/// </summary>
public class IndicatorsTests
{
    private static Kline MakeBar(int seq, decimal close, decimal high, decimal low)
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
            volume: 1m,
            quoteVolume: close,
            tradeCount: 1,
            takerBuyBase: 0m,
            takerBuyQuote: 0m,
            isClosed: true);
    }

    [Fact]
    public void Rsi_AllGains_Returns100()
    {
        var bars = Enumerable.Range(0, 20)
            .Select(i => MakeBar(i, close: 100m + i, high: 100m + i, low: 100m + i))
            .ToList();

        Indicators.Rsi(bars, period: 14).Should().Be(100m);
    }

    [Fact]
    public void Rsi_AllLosses_ReturnsZero()
    {
        var bars = Enumerable.Range(0, 20)
            .Select(i => MakeBar(i, close: 200m - i, high: 200m - i, low: 200m - i))
            .ToList();

        Indicators.Rsi(bars, period: 14).Should().Be(0m);
    }

    [Fact]
    public void Rsi_FlatPrices_ReturnsHundred()
    {
        // No losses, no gains -> avgLoss=0 path returns 100 (matches MeanReversionEvaluator semantics).
        var bars = Enumerable.Range(0, 20).Select(i => MakeBar(i, 100m, 100m, 100m)).ToList();

        Indicators.Rsi(bars, period: 14).Should().Be(100m);
    }

    [Fact]
    public void Rsi_NotEnoughBars_ReturnsFiftyDefault()
    {
        var bars = Enumerable.Range(0, 5).Select(i => MakeBar(i, 100m + i, 100m + i, 100m + i)).ToList();

        Indicators.Rsi(bars, period: 14).Should().Be(50m);
    }

    [Fact]
    public void Ema_FlatSeries_EqualsClosePrice()
    {
        var bars = Enumerable.Range(0, 20).Select(i => MakeBar(i, 100m, 100m, 100m)).ToList();

        Indicators.Ema(bars, period: 8, endIndex: bars.Count - 1).Should().Be(100m);
    }

    [Fact]
    public void Ema_FastReactsBeforeSlow()
    {
        // Step jump mid-series: long flat tail at 100, then a single jump to 200 just before
        // the latest bar. Fast(3) places more weight on the recent jump than Slow(8), so
        // immediately after the jump fast > slow.
        var seq = new List<decimal>();
        for (var i = 0; i < 18; i++) seq.Add(100m);
        seq.Add(150m);
        seq.Add(200m);
        var bars = seq.Select((c, i) => MakeBar(i, c, c, c)).ToList();

        var fast = Indicators.Ema(bars, period: 3, endIndex: bars.Count - 1);
        var slow = Indicators.Ema(bars, period: 8, endIndex: bars.Count - 1);

        fast.Should().BeGreaterThan(slow);
    }

    [Fact]
    public void Atr_FlatBars_ReturnsZero()
    {
        var bars = Enumerable.Range(0, 20).Select(i => MakeBar(i, 100m, 100m, 100m)).ToList();

        Indicators.Atr(bars, period: 14).Should().Be(0m);
    }

    [Fact]
    public void BollingerBands_FlatSeries_AllEqual()
    {
        var bars = Enumerable.Range(0, 25).Select(i => MakeBar(i, 100m, 100m, 100m)).ToList();
        var (mean, upper, lower) = Indicators.BollingerBands(bars, period: 20, stdDevMultiplier: 2m);

        mean.Should().Be(100m);
        upper.Should().Be(100m);
        lower.Should().Be(100m);
    }

    [Fact]
    public void BollingerBands_RisingSeries_UpperGreaterThanLower()
    {
        var bars = Enumerable.Range(0, 25)
            .Select(i => MakeBar(i, 100m + i, 100m + i, 100m + i))
            .ToList();
        var (mean, upper, lower) = Indicators.BollingerBands(bars, period: 20, stdDevMultiplier: 2m);

        upper.Should().BeGreaterThan(mean);
        mean.Should().BeGreaterThan(lower);
    }
}
