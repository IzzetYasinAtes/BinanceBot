using BinanceBot.Domain.MarketData;
using BinanceBot.Infrastructure.Strategies.Evaluators;

namespace BinanceBot.Infrastructure.Strategies.Patterns;

/// <summary>
/// ADR-0014 §14.3: shared filter primitives used across the 14 detectors.
/// Pure functions — no side effects, no allocations beyond local <c>decimal</c>.
/// Reuses <see cref="Indicators"/> for RSI / EMA / ATR (decimal arithmetic).
/// </summary>
internal static class PatternFilters
{
    /// <summary>
    /// Simple-moving-average volume across the last <paramref name="lookback"/> closed bars.
    /// Returns 0 when history is short — caller must handle the volume-confirm fallback.
    /// </summary>
    public static decimal AverageVolume(IReadOnlyList<Kline> bars, int lookback)
    {
        if (bars.Count < lookback)
        {
            return 0m;
        }
        decimal sum = 0m;
        for (var i = bars.Count - lookback; i < bars.Count; i++)
        {
            sum += bars[i].Volume;
        }
        return sum / lookback;
    }

    /// <summary>
    /// True when the bar's volume meets the strategy's confirmation threshold
    /// (e.g. <c>1.5 ×</c> the rolling average for BTC/BNB, <c>1.8 ×</c> for XRP).
    /// </summary>
    public static bool VolumeConfirmed(Kline bar, decimal avg, decimal multiplier)
        => avg > 0m && bar.Volume >= avg * multiplier;

    /// <summary>
    /// True when the latest RSI sits inside the inclusive band — used to reject reversals
    /// during extreme overbought/oversold regimes (whipsaw).
    /// </summary>
    public static bool RsiInBand(IReadOnlyList<Kline> bars, int period, decimal min, decimal max)
    {
        if (bars.Count < period + 2)
        {
            return false;
        }
        var rsi = Indicators.Rsi(bars, period);
        return rsi >= min && rsi <= max;
    }

    /// <summary>
    /// True when the latest close is above the EMA of <paramref name="emaPeriod"/>.
    /// </summary>
    public static bool TrendUp(IReadOnlyList<Kline> bars, int emaPeriod)
        => bars[^1].ClosePrice > Indicators.Ema(bars, emaPeriod, bars.Count - 1);

    /// <summary>
    /// True when the latest close is below the EMA — mirror of <see cref="TrendUp"/>.
    /// </summary>
    public static bool TrendDown(IReadOnlyList<Kline> bars, int emaPeriod)
        => bars[^1].ClosePrice < Indicators.Ema(bars, emaPeriod, bars.Count - 1);

    /// <summary>Clamp to the [0, 1] interval (confidence safety net).</summary>
    public static decimal Clamp01(decimal v) => v < 0m ? 0m : (v > 1m ? 1m : v);

    /// <summary>
    /// 3-bar swing-low pivot detector — bar at <paramref name="i"/> is a pivot low when
    /// it is strictly lower than both neighbours. Used by Double Bottom + Flag/Triangle.
    /// </summary>
    public static bool IsPivotLow(IReadOnlyList<Kline> bars, int i)
        => i > 0 && i < bars.Count - 1
           && bars[i].LowPrice < bars[i - 1].LowPrice
           && bars[i].LowPrice < bars[i + 1].LowPrice;

    /// <summary>
    /// 3-bar swing-high pivot detector — mirror of <see cref="IsPivotLow"/>.
    /// </summary>
    public static bool IsPivotHigh(IReadOnlyList<Kline> bars, int i)
        => i > 0 && i < bars.Count - 1
           && bars[i].HighPrice > bars[i - 1].HighPrice
           && bars[i].HighPrice > bars[i + 1].HighPrice;
}
