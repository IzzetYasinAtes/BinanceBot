using System.Collections.Concurrent;
using BinanceBot.Application.Abstractions.Binance;
using BinanceBot.Application.Strategies.Indicators;
using BinanceBot.Domain.MarketData;
using BinanceBot.Domain.SystemEvents.Events;
using BinanceBot.Domain.ValueObjects;
using BinanceBot.Infrastructure.Binance;
using BinanceBot.Infrastructure.Strategies.Evaluators;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BinanceBot.Infrastructure.Strategies.Indicators;

/// <summary>
/// ADR-0015 §15.6 + §15.7 + ADR-0018 §18.11. Maintains per-symbol rolling buffers
/// of closed 1m, 1h, and 30s bars; computes <see cref="MarketIndicatorSnapshot"/>
/// (eski VwapEma path) ve <see cref="MicroScalperIndicatorSnapshot"/> (Loop 23+
/// MicroScalper path) on demand for the evaluators.
///
/// Lifecycle:
///   1. On host start, warm up 1m (1440 bars), 1h (60 bars) ve 30s (50 bars)
///      per symbol via REST <c>GET /api/v3/klines</c>. Per-symbol failures are
///      logged and skipped — the service is best-effort and never fail-fasts
///      the host.
///   2. Consume the shared <see cref="IBinanceMarketStream"/> kline channel and
///      upsert closed bars into the appropriate buffer.
///
/// Thread model:
///   - Writes (REST warmup + WS consumer) are serialised per (symbol, interval) by
///     a lightweight lock around <see cref="IndicatorRollingBuffer"/>.
///   - Reads (<see cref="TryGetSnapshot"/>, <see cref="TryGetMicroScalperSnapshot"/>)
///     take the same lock, copy the buffer contents and compute indicators — latency
///     is O(bars) which is dominated by the 1440-bar VWAP sum (&lt;1ms in practice).
/// </summary>
public sealed class MarketIndicatorService : IMarketIndicatorService, IHostedService
{
    // ADR-0015 §15.2 + §15.6 defaults. Parameters here are strictly service-level
    // (buffer sizing); strategy-level parameters live in evaluator JSON.
    internal const int OneMinuteBufferCapacity = 1440; // rolling 24h VWAP window
    internal const int OneHourBufferCapacity = 60;     // 21-period EMA needs ~50 bars warm

    // ADR-0018 §18.11 — 30sn bar buffer. MicroScalper için en fazla gerekli lookback:
    //   - EMA20 warm: ~40 bar (2× period, ema stabilize)
    //   - VolumeSMA20: 20 bar
    //   - VWAP 15-bar rolling: 15 bar
    // 50 bar safety. Binance REST limit endpoint 1 sayfa yeter (=< 1000).
    internal const int ThirtySecondBufferCapacity = 50;

    // Loop 37 — 5m bar buffer (AtrScalper timeframe migration). 5m timeframe'de
    // MaxHold (default 8 bar) × bar body potansiyeli 1m'e göre 5× artar, TP hit
    // olasılığı matematiksel olarak sağlanır. Gerekli lookback:
    //   - EMA20 warm: ~40 bar
    //   - VolumeSMA20: 20 bar
    //   - VWAP 15-bar rolling: 15 bar
    //   - ATR14: 15 bar
    // 100 bar safety (~8 saat geçmiş). Binance REST limit 1 sayfa yeter.
    internal const int FiveMinuteBufferCapacity = 100;

    // Loop 41 — 15m bar buffer (Donchian Breakout 15m). Gerekli lookback:
    //   - Donchian 20: 20 bar
    //   - VolumeAvg+Std 20: 20 bar
    //   - ATR14: 15 bar
    //   - +1 current bar
    // 80 bar safety (~20 saat geçmiş). 1 sayfa REST yeter (limit 1000).
    internal const int FifteenMinuteBufferCapacity = 80;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IBinanceMarketStream _stream;
    private readonly IOptionsMonitor<BinanceOptions> _options;
    private readonly ILogger<MarketIndicatorService> _logger;

    private readonly ConcurrentDictionary<string, SymbolState> _state =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly CancellationTokenSource _cts = new();
    private Task? _consumerTask;

    public MarketIndicatorService(
        IServiceScopeFactory scopeFactory,
        IBinanceMarketStream stream,
        IOptionsMonitor<BinanceOptions> options,
        ILogger<MarketIndicatorService> logger)
    {
        _scopeFactory = scopeFactory;
        _stream = stream;
        _options = options;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var symbols = _options.CurrentValue.Symbols
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var symbol in symbols)
        {
            _state.TryAdd(symbol.ToUpperInvariant(), new SymbolState());
        }

        // REST warmup and WS consumer both run in the background so StartAsync
        // never blocks host startup on external I/O.
        _consumerTask = Task.Run(() => RunAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try { _cts.Cancel(); } catch (ObjectDisposedException) { }

        if (_consumerTask is not null)
        {
            try
            {
                await Task.WhenAny(_consumerTask, Task.Delay(Timeout.Infinite, cancellationToken));
            }
            catch
            {
                // Swallow — shutdown must not propagate.
            }
        }

        _cts.Dispose();
    }

    public MarketIndicatorSnapshot? TryGetSnapshot(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return null;
        }

