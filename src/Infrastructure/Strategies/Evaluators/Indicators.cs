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
    /// Loop 41 — Donchian channel: highest high &amp; lowest low over the most recent
    /// <paramref name="period"/> bars (current bar dahil değil, kapanmış pencereyi
    /// hesaplayan caller'dan beklenir). Yetersiz history (&lt; period) ⇒ <c>(0, 0)</c>;
    /// caller no-signal olarak ele almalıdır.
    /// </summary>
    public static (decimal High, decimal Low) Donchian(IReadOnlyList<Kline> bars, int period)
    {
        if (period <= 0 || bars.Count < period)
        {
            return (0m, 0m);
        }

        var start = bars.Count - period;
        var high = bars[start].HighPrice;
        var low = bars[start].LowPrice;
        for (var i = start + 1; i < bars.Count; i++)
        {
            if (bars[i].HighPrice > high) high = bars[i].HighPrice;
            if (bars[i].LowPrice < low) low = bars[i].LowPrice;
        }
        return (high, low);
    }

    /// <summary>
    /// Loop 41 — population standard deviation of <c>Volume</c> across the most recent
    /// <paramref name="period"/> bars; <paramref name="mean"/> caller tarafından
    /// hesaplanmış değer (örn. <see cref="VolumeSma"/>). Yetersiz history ⇒ <c>0</c>;
    /// caller bu durumda Z-Score üretmemelidir.
    /// </summary>
    public static decimal VolumeStdev(IReadOnlyList<Kline> bars, int period, decimal mean)
    {
        if (period <= 0 || bars.Count < period)
        {
            return 0m;
        }

        decimal sqSum = 0m;
        var start = bars.Count - period;
        for (var i = start; i < bars.Count; i++)
        {
            var d = bars[i].Volume - mean;
            sqSum += d * d;
        }
        var variance = sqSum / period;
        return variance <= 0m ? 0m : (decimal)Math.Sqrt((double)variance);
    }

    /// <summary>
    /// Loop 67 KMS — arithmetic mean of <c>TradeCount</c> across the most recent
    /// <paramref name="period"/> bars. Used by KlineMomentumSpread5m for the
    /// "trade count surge" filter (currentTradeCount &gt; avgTradeCount × N).
    /// Returns <c>0</c> when history is insufficient (caller treats that as
    /// "warmup not done — skip").
    /// </summary>
    public static decimal TradeCountAvg(IReadOnlyList<Kline> bars, int period)
    {
        if (period <= 0 || bars.Count < period)
        {
            return 0m;
        }

        decimal sum = 0m;
        var start = bars.Count - period;
        for (var i = start; i < bars.Count; i++)
        {
            sum += bars[i].TradeCount;
        }
        return sum / period;
    }

    /// <summary>
    /// Loop 80 — Wilder ADX (Average Directional Index). Classic recipe:
    /// <list type="number">
    ///   <item>+DM = max(High - prevHigh, 0) when High - prevHigh &gt; prevLow - Low; else 0.</item>
    ///   <item>-DM = max(prevLow - Low, 0) when prevLow - Low &gt; High - prevHigh; else 0.</item>
    ///   <item>TR  = max(High-Low, |High-prevClose|, |Low-prevClose|).</item>
    ///   <item>SmoothedTR / +DM / -DM via Wilder recursion: <c>val = prev - prev/period + curr</c>
    ///         (seed = arithmetic sum of the first <c>period</c> values).</item>
    ///   <item>+DI = +DM_smooth / TR_smooth × 100 ; -DI = -DM_smooth / TR_smooth × 100.</item>
    ///   <item>DX = |+DI - -DI| / (+DI + -DI) × 100.</item>
    ///   <item>ADX = Wilder EMA-period of DX (seed = arithmetic mean of the first <c>period</c> DX values).</item>
    /// </list>
    /// Returns <c>0</c> when history is insufficient (caller treats as
    /// "warmup not done — gate disabled / unavailable"). Min bars: <c>2 × period</c>
    /// (period TR + period DX). Pure decimal math throughout.
    /// </summary>
    public static decimal Adx(IReadOnlyList<Kline> bars, int period)
    {
        if (period <= 0) return 0m;
        // We need (period+1) raw bars to make `period` TR+DM samples (each needs prev),
        // then `period` DX values to seed the ADX EMA → 2*period + 1 minimum.
        var minBars = 2 * period + 1;
        if (bars.Count < minBars) return 0m;

        // Step 1 — raw +DM, -DM, TR per bar (i = 1..N-1).
        var n = bars.Count;
        var plusDm = new decimal[n];
        var minusDm = new decimal[n];
        var tr = new decimal[n];
        for (var i = 1; i < n; i++)
        {
            var upMove = bars[i].HighPrice - bars[i - 1].HighPrice;
            var downMove = bars[i - 1].LowPrice - bars[i].LowPrice;
            plusDm[i] = (upMove > downMove && upMove > 0m) ? upMove : 0m;
            minusDm[i] = (downMove > upMove && downMove > 0m) ? downMove : 0m;

            var hl = bars[i].HighPrice - bars[i].LowPrice;
            var hpc = Math.Abs(bars[i].HighPrice - bars[i - 1].ClosePrice);
            var lpc = Math.Abs(bars[i].LowPrice - bars[i - 1].ClosePrice);
            tr[i] = Math.Max(hl, Math.Max(hpc, lpc));
        }

        // Step 2 — seed Wilder smoothed values at index `period` (sum of i=1..period).
        decimal trSmooth = 0m;
        decimal plusDmSmooth = 0m;
        decimal minusDmSmooth = 0m;
        for (var i = 1; i <= period; i++)
        {
            trSmooth += tr[i];
            plusDmSmooth += plusDm[i];
            minusDmSmooth += minusDm[i];
        }

        // Step 3 — DX series. First DX at index `period` (after seed).
        // Subsequent: Wilder recursion val = prev - prev/period + curr.
        var dxValues = new List<decimal>(n - period);
        var firstDx = ComputeDx(plusDmSmooth, minusDmSmooth, trSmooth);
        dxValues.Add(firstDx);

        for (var i = period + 1; i < n; i++)
        {
            trSmooth = trSmooth - (trSmooth / period) + tr[i];
            plusDmSmooth = plusDmSmooth - (plusDmSmooth / period) + plusDm[i];
            minusDmSmooth = minusDmSmooth - (minusDmSmooth / period) + minusDm[i];

            dxValues.Add(ComputeDx(plusDmSmooth, minusDmSmooth, trSmooth));
        }

        // Step 4 — ADX = Wilder EMA-period of DX, seeded by mean of first period DX values.
        if (dxValues.Count < period) return 0m;

        decimal adx = 0m;
        for (var k = 0; k < period; k++)
        {
            adx += dxValues[k];
        }
        adx /= period;

        for (var k = period; k < dxValues.Count; k++)
        {
            adx = (adx * (period - 1) + dxValues[k]) / period;
        }

        return adx;

        static decimal ComputeDx(decimal pdmS, decimal mdmS, decimal trS)
        {
            if (trS <= 0m) return 0m;
            var plusDi = pdmS / trS * 100m;
            var minusDi = mdmS / trS * 100m;
            var sum = plusDi + minusDi;
            if (sum <= 0m) return 0m;
            return Math.Abs(plusDi - minusDi) / sum * 100m;
        }
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

    /// <summary>
    /// Loop 81 — BBW window. Returns the minimum BBW value over the last
    /// <paramref name="window"/> consecutive bars, where each BBW is computed
    /// from BollingerBands(period, stdDevMultiplier) ending at that bar.
    /// Used by <c>EmaSqueezeBreakDetector</c> to detect "squeeze release"
    /// (current BBW expanded beyond a recent compression minimum).
    /// Returns <c>0</c> when history insufficient (caller treats as no-signal).
    /// </summary>
    public static decimal BollingerBandWidthMin(
        IReadOnlyList<Kline> bars, int period, decimal stdDevMultiplier, int window)
    {
        if (window <= 0 || bars.Count < period + window - 1)
        {
            return 0m;
        }

        decimal min = decimal.MaxValue;
        for (var k = 0; k < window; k++)
        {
            // Pencerenin "k bar geride biten" snapshot'ı için bars[..bars.Count - k]
            // alt kümesi yeterli (BollingerBands son <c>period</c> bar'ı tüketir).
            var endExclusive = bars.Count - k;
            if (endExclusive < period) break;

            // BollingerBands son `period` bar'ı tüketir; aynı algoritmayı tekrar
            // yazmak yerine sub-list materialise et.
            var sub = new List<Kline>(period);
            for (var i = endExclusive - period; i < endExclusive; i++)
            {
                sub.Add(bars[i]);
            }
            var (mean, upper, lower) = BollingerBands(sub, period, stdDevMultiplier);
            if (mean <= 0m) continue;
            var bbw = (upper - lower) / mean;
            if (bbw < min) min = bbw;
        }

        return min == decimal.MaxValue ? 0m : min;
    }

    /// <summary>
    /// Loop 81 — Simple moving average of <c>Volume</c> alias for
    /// <see cref="VolumeSma"/> ile aynı; semantik isim (volume-surge gate kullanır).
    /// Returns <c>0</c> when history insufficient.
    /// </summary>
    public static decimal AverageVolume(IReadOnlyList<Kline> bars, int period) =>
        VolumeSma(bars, period);

    /// <summary>
    /// Loop 81 — MACD line: <c>EMA(close, fast) - EMA(close, slow)</c>. Last
    /// closed bar dahil. Yetersiz history (&lt; <paramref name="slow"/> + 1) ⇒
    /// <c>0</c>. Signal line + histogram dahil değil — Loop 81 sadece
    /// zero-line cross kullanır.
    /// </summary>
    public static decimal Macd(IReadOnlyList<Kline> bars, int fast, int slow)
    {
        if (fast <= 0 || slow <= 0 || fast >= slow) return 0m;
        if (bars.Count < slow + 1) return 0m;

        var endIndex = bars.Count - 1;
        var emaFast = Ema(bars, period: fast, endIndex: endIndex);
        var emaSlow = Ema(bars, period: slow, endIndex: endIndex);
        return emaFast - emaSlow;
    }
}
