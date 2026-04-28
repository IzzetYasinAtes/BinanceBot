using BinanceBot.Domain.MarketData;

namespace BinanceBot.Application.Strategies.Indicators;

/// <summary>
/// ADR-0015 §15.6 + ADR-0018 §18.11. Evaluator-facing port for pre-computed market
/// indicators. Infrastructure impl maintains rolling 1m + 1h + 30s + 5m buffers and
/// exposes a snapshot per-symbol when warmup completes. Evaluator is snapshot-consumer
/// only — it does not own the rolling buffer, the Channel subscription, or the REST
/// backfill wiring.
/// </summary>
public interface IMarketIndicatorService
{
    /// <summary>
    /// ADR-0015 §15.6. Returns the most recent closed-bar indicator snapshot
    /// (1m VWAP + 1h EMA21 + SwingHigh20) for <paramref name="symbol"/>, or
    /// <c>null</c> when the warmup (1440 × 1m + 21 × 1h) is incomplete.
    /// Eski VwapEma path'i; Loop 23+ seed reset sonrası çağırılmaz.
    /// </summary>
    MarketIndicatorSnapshot? TryGetSnapshot(string symbol);

    /// <summary>
    /// ADR-0018 §18.11 + Loop 37. Returns the micro-scalper snapshot
    /// (rolling 15-bar VWAP + EMA20 now/prev + VolumeSMA20 + ATR14) for
    /// <paramref name="symbol"/> computed from the requested
    /// <paramref name="interval"/> buffer, or <c>null</c> when the warmup
    /// (en az 21 bar) tamamlanmadı. Supported intervals: <see cref="KlineInterval.OneMinute"/>
    /// (default; legacy MicroScalper path) and <see cref="KlineInterval.FiveMinutes"/>
    /// (Loop 37 AtrScalper 5m path). Unsupported interval ⇒ <c>null</c>.
    /// </summary>
    MicroScalperIndicatorSnapshot? TryGetMicroScalperSnapshot(
        string symbol,
        KlineInterval interval = KlineInterval.OneMinute);

    /// <summary>
    /// Loop 41 AR-GE — Donchian Channel Breakout (15m) snapshot. Donchian periyodu,
    /// volume penceresi ve ATR periyodu evaluator parametrelerinden gelir; service
    /// tarafı yalnızca <see cref="KlineInterval.FifteenMinutes"/> rolling buffer'ından
    /// hesaplama yapar. Warmup eşiği <c>max(donchianPeriod, volumeWindow, atrPeriod+1)</c>
    /// karşılanmadan ⇒ <c>null</c>. Buffer yoksa veya symbol takip edilmiyorsa
    /// ⇒ <c>null</c>.
    /// </summary>
    DonchianBreakoutIndicatorSnapshot? TryGetDonchianBreakoutSnapshot(
        string symbol,
        int donchianPeriod,
        int volumeWindow,
        int atrPeriod);

    /// <summary>
    /// Loop 44 AR-GE — Bollinger Bands Mean Reversion (15m) snapshot. BB periyodu +
    /// std multiplier, RSI periyodu, volume penceresi ve ATR periyodu evaluator
    /// parametrelerinden gelir; service tarafı yalnızca
    /// <see cref="KlineInterval.FifteenMinutes"/> rolling buffer'ından hesaplama
    /// yapar.
    ///
    /// Donchian'dan farklı pencere semantiği: Donchian "current bar pencerede DEĞİL"
    /// (closed-window breakout), BB "current bar pencerede VAR" (standard BB period
    /// = son N bar dahil). Aynı şekilde VolumeAvg/Std ve RSI hesabında da current
    /// bar dahildir; yalnız ATR (period+1 bar gerektirir) önceki bar'ı önek olarak
    /// kullanır.
    ///
    /// Warmup eşiği: <c>max(bbPeriod, rsiPeriod+1, volumeWindow, atrPeriod+1)</c>
    /// karşılanmadan ⇒ <c>null</c>. Buffer yoksa, symbol takip edilmiyorsa veya
    /// herhangi bir parametre &lt;= 0 ise ⇒ <c>null</c>.
    /// </summary>
    BbMeanReversionIndicatorSnapshot? TryGetBbMeanReversionSnapshot(
        string symbol,
        int bbPeriod,
        decimal bbStdMultiplier,
        int rsiPeriod,
        int volumeWindow,
        int atrPeriod);

    /// <summary>
    /// Loop 46 AR-GE — EMA9/EMA21 crossover scalper (1m) snapshot. EMA fast/slow
    /// periyotları, RSI periyodu, volume penceresi ve ATR periyodu evaluator
    /// parametrelerinden gelir; service yalnızca <see cref="KlineInterval.OneMinute"/>
    /// rolling buffer'ından hesaplama yapar (1m WS @kline_1m + REST 1440-bar
    /// warmup zaten doludur).
    ///
    /// BB / Donchian semantiğinden farklı: tüm indikatörler current bar DAHİL
    /// hesaplanır (EMA "now" current bar dahil son N bar üzerinde, EMA "prev"
    /// current bar HARİÇ son N bar üzerinde — slope/cross trace için). RSI ve
    /// ATR period+1 bar gerektirir (close-to-close diff / TR prev-close).
    ///
    /// Warmup eşiği:
    /// <c>max(emaSlowPeriod + 1, rsiPeriod + 1, volumeWindow, atrPeriod + 1)</c>.
    /// Eşik karşılanmadıysa, symbol takip edilmiyorsa veya parametre &lt;= 0 ise
    /// <c>null</c> döner.
    /// </summary>
    EmaScalperIndicatorSnapshot? TryGetEmaScalperSnapshot(
        string symbol,
        int emaFastPeriod,
        int emaSlowPeriod,
        int rsiPeriod,
        int volumeWindow,
        int atrPeriod);
}
