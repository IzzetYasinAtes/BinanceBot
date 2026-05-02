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

    /// <summary>
    /// Loop 80 — ADX min bar contract: insufficient history (&lt; 2*period+1)
    /// must return 0 so the evaluator can treat it as "warmup not done — gate
    /// open / unavailable" without false-skipping during the first 29 bars.
    /// </summary>
    [Fact]
    public void Adx_NotEnoughBars_ReturnsZero()
    {
        var bars = Enumerable.Range(0, 20).Select(i => MakeBar(i, 100m, 100m, 100m)).ToList();

        Indicators.Adx(bars, period: 14).Should().Be(0m);
    }

    /// <summary>
    /// Loop 80 — ADX flat-bars contract: zero TR ⇒ DX undefined → returns 0.
    /// (Pure decimal — no NaN risk; ComputeDx guards trS &lt;= 0.)
    /// </summary>
    [Fact]
    public void Adx_FlatBars_ReturnsZero()
    {
        var bars = Enumerable.Range(0, 60).Select(i => MakeBar(i, 100m, 100m, 100m)).ToList();

        Indicators.Adx(bars, period: 14).Should().Be(0m);
    }

    /// <summary>
    /// Loop 80 — ADX strong-trend contract: a long monotonic uptrend (every
    /// bar makes a new high above the previous high, low above the previous
    /// low, no downward TR) is the textbook "ADX should rise toward 100"
    /// scenario. We assert &gt; 25 (the canonical "trending" Wilder threshold);
    /// the actual converged value depends on the smoothing horizon, but the
    /// monotonic case is well above the gate.
    /// </summary>
    [Fact]
    public void Adx_StrongUptrend_ExceedsTrendingThreshold()
    {
        // Monotonic uptrend 60 bars. Each bar: high = (i+1)*1, low = i*1,
        // close = (i + 0.5). Plenty of bars for warmup + smoothing convergence.
        var bars = Enumerable.Range(0, 60)
            .Select(i => MakeBar(
                seq: i,
                close: 100m + i + 0.5m,
                high: 100m + i + 1m,
                low: 100m + i))
            .ToList();

        var adx = Indicators.Adx(bars, period: 14);

        adx.Should().BeGreaterThan(25m);
        adx.Should().BeLessThanOrEqualTo(100m);
    }

    /// <summary>
    /// Loop 81 — MACD line not enough bars returns 0 (warmup contract).
    /// </summary>
    [Fact]
    public void Macd_NotEnoughBars_ReturnsZero()
    {
        var bars = Enumerable.Range(0, 20).Select(i => MakeBar(i, 100m + i, 100m + i, 100m + i)).ToList();
        Indicators.Macd(bars, fast: 12, slow: 26).Should().Be(0m);
    }

    /// <summary>
    /// Loop 81 — MACD: rising series → fast EMA &gt; slow EMA → MACD &gt; 0.
    /// </summary>
    [Fact]
    public void Macd_RisingSeries_PositiveMacd()
    {
        var bars = Enumerable.Range(0, 60)
            .Select(i => MakeBar(i, close: 100m + i, high: 100m + i, low: 100m + i))
            .ToList();
        Indicators.Macd(bars, fast: 12, slow: 26).Should().BeGreaterThan(0m);
    }

    /// <summary>
    /// Loop 81 — MACD: falling series → fast &lt; slow → MACD &lt; 0.
    /// </summary>
    [Fact]
    public void Macd_FallingSeries_NegativeMacd()
    {
        var bars = Enumerable.Range(0, 60)
            .Select(i => MakeBar(i, close: 200m - i, high: 200m - i, low: 200m - i))
            .ToList();
        Indicators.Macd(bars, fast: 12, slow: 26).Should().BeLessThan(0m);
    }
}
