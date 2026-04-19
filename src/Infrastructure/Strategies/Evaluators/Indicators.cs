using BinanceBot.Domain.MarketData;

namespace BinanceBot.Infrastructure.Strategies.Evaluators;

/// <summary>
/// Pure technical-indicator helpers shared by strategy evaluators (ADR-0012 §12.5 DRY refactor).
/// All inputs are <see cref="IReadOnlyList{Kline}"/> sequences ordered oldest-first; methods do
/// not mutate inputs and have no side effects. Decimal arithmetic throughout — never double.
/// </summary>
internal static class Indicators
{
    /// <summary>
    /// Wilder-style RSI over the last <paramref name="period"/> close-to-close differences.
    /// Returns <c>50</c> when there is not enough history and <c>100</c> when no losses occurred.
    /// </summary>
    public static decimal Rsi(IReadOnlyList<Kline> bars, int period)
    {
        if (bars.Count < period + 1)
        {
            return 50m;
        }

        decimal gainSum = 0m, lossSum = 0m;
        var start = bars.Count - period;
        for (var i = start; i < bars.Count; i++)
        {
            var diff = bars[i].ClosePrice - bars[i - 1].ClosePrice;
            if (diff >= 0m)
            {
                gainSum += diff;
            }
            else
            {
                lossSum -= diff;
            }
        }

        var avgGain = gainSum / period;
        var avgLoss = lossSum / period;
        if (avgLoss == 0m)
        {
            return 100m;
        }
        var rs = avgGain / avgLoss;
        return 100m - (100m / (1m + rs));
    }

    /// <summary>
    /// Exponential moving average of close prices computed up to and including <paramref name="endIndex"/>.
    /// When the window does not yet have <paramref name="period"/> bars, falls back to the close at <c>endIndex</c>.
    /// </summary>
    public static decimal Ema(IReadOnlyList<Kline> bars, int period, int endIndex)
    {
        if (endIndex < period - 1)
        {
            return bars[endIndex].ClosePrice;
        }

        decimal alpha = 2m / (period + 1);
        decimal ema = bars[endIndex - period + 1].ClosePrice;
        for (var i = endIndex - period + 2; i <= endIndex; i++)
        {
            ema = alpha * bars[i].ClosePrice + (1 - alpha) * ema;
        }
        return ema;
    }

    /// <summary>
    /// Average True Range over the most recent <paramref name="period"/> bars (Wilder/SMA blend).
    /// Returns 0 when history is insufficient.
    /// </summary>
    public static decimal Atr(IReadOnlyList<Kline> bars, int period)
    {
        if (bars.Count < period + 1)
        {
            return 0m;
        }

        var start = bars.Count - period;
        decimal sum = 0m;
        for (var i = start; i < bars.Count; i++)
        {
            var high = bars[i].HighPrice;
            var low = bars[i].LowPrice;
            var prevClose = bars[i - 1].ClosePrice;
            var tr = Math.Max(
                high - low,
                Math.Max(Math.Abs(high - prevClose), Math.Abs(low - prevClose)));
            sum += tr;
        }
        return sum / period;
    }

    /// <summary>
    /// ADR-0015 §15.6. Rolling typical-price (HLC/3) volume-weighted average over the full
    /// <paramref name="bars"/> window. Returns <c>0</c> when the cumulative volume is zero
    /// (pre-warmup / all-flat window) — caller must treat that as no-signal.
    /// Pure decimal math — no NaN/infinity risk.
    /// </summary>
    public static decimal Vwap(IReadOnlyList<Kline> bars)
    {
        if (bars.Count == 0)
        {
            return 0m;
        }

        decimal tpvSum = 0m;
        decimal volSum = 0m;
        for (var i = 0; i < bars.Count; i++)
        {
            var b = bars[i];
            var typical = (b.HighPrice + b.LowPrice + b.ClosePrice) / 3m;
            tpvSum += typical * b.Volume;
            volSum += b.Volume;
        }

        return volSum > 0m ? tpvSum / volSum : 0m;
    }

    /// <summary>
    /// Simple moving average of <c>Volume</c> across the most recent
    /// <paramref name="period"/> bars. Returns <c>0</c> when history is insufficient.
    /// </summary>
    public static decimal VolumeSma(IReadOnlyList<Kline> bars, int period)
    {
        if (period <= 0 || bars.Count < period)
        {
            return 0m;
        }

        decimal sum = 0m;
        var start = bars.Count - period;
        for (var i = start; i < bars.Count; i++)
        {
            sum += bars[i].Volume;
        }
        return sum / period;
    }

    /// <summary>
    /// Highest <c>High</c> among the last <paramref name="lookback"/> bars. Returns <c>0</c>
    /// when history is insufficient — evaluators treat that as no-signal.
    /// </summary>
    public static decimal SwingHigh(IReadOnlyList<Kline> bars, int lookback)
    {
        if (lookback <= 0 || bars.Count < lookback)
        {
            return 0m;
        }

        var start = bars.Count - lookback;
        var high = bars[start].HighPrice;
        for (var i = start + 1; i < bars.Count; i++)
        {
            if (bars[i].HighPrice > high)
            {
                high = bars[i].HighPrice;
            }
        }
        return high;
    }

    /// <summary>
    /// Bollinger Bands (mean ± stdDev * multiplier) over the most recent <paramref name="period"/> closes.
    /// Falls back to a flat band centred on the latest close when history is insufficient.
    /// </summary>
    public static (decimal Mean, decimal Upper, decimal Lower) BollingerBands(
        IReadOnlyList<Kline> bars, int period, decimal stdDevMultiplier)
    {
        if (bars.Count < period)
        {
            var c = bars[^1].ClosePrice;
            return (c, c, c);
        }

        decimal sum = 0m;
        var start = bars.Count - period;
        for (var i = start; i < bars.Count; i++)
        {
            sum += bars[i].ClosePrice;
        }
        var mean = sum / period;

        decimal sqSum = 0m;
        for (var i = start; i < bars.Count; i++)
        {
            var d = bars[i].ClosePrice - mean;
            sqSum += d * d;
        }
        var variance = sqSum / period;
        var stdDev = (decimal)Math.Sqrt((double)variance);
        return (mean, mean + stdDevMultiplier * stdDev, mean - stdDevMultiplier * stdDev);
    }
}