        if (!_state.TryGetValue(symbol, out var state))
        {
            return null;
        }

        lock (state.SyncRoot)
        {
            var oneMinuteBars = state.OneMinute.Snapshot();
            var oneHourBars = state.OneHour.Snapshot();

            // ADR-0015 §15.7: snapshot is null until the warmup budget is met on both
            // intervals. Evaluator early-returns on null — no signal produced before
            // rolling 24h VWAP + 21h EMA21 are available.
            if (oneMinuteBars.Count < 21 || oneHourBars.Count < 22)
            {
                return null;
            }

            var klines1m = ToKlineList(oneMinuteBars);
            var klines1h = ToKlineList(oneHourBars);

            var vwap = Evaluators.Indicators.Vwap(klines1m);
            var volumeSma20 = Evaluators.Indicators.VolumeSma(klines1m, 20);
            var swingHigh20 = Evaluators.Indicators.SwingHigh(klines1m, 20);
            var ema1hNow = Evaluators.Indicators.Ema(klines1h, period: 21, endIndex: klines1h.Count - 1);
            var ema1hPrev = Evaluators.Indicators.Ema(klines1h, period: 21, endIndex: klines1h.Count - 2);

            var lastBar = klines1m[^1];
            var prevBar = klines1m[^2];

            return new MarketIndicatorSnapshot(
                Vwap: vwap,
                PrevBarClose: prevBar.ClosePrice,
                LastBarClose: lastBar.ClosePrice,
                LastBarVolume: lastBar.Volume,
                VolumeSma20: volumeSma20,
                Ema1h21Now: ema1hNow,
                Ema1h21Prev: ema1hPrev,
                SwingHigh20: swingHigh20,
                AsOf: lastBar.CloseTime);
        }
    }

    /// <summary>
    /// ADR-0018 §18.11 + Loop 37 — MicroScalper/AtrScalper snapshot'ı. Interval
    /// parametresi ile hangi rolling buffer'dan okuyacağı seçilir.
    ///
    /// Loop 24 runtime bug-fix: Binance SPOT WebSocket <c>@kline_30s</c>'i
    /// desteklemez (bkz. binance-spot-api-docs: valid kline intervals =
    /// 1s,1m,3m,5m,15m,30m,1h,…). Testnet SPOT stream 30s subscription'ını
    /// sessizce reddediyordu ve <c>state.ThirtySecond</c> buffer'ı hiçbir zaman
    /// dolmuyordu (20 dk runtime = 0 bar). Evaluator path bu yüzden hiç
    /// çağrılmadı (0 emit, 0 skip).
    ///
    /// Geçici çözüm: <see cref="KlineInterval.OneMinute"/> default'u
    /// <c>state.OneMinute</c> buffer'ından hesaplanıyor — KlineIngestionWorker
    /// ve warmup REST path ikisi de 1m bar'ları doldurur. Warmup 1440 bar REST
    /// backfill ile anında tamamlanır.
    ///
    /// Loop 37: <see cref="KlineInterval.FiveMinutes"/> interval parametresi
    /// ile <c>state.FiveMinute</c> buffer üzerinden 5m snapshot hesaplanır.
    /// 4 loop kümülatif 1/28 TP hit (%3.6) teşhisi sonrası 5m timeframe'e
    /// geçilir: 5m bar body × 8 bar = %1.20 potansiyel, TP %0.30 hit %50+
    /// beklentisi. Parametreler evaluator JSON'ından (<c>KlineInterval</c>
    /// alanı) taşınır.
    ///
    /// Unsupported interval (3m/15m/30m/1h/...) ⇒ <c>null</c>.
    /// </summary>
    public MicroScalperIndicatorSnapshot? TryGetMicroScalperSnapshot(
        string symbol,
        KlineInterval interval = KlineInterval.OneMinute)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return null;
        }

        if (!_state.TryGetValue(symbol, out var state))
        {
            return null;
        }

        lock (state.SyncRoot)
        {
            // Loop 37: interval-driven buffer seçimi. Unsupported interval ⇒ null.
            // OneMinute = legacy MicroScalper/AtrScalper path (Loop 24 workaround);
            // FiveMinutes = Loop 37 AtrScalper 5m timeframe.
            var buffer = interval switch
            {
                KlineInterval.OneMinute => state.OneMinute,
                KlineInterval.FiveMinutes => state.FiveMinute,
                _ => null,
            };
            if (buffer is null)
            {
                return null;
            }

            var bars = buffer.Snapshot();

            // Warmup eşiği: EMA20 + VolumeSMA20 hesaplanabilir + önceki EMA için +1 bar.
            if (bars.Count < 21)
            {
                return null;
            }

            // VWAP rolling 15-bar: toplam bar sayısı fazla olsa bile son 15 bar.
            var klines = ToKlineList(bars);
            var vwapWindow = klines.Count <= 15
                ? klines
                : (IReadOnlyList<Kline>)klines.GetRange(klines.Count - 15, 15);

            var vwap = Evaluators.Indicators.Vwap(vwapWindow);
            var volumeSma20 = Evaluators.Indicators.VolumeSma(klines, 20);
            var ema20Now = Evaluators.Indicators.Ema(klines, period: 20, endIndex: klines.Count - 1);
            var ema20Prev = Evaluators.Indicators.Ema(klines, period: 20, endIndex: klines.Count - 2);

            // Loop 33 (AR-GE Strateji D) — ATR14 snapshot alanı. AtrScalperVwapEma1m
            // evaluator TP/SL geometrisini volatilite rejimine adaptif ölçekler.
            // Indicators.Atr() bar < period+1 ise 0 döner; evaluator MinTp/MaxTp
            // clip ile degenerate-case'i güvenle handle eder. 21-bar warmup eşiği
            // 14-period ATR için zaten yeterli (15 bar minimum).
            var atr14 = Evaluators.Indicators.Atr(klines, period: 14);

            var lastBar = klines[^1];
            var prevBar = klines[^2];

            return new MicroScalperIndicatorSnapshot(
                Vwap: vwap,
                PrevBarClose: prevBar.ClosePrice,
                LastBarClose: lastBar.ClosePrice,
                LastBarVolume: lastBar.Volume,
                VolumeSma20: volumeSma20,
                Ema20Now: ema20Now,
                Ema20Prev: ema20Prev,
                AsOf: lastBar.CloseTime,
                Atr14: atr14);
        }
    }

    /// <summary>
    /// Loop 41 AR-GE — Donchian Breakout 15m snapshot. 15m rolling buffer'dan
    /// son <paramref name="donchianPeriod"/> KAPANMIŞ bar'ın Donchian
    /// (high/low) penceresini, son <paramref name="volumeWindow"/> bar'ın
    /// volume aritmetik ortalama + population std dev'ini, son
    /// <paramref name="atrPeriod"/>+1 bar'lık ATR'ı hesaplar; "current bar"
    /// olarak buffer'daki son kapalı bar'ı kullanır (Donchian penceresi
    /// current bar'ı dışarıda bırakır — "üst kırılım" semantiği).
    ///
    /// Warmup eşiği: <c>max(donchianPeriod + 1, volumeWindow + 1, atrPeriod + 2)</c>
    /// — pencereyi current bar dışarıda bırakmak için +1 bar.
    /// Eşik karşılanmadıysa, symbol takip edilmiyorsa veya parametre &lt;= 0 ise
    /// <c>null</c> döner.
    /// </summary>
    public DonchianBreakoutIndicatorSnapshot? TryGetDonchianBreakoutSnapshot(
        string symbol,
        int donchianPeriod,
        int volumeWindow,
        int atrPeriod)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return null;
        }
        if (donchianPeriod <= 0 || volumeWindow <= 0 || atrPeriod <= 0)
        {
            return null;
        }

        if (!_state.TryGetValue(symbol, out var state))
        {
            return null;
        }

        lock (state.SyncRoot)
        {
            var bars = state.FifteenMinute.Snapshot();

            // Pencere kapalı bar'lardan oluşur; current bar kırılımı kapalı bar
            // tabanına göre ölçülür → minimum bar sayısı + current.
            var minBars = Math.Max(
                Math.Max(donchianPeriod + 1, volumeWindow + 1),
                atrPeriod + 2);
            if (bars.Count < minBars)
            {
                return null;
            }

            var klines = ToKlineList(bars);

            // Current bar = son kapalı; pencere bunu içermez (closed-window
            // breakout semantiği — currentClose > Max(prev N).
            var current = klines[^1];
            var window = klines.GetRange(klines.Count - 1 - donchianPeriod, donchianPeriod);

            var (donHigh, donLow) = Evaluators.Indicators.Donchian(window, donchianPeriod);

            var volWindow = klines.GetRange(klines.Count - 1 - volumeWindow, volumeWindow);
            var volAvg = Evaluators.Indicators.VolumeSma(volWindow, volumeWindow);
            var volStd = Evaluators.Indicators.VolumeStdev(volWindow, volumeWindow, volAvg);

            // ATR window: current bar dahil son atrPeriod+1 bar (Indicators.Atr
            // pencerede period+1 bar bekler — son period bar üzerinde TR hesabı).
            var atrWindow = klines.GetRange(klines.Count - (atrPeriod + 1), atrPeriod + 1);
            var atr14 = Evaluators.Indicators.Atr(atrWindow, atrPeriod);

            return new DonchianBreakoutIndicatorSnapshot(
                DonchianHigh20: donHigh,
                DonchianLow20: donLow,
                VolumeAvg20: volAvg,
                VolumeStd20: volStd,
                CurrentVolume: current.Volume,
                Atr14: atr14,
                CurrentClose: current.ClosePrice,
                BarClosed: true,
                LastBarOpenTime: current.OpenTime,
                AsOf: current.CloseTime);
        }
    }

    /// <summary>
    /// Loop 44 AR-GE — Bollinger Bands Mean Reversion 15m snapshot. 15m rolling
    /// buffer'dan son <paramref name="bbPeriod"/> bar'ın Bollinger Bands
    /// (mean ± stdMultiplier × σ), son <paramref name="rsiPeriod"/>+1 bar'ın
    /// Wilder RSI'ı, son <paramref name="volumeWindow"/> bar'ın volume aritmetik
    /// ortalama + population std dev'ini ve son <paramref name="atrPeriod"/>+1
    /// bar'ın ATR'ını hesaplar; "current bar" buffer'daki son kapalı bar'dır.
    ///
    /// Donchian'dan farklı: BB pencere semantiğinde current bar DAHİLDİR
    /// (standard BB period = son N bar; current bar period'un en son üyesi).
    /// Aynı şekilde Volume mean/std ve RSI da current bar'ı içerir. ATR ise
    /// period+1 bar gerektirir (önceki bar prev-close referansı için).
    ///
    /// Warmup eşiği:
    /// <c>max(bbPeriod, rsiPeriod + 1, volumeWindow, atrPeriod + 1)</c>.
    /// Eşik karşılanmadıysa, symbol takip edilmiyorsa veya parametre &lt;= 0 ise
    /// <c>null</c> döner.
    /// </summary>
    public BbMeanReversionIndicatorSnapshot? TryGetBbMeanReversionSnapshot(
        string symbol,
        int bbPeriod,
        decimal bbStdMultiplier,
        int rsiPeriod,
        int volumeWindow,
        int atrPeriod)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return null;
        }
        if (bbPeriod <= 0 || rsiPeriod <= 0 || volumeWindow <= 0 || atrPeriod <= 0)
        {
            return null;
        }

        if (!_state.TryGetValue(symbol, out var state))
        {
            return null;
        }

        lock (state.SyncRoot)
        {
            var bars = state.FifteenMinute.Snapshot();

            // BB / VolumeAvg / VolumeStd current bar dahil son N bar üzerinde
            // hesaplanır → minimum N bar yeter. RSI ve ATR period+1 bar ister.
            var minBars = Math.Max(
                Math.Max(bbPeriod, volumeWindow),
                Math.Max(rsiPeriod + 1, atrPeriod + 1));
            if (bars.Count < minBars)
            {
                return null;
            }

            var klines = ToKlineList(bars);
            var current = klines[^1];

            // BB pencere: current dahil son bbPeriod bar (closes).
            var bbWindow = klines.GetRange(klines.Count - bbPeriod, bbPeriod);
            var (bbMean, bbUpper, bbLower) =
                Evaluators.Indicators.BollingerBands(bbWindow, bbPeriod, bbStdMultiplier);

            // RSI: Wilder period bar'lık rolling — Indicators.Rsi son period bar
            // close-to-close diff üzerinden hesaplar (+1 prev bar gerek).
            var rsiWindow = klines.GetRange(klines.Count - (rsiPeriod + 1), rsiPeriod + 1);
            var rsi14 = Evaluators.Indicators.Rsi(rsiWindow, rsiPeriod);

            // Volume mean + population std, current bar dahil.
            var volWindow = klines.GetRange(klines.Count - volumeWindow, volumeWindow);
            var volAvg = Evaluators.Indicators.VolumeSma(volWindow, volumeWindow);
            var volStd = Evaluators.Indicators.VolumeStdev(volWindow, volumeWindow, volAvg);

            // ATR: period+1 bar (Indicators.Atr içeride period TR hesabı yapar).
            var atrWindow = klines.GetRange(klines.Count - (atrPeriod + 1), atrPeriod + 1);
            var atr14 = Evaluators.Indicators.Atr(atrWindow, atrPeriod);

            return new BbMeanReversionIndicatorSnapshot(
                BbUpper: bbUpper,
                BbMiddle: bbMean,
                BbLower: bbLower,
                Rsi14: rsi14,
                VolumeAvg20: volAvg,
                VolumeStd20: volStd,
                CurrentVolume: current.Volume,
                Atr14: atr14,
                CurrentClose: current.ClosePrice,
                BarClosed: true,
                LastBarOpenTime: current.OpenTime,
                AsOf: current.CloseTime);
        }
    }

    /// <summary>
    /// Loop 46 AR-GE — EMA9/EMA21 crossover scalper (1m) snapshot. 1m rolling
    /// buffer'dan EMA9/EMA21 (now + prev), Wilder RSI14, VolumeSMA20 ve ATR14
    /// hesaplar; "current bar" buffer'daki son kapalı 1m bar'dır.
    ///
    /// Pencere semantiği:
    ///   - Ema*Now : current bar dahil son <c>period</c> bar üzerinde EMA.
    ///   - Ema*Prev: current bar HARİÇ son <c>period</c> bar (yani indeks
    ///     <c>Count-2</c> son bar) üzerinde EMA — slope/cross trace.
    ///   - Rsi14   : son <c>rsiPeriod+1</c> bar close-to-close diff (Wilder).
    ///   - VolumeSma20: son <c>volumeWindow</c> bar volume aritmetik ortalama
    ///     (current bar dahil).
    ///   - Atr14   : son <c>atrPeriod+1</c> bar TR (Indicators.Atr içeride
    ///     son <c>atrPeriod</c> bar üzerinde TR hesabı yapar; prev-close
    ///     referansı için +1).
    ///
    /// Warmup eşiği:
    /// <c>max(emaSlowPeriod + 1, rsiPeriod + 1, volumeWindow, atrPeriod + 1)</c>.
    /// Eşik karşılanmadıysa veya parametre &lt;= 0 ise <c>null</c>.
    /// </summary>
    public EmaScalperIndicatorSnapshot? TryGetEmaScalperSnapshot(
        string symbol,
        int emaFastPeriod,
        int emaSlowPeriod,
        int rsiPeriod,
        int volumeWindow,
        int atrPeriod)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return null;
        }
        if (emaFastPeriod <= 0 || emaSlowPeriod <= 0
            || rsiPeriod <= 0 || volumeWindow <= 0 || atrPeriod <= 0)
        {
            return null;
        }

        if (!_state.TryGetValue(symbol, out var state))
        {
            return null;
        }

        lock (state.SyncRoot)
        {
            var bars = state.OneMinute.Snapshot();

            // EMA*Prev için Count-2 indeksli bar gerekir → +1 ekstra bar şart.
            // Slow EMA period kadar history bekleriz (kısa-EMA period zaten daha küçük).
            var minBars = Math.Max(
                Math.Max(emaSlowPeriod + 1, volumeWindow),
                Math.Max(rsiPeriod + 1, atrPeriod + 1));
            if (bars.Count < minBars)
            {
                return null;
            }

            var klines = ToKlineList(bars);
            var current = klines[^1];

            var emaFastNow = Evaluators.Indicators.Ema(klines, period: emaFastPeriod, endIndex: klines.Count - 1);
            var emaFastPrev = Evaluators.Indicators.Ema(klines, period: emaFastPeriod, endIndex: klines.Count - 2);
            var emaSlowNow = Evaluators.Indicators.Ema(klines, period: emaSlowPeriod, endIndex: klines.Count - 1);
            var emaSlowPrev = Evaluators.Indicators.Ema(klines, period: emaSlowPeriod, endIndex: klines.Count - 2);

            // RSI: son rsiPeriod+1 bar close-to-close diff.
            var rsiWindow = klines.GetRange(klines.Count - (rsiPeriod + 1), rsiPeriod + 1);
            var rsi14 = Evaluators.Indicators.Rsi(rsiWindow, rsiPeriod);

            // VolumeSMA: current bar dahil son volumeWindow bar.
            var volWindow = klines.GetRange(klines.Count - volumeWindow, volumeWindow);
            var volumeSma = Evaluators.Indicators.VolumeSma(volWindow, volumeWindow);

            // ATR: period+1 bar.
            var atrWindow = klines.GetRange(klines.Count - (atrPeriod + 1), atrPeriod + 1);
            var atr14 = Evaluators.Indicators.Atr(atrWindow, atrPeriod);

            return new EmaScalperIndicatorSnapshot(
                Ema9Now: emaFastNow,
                Ema9Prev: emaFastPrev,
                Ema21Now: emaSlowNow,
                Ema21Prev: emaSlowPrev,
                Rsi14: rsi14,
                VolumeSma20: volumeSma,
                CurrentVolume: current.Volume,
                Atr14: atr14,
                CurrentClose: current.ClosePrice,
                BarClosed: true,
                LastBarOpenTime: current.OpenTime,
                AsOf: current.CloseTime);
        }
    }

    /// <summary>
    /// Loop 50 AR-GE — Hybrid 1m frekans tetiği + 15m kalite kapısı snapshot.
    /// Hem <c>state.OneMinute</c> hem <c>state.FifteenMinute</c> buffer'ından
    /// okur, iki timeframe değerlerini tek snapshot'ta birleştirir.
    ///
    /// 1m pencere semantiği: EMA9/EMA21 now (endIndex = Count-1) ve prev
    /// (endIndex = Count-2); VolumeMa20 son <c>volumeWindow_1m</c> bar
    /// dahil; ATR14 son <c>atrPeriod_1m+1</c> bar (prev-close referansı).
    ///
    /// 15m pencere semantiği: BB(period, std×mult) current bar dahil;
    /// RSI14 curr son <c>rsiPeriod_15m+1</c> bar (close-to-close diff),
    /// RSI14 prev current bar HARİÇ son <c>rsiPeriod_15m+1</c> bar (yani
    /// indeks <c>Count-2</c> son bar) — momentum yukarı dönüş trace için;
    /// ATR14 son <c>atrPeriod_15m+1</c> bar.
    ///
    /// Warmup eşiği:
    ///   1m: <c>max(emaSlowPeriod_1m + 1, volumeWindow_1m, atrPeriod_1m + 1)</c>
    ///   15m: <c>max(bbPeriod_15m, rsiPeriod_15m + 2, atrPeriod_15m + 1)</c>
    /// Eşik karşılanmadıysa, symbol takip edilmiyorsa veya parametre &lt;= 0
    /// ise <c>null</c> döner.
    /// </summary>
    public HybridMomentum1mIndicatorSnapshot? TryGetHybridMomentum1mSnapshot(
        string symbol,
        int emaFastPeriod,
        int emaSlowPeriod,
        int volumeWindow_1m,
        int atrPeriod_1m,
        int bbPeriod_15m,
        decimal bbStdMultiplier_15m,
        int rsiPeriod_15m,
        int atrPeriod_15m)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return null;
        }
        if (emaFastPeriod <= 0 || emaSlowPeriod <= 0
            || volumeWindow_1m <= 0 || atrPeriod_1m <= 0
            || bbPeriod_15m <= 0 || rsiPeriod_15m <= 0 || atrPeriod_15m <= 0)
        {
            return null;
        }

        if (!_state.TryGetValue(symbol, out var state))
        {
            return null;
        }

        lock (state.SyncRoot)
        {
            var bars1m = state.OneMinute.Snapshot();
            var bars15m = state.FifteenMinute.Snapshot();

            // Loop 50 — 1m warmup eşiği. EMA prev (Count-2) için +1 bar şart;
            // Slow EMA period kadar history bekleriz.
            var minBars1m = Math.Max(
                Math.Max(emaSlowPeriod + 1, volumeWindow_1m),
                atrPeriod_1m + 1);
            if (bars1m.Count < minBars1m)
            {
                return null;
            }

            // Loop 50 — 15m warmup eşiği. RSI prev hesabı için +2 bar
            // (Indicators.Rsi son rsiPeriod bar üzerinde close-to-close diff
            // yapar; prev için bir bar daha geriye git).
            var minBars15m = Math.Max(
                Math.Max(bbPeriod_15m, rsiPeriod_15m + 2),
                atrPeriod_15m + 1);
            if (bars15m.Count < minBars15m)
            {
                return null;
            }

            // ── 1m hesapları ───────────────────────────────────────────────
            var klines1m = ToKlineList(bars1m);
            var current1m = klines1m[^1];

            var ema9_1m = Evaluators.Indicators.Ema(klines1m, period: emaFastPeriod, endIndex: klines1m.Count - 1);
            var ema21_1m = Evaluators.Indicators.Ema(klines1m, period: emaSlowPeriod, endIndex: klines1m.Count - 1);
            var ema9Prev_1m = Evaluators.Indicators.Ema(klines1m, period: emaFastPeriod, endIndex: klines1m.Count - 2);
            var ema21Prev_1m = Evaluators.Indicators.Ema(klines1m, period: emaSlowPeriod, endIndex: klines1m.Count - 2);

            var volWindow_1m = klines1m.GetRange(klines1m.Count - volumeWindow_1m, volumeWindow_1m);
            var volumeMa20_1m = Evaluators.Indicators.VolumeSma(volWindow_1m, volumeWindow_1m);

            var atrWindow_1m = klines1m.GetRange(klines1m.Count - (atrPeriod_1m + 1), atrPeriod_1m + 1);
            var atr14_1m = Evaluators.Indicators.Atr(atrWindow_1m, atrPeriod_1m);

            // ── 15m hesapları ──────────────────────────────────────────────
            var klines15m = ToKlineList(bars15m);
            var current15m = klines15m[^1];

            // BB current bar dahil son bbPeriod bar.
            var bbWindow_15m = klines15m.GetRange(klines15m.Count - bbPeriod_15m, bbPeriod_15m);
            var (bbMean_15m, bbUpper_15m, bbLower_15m) =
                Evaluators.Indicators.BollingerBands(bbWindow_15m, bbPeriod_15m, bbStdMultiplier_15m);

            // RSI curr — son rsiPeriod+1 bar üzerinde close-to-close diff.
            var rsiWindowCurr_15m = klines15m.GetRange(klines15m.Count - (rsiPeriod_15m + 1), rsiPeriod_15m + 1);
            var rsi14_15m = Evaluators.Indicators.Rsi(rsiWindowCurr_15m, rsiPeriod_15m);

            // RSI prev — current bar HARİÇ; bir önceki bar son üye olacak şekilde
            // pencereyi bir bar geriye kaydır. (Count-1) son bar; prev penceresi
            // (Count-2) son bar dahil son rsiPeriod+1 bar.
            var rsiWindowPrev_15m = klines15m.GetRange(klines15m.Count - 1 - (rsiPeriod_15m + 1), rsiPeriod_15m + 1);
            var rsi14Prev_15m = Evaluators.Indicators.Rsi(rsiWindowPrev_15m, rsiPeriod_15m);

            // ATR — son atrPeriod+1 bar (prev-close referansı için +1).
            var atrWindow_15m = klines15m.GetRange(klines15m.Count - (atrPeriod_15m + 1), atrPeriod_15m + 1);
            var atr14_15m = Evaluators.Indicators.Atr(atrWindow_15m, atrPeriod_15m);

            return new HybridMomentum1mIndicatorSnapshot(
                // 1m
                Ema9_1m: ema9_1m,
                Ema21_1m: ema21_1m,
                Ema9Prev_1m: ema9Prev_1m,
                Ema21Prev_1m: ema21Prev_1m,
                CurrentVolume_1m: current1m.Volume,
                VolumeMa20_1m: volumeMa20_1m,
                Atr14_1m: atr14_1m,
                CurrentClose_1m: current1m.ClosePrice,
                BarClosed_1m: true,
                LastBarOpenTime_1m: current1m.OpenTime,
                // 15m
                BbUpper_15m: bbUpper_15m,
                BbMiddle_15m: bbMean_15m,
                BbLower_15m: bbLower_15m,
                Rsi14_15m: rsi14_15m,
                Rsi14Prev_15m: rsi14Prev_15m,
                Atr14_15m: atr14_15m,
                CurrentClose_15m: current15m.ClosePrice,
                BarClosed_15m: true,
                LastBarOpenTime_15m: current15m.OpenTime,
                AsOf: current1m.CloseTime);
        }
    }

    /// <summary>
    /// Test-friendly injection path — infrastructure tests seed the buffers directly
    /// without starting the hosted service. Returns <c>true</c> when the symbol is
    /// known (added via <c>Symbols</c> config) and the bar was upserted.
    /// </summary>
    internal bool SeedBar(string symbol, KlineInterval interval, WsKlinePayload bar)
    {
        if (!_state.TryGetValue(symbol, out var state))
        {
            return false;
        }

        lock (state.SyncRoot)
        {
            var buf = SelectBuffer(state, interval);
            if (buf is null)
            {
                return false;
            }
            buf.Upsert(bar);
        }
        return true;
    }

    private static IndicatorRollingBuffer? SelectBuffer(SymbolState state, KlineInterval interval) =>
        interval switch
        {
            KlineInterval.OneMinute => state.OneMinute,
            KlineInterval.OneHour => state.OneHour,
            KlineInterval.ThirtySeconds => state.ThirtySecond,
            // Loop 37 — 5m bar buffer (AtrScalper timeframe migration).
            KlineInterval.FiveMinutes => state.FiveMinute,
            // Loop 41 — 15m bar buffer (Donchian Breakout 15m).
            KlineInterval.FifteenMinutes => state.FifteenMinute,
            _ => null,
        };

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            await WarmupAsync(ct);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MarketIndicator warmup failed; live WS consumer will continue");
        }

        try
        {
            // Loop 23 blocker fix (BLOCKER-2): dedicated subscriber channel so
            // this service shares no reader with KlineIngestionWorker (previous
            // single-channel design raced — one of the two consumers missed each
            // envelope and the 30s buffer never reached the 21-bar threshold).
            var reader = _stream.SubscribeKlines();
            await foreach (var payload in reader.ReadAllAsync(ct).WithCancellation(ct))
            {
                if (!payload.IsClosed)
                {
                    continue;
                }

                if (payload.Interval != KlineInterval.OneMinute
                    && payload.Interval != KlineInterval.OneHour
                    && payload.Interval != KlineInterval.ThirtySeconds
                    && payload.Interval != KlineInterval.FiveMinutes
                    && payload.Interval != KlineInterval.FifteenMinutes)
                {
                    continue;
                }

                if (!_state.TryGetValue(payload.Symbol, out var state))
                {
                    continue;
                }

                lock (state.SyncRoot)
                {
                    var buf = SelectBuffer(state, payload.Interval);
                    if (buf is null) continue;
                    buf.Upsert(payload);
                }
            }
        }
        catch (OperationCanceledException) { /* shutdown */ }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MarketIndicator consumer loop terminated unexpectedly");
        }
    }

    private async Task WarmupAsync(CancellationToken ct)
    {
        var symbols = _state.Keys.ToArray();
        if (symbols.Length == 0)
        {
            return;
        }

        // Per-symbol, per-interval REST fetch. We use the shared IBinanceMarketData
        // client — same rate-limit handler path as KlineBackfillWorker.
        await using var scope = _scopeFactory.CreateAsyncScope();
        var marketData = scope.ServiceProvider.GetRequiredService<IBinanceMarketData>();

        foreach (var symbol in symbols)
        {
            if (ct.IsCancellationRequested) return;

            await WarmupOneAsync(marketData, symbol, KlineInterval.OneMinute, OneMinuteBufferCapacity, ct);
            await WarmupOneAsync(marketData, symbol, KlineInterval.OneHour, OneHourBufferCapacity, ct);
            // Loop 37 — 5m bar warmup (AtrScalper timeframe migration). 100 bar
            // REST backfill ile anında doldur; WS @kline_5m stream ile live tutulur.
            await WarmupOneAsync(marketData, symbol, KlineInterval.FiveMinutes, FiveMinuteBufferCapacity, ct);
            // Loop 41 — 15m bar warmup (Donchian Breakout 15m). 80 bar REST backfill;
            // WS @kline_15m stream ile live tutulur. Donchian/Volume/ATR warmup için
            // yeterli (max gereksinim 21 bar).
            await WarmupOneAsync(marketData, symbol, KlineInterval.FifteenMinutes, FifteenMinuteBufferCapacity, ct);
            // Loop 24 bug-fix: 30s bar path deprecated — Binance SPOT WS does NOT
            // support @kline_30s (SPOT valid intervals: 1s,1m,3m,…). TryGetMicroScalperSnapshot
            // OneMinute default'u state.OneMinute'dan okur, 30s warmup gerekmez.
            // state.ThirtySecond buffer remains (consumer loop keeps accepting 30s
            // bars if ever sent) but is intentionally unused downstream.

            // ADR-0016 §16.9.6 — emit per-symbol warmup completion marker.
            await MaybePublishWarmupAsync(symbol, ct);
        }

        _logger.LogInformation("MarketIndicator warmup completed: {Count} symbol(s)", symbols.Length);
    }

    /// <summary>
    /// ADR-0016 §16.9.6 — once both intervals warmed, publish
    /// <see cref="IndicatorWarmupCompletedEvent"/> so the SystemEvents pipe records
    /// readiness. Tolerant of concurrent callers via double-check on symbol state.
    /// </summary>
    private async Task MaybePublishWarmupAsync(string symbol, CancellationToken ct)
    {
        if (!_state.TryGetValue(symbol, out var state))
        {
            return;
        }

        int oneMinCount;
        int oneHourCount;
        lock (state.SyncRoot)
        {
            if (state.WarmupEventPublished)
            {
                return;
            }
            oneMinCount = state.OneMinute.Count;
            oneHourCount = state.OneHour.Count;
            if (oneMinCount < OneMinuteBufferCapacity || oneHourCount < OneHourBufferCapacity / 2)
            {
                return;
            }
            state.WarmupEventPublished = true;
        }

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var publisher = scope.ServiceProvider.GetRequiredService<IPublisher>();
            await publisher.Publish(
                new IndicatorWarmupCompletedEvent(symbol, oneMinCount, oneHourCount),
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "IndicatorWarmupCompleted publish failed symbol={Symbol}", symbol);
        }
    }

    private async Task WarmupOneAsync(
        IBinanceMarketData marketData,
        string symbol,
        KlineInterval interval,
        int capacity,
        CancellationToken ct)
    {
        try
        {
            // Binance hard-cap /api/v3/klines?limit is 1000. Capacity >1000 would require
            // a paged fetch, but our current 1440-bar window only needs 1 extra page.
            var pagesNeeded = (capacity + 999) / 1000;
            var bars = new List<RestKlineDto>(capacity);
            DateTimeOffset? endTime = null;

            for (var page = 0; page < pagesNeeded; page++)
            {
                if (ct.IsCancellationRequested) return;

                var remaining = capacity - bars.Count;
                var pageLimit = Math.Min(1000, remaining);

                var pageBars = await marketData.GetKlinesAsync(
                    symbol, interval, pageLimit,
                    startTime: null, endTime, ct);

                if (pageBars.Count == 0)
                {
                    break;
                }

                // Oldest-first from Binance — prepend earlier pages.
                var merged = new List<RestKlineDto>(pageBars.Count + bars.Count);
                merged.AddRange(pageBars);
                merged.AddRange(bars);
                bars = merged;

                endTime = pageBars[0].OpenTime.AddMilliseconds(-1);

                if (pageBars.Count < pageLimit)
                {
                    break;
                }
            }

            if (bars.Count == 0)
            {
                _logger.LogWarning(
                    "MarketIndicator warmup returned 0 bars for {Symbol} {Interval}",
                    symbol, interval);
                return;
            }

            if (!_state.TryGetValue(symbol, out var state))
            {
                return;
            }

            lock (state.SyncRoot)
            {
                var buf = SelectBuffer(state, interval);
                if (buf is null)
                {
                    _logger.LogWarning(
                        "Unsupported warmup interval {Interval} for {Symbol}; skipped",
                        interval, symbol);
                    return;
                }
                foreach (var bar in bars)
                {
                    var payload = new WsKlinePayload(
                        Symbol: symbol,
                        Interval: interval,
                        OpenTime: bar.OpenTime,
                        CloseTime: bar.CloseTime,
                        Open: bar.Open,
                        High: bar.High,
                        Low: bar.Low,
                        Close: bar.Close,
                        Volume: bar.Volume,
                        QuoteVolume: bar.QuoteVolume,
                        TradeCount: bar.TradeCount,
                        TakerBuyBase: bar.TakerBuyBase,
                        TakerBuyQuote: bar.TakerBuyQuote,
                        IsClosed: true);
                    buf.Upsert(payload);
                }
            }

            _logger.LogInformation(
                "MarketIndicator warmup {Symbol} {Interval}: loaded {Count} bar(s)",
                symbol, interval, bars.Count);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutdown — no-op.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "MarketIndicator warmup failed for {Symbol} {Interval}; continuing",
                symbol, interval);
        }
    }

    private static List<Kline> ToKlineList(IReadOnlyList<WsKlinePayload> payloads)
    {
        var list = new List<Kline>(payloads.Count);
        foreach (var p in payloads)
        {
            list.Add(Kline.Ingest(
                Symbol.From(p.Symbol),
                p.Interval,
                openTime: p.OpenTime,
                closeTime: p.CloseTime,
                open: p.Open,
                high: p.High,
                low: p.Low,
                close: p.Close,
                volume: p.Volume,
                quoteVolume: p.QuoteVolume,
                tradeCount: p.TradeCount,
                takerBuyBase: p.TakerBuyBase,
                takerBuyQuote: p.TakerBuyQuote,
                isClosed: p.IsClosed));
        }
        return list;
    }

    private sealed class SymbolState
    {
        public object SyncRoot { get; } = new();
        public IndicatorRollingBuffer OneMinute { get; } = new(OneMinuteBufferCapacity);
        public IndicatorRollingBuffer OneHour { get; } = new(OneHourBufferCapacity);
        // ADR-0018 §18.11 — 30sn bar buffer for MicroScalper path.
        public IndicatorRollingBuffer ThirtySecond { get; } = new(ThirtySecondBufferCapacity);
        // Loop 37 — 5m bar buffer for AtrScalper 5m timeframe migration.
        public IndicatorRollingBuffer FiveMinute { get; } = new(FiveMinuteBufferCapacity);
        // Loop 41 — 15m bar buffer for Donchian Breakout 15m strategy.
        public IndicatorRollingBuffer FifteenMinute { get; } = new(FifteenMinuteBufferCapacity);

        // ADR-0016 §16.9.6 — one-shot latch: when warmup budget crosses threshold
        // we publish IndicatorWarmupCompletedEvent exactly once per symbol.
        public bool WarmupEventPublished { get; set; }
    }
}
