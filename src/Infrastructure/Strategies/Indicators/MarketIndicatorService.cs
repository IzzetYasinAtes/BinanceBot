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
/// Loop 67 KMS pivot — maintains per-symbol rolling 5m bar buffer and exposes
/// the <see cref="KmsMomentumSnapshot"/> for the
/// <c>KlineMomentumSpread5m</c> evaluator. All legacy 1m/1h/30s/15m buffers
/// + their snapshot helpers (VwapEma / MicroScalper / Atr / Donchian / BB /
/// EmaScalper / HybridMomentum) are removed in this loop; the surface is
/// intentionally minimal so future strategies opt in via additive snapshots.
///
/// Lifecycle:
///   1. On host start, REST-warm 5m (200 bars) per symbol via the shared
///      <c>IBinanceMarketData</c> client. Per-symbol failures are logged and
///      skipped — best-effort, never fail-fasts the host.
///   2. Consume the shared <see cref="IBinanceMarketStream"/> kline channel
///      (subscribed once at startup) and upsert closed 5m bars into the
///      symbol's buffer.
///
/// Thread model:
///   - Writes (REST warmup + WS consumer) are serialised per symbol by a
///     lock around <see cref="IndicatorRollingBuffer"/>.
///   - Reads (<see cref="TryGetKmsMomentumSnapshot"/>) take the same lock,
///     copy bars, then run pure decimal math — &lt;1ms.
/// </summary>
public sealed class MarketIndicatorService : IMarketIndicatorService, IHostedService
{
    // Loop 67 KMS — 5m rolling buffer kapasitesi. Gerekli lookback:
    //   - RSI prev (14): max(period+1, period+2 — prev shift) = 16 bar
    //   - EMA9 now/prev: 10 bar
    //   - ATR14: 15 bar
    //   - TradeCountAvg(20): 20 bar
    //   - Loop 77 — EMA200 trend gate: 200 bar (16.67h history) zorunlu
    //   - Loop 77 — Bollinger(20,2) BBW: 20 bar
    // Binance REST limit 1 sayfa yeter (limit 1000 ≥ 200).
    internal const int FiveMinuteBufferCapacity = 200;

    // Loop 77 — EMA200 trend gate sabit periyodu. Snapshot her zaman 200
    // dener; warmup yetersizse 0 döner ve evaluator gate'i "unavailable"
    // (açık) olarak yorumlar.
    internal const int Ema200Period = 200;

    // Loop 77 — Bollinger Bands sabit periyod + stdDev katsayısı. KMS
    // snapshot BBW (band width) hesabı için kullanır; klasik Bollinger
    // ayarları (20, 2.0).
    internal const int BollingerPeriod = 20;
    internal const decimal BollingerStdDev = 2.0m;

    // Loop 77 — IndicatorWarmupCompleted publish eşiği. Loop 67'de 30 bar
    // yeterliydi (RSI/EMA9/ATR14 için ~22 bar). EMA200 trend gate için
    // tam 200 bar gerekli; warmup eventi de bu eşikte yayılmalı ki
    // downstream "evaluator hazır" varsayımı doğru kalsın.
    internal const int WarmupCompletedBarThreshold = 200;

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

    /// <summary>
    /// Loop 67 KMS — KlineMomentumSpread5m snapshot. Reads the 5m rolling
    /// buffer, computes:
    ///   - RSI(rsiPeriod) at <c>Count-1</c> (curr) and <c>Count-2</c> (prev)
    ///     by shifting the window one bar back — both use the same Wilder
    ///     close-to-close diff helper.
    ///   - EMA(emaPeriod) at <c>Count-1</c> (now) and <c>Count-2</c> (prev)
    ///     for slope confirmation.
    ///   - ATR(atrPeriod) — period+1 bar prev-close reference.
    ///   - TradeCountAvg(tradeCountWindow) + current bar TradeCount.
    /// Warmup threshold: <c>max(rsiPeriod + 2, emaPeriod + 1, atrPeriod + 1, tradeCountWindow)</c>.
    /// </summary>
    public KmsMomentumSnapshot? TryGetKmsMomentumSnapshot(
        string symbol,
        int rsiPeriod,
        int emaPeriod,
        int atrPeriod,
        int tradeCountWindow)
    {
        if (string.IsNullOrWhiteSpace(symbol)) return null;
        if (rsiPeriod <= 0 || emaPeriod <= 0 || atrPeriod <= 0 || tradeCountWindow <= 0)
        {
            return null;
        }

        if (!_state.TryGetValue(symbol, out var state))
        {
            return null;
        }

        lock (state.SyncRoot)
        {
            var bars = state.FiveMinute.Snapshot();

            // Warmup eşiği. RSI prev için +2 (Indicators.Rsi son rsiPeriod
            // close-to-close diff yapar; prev için pencereyi bir bar geriye
            // kaydır → toplam rsiPeriod + 2 bar gerekli).
            var minBars = Math.Max(
                Math.Max(rsiPeriod + 2, emaPeriod + 1),
                Math.Max(atrPeriod + 1, tradeCountWindow));
            if (bars.Count < minBars)
            {
                return null;
            }

            var klines = ToKlineList(bars);
            var current = klines[^1];

            // RSI curr — son (rsiPeriod + 1) bar üzerinde Wilder diff.
            var rsiCurrWindow = klines.GetRange(klines.Count - (rsiPeriod + 1), rsiPeriod + 1);
            var rsi14 = Evaluators.Indicators.Rsi(rsiCurrWindow, rsiPeriod);

            // RSI prev — current bar HARİÇ; pencereyi bir bar geriye kaydır.
            // Window son üyesi (Count-2) bar olur.
            var rsiPrevWindow = klines.GetRange(klines.Count - 1 - (rsiPeriod + 1), rsiPeriod + 1);
            var rsi14Prev = Evaluators.Indicators.Rsi(rsiPrevWindow, rsiPeriod);

            // EMA9 now/prev — slope teyidi için ardışık iki endIndex.
            var ema9Now = Evaluators.Indicators.Ema(klines, period: emaPeriod, endIndex: klines.Count - 1);
            var ema9Prev = Evaluators.Indicators.Ema(klines, period: emaPeriod, endIndex: klines.Count - 2);

            // ATR — son (atrPeriod + 1) bar (Indicators.Atr içeride period TR).
            var atrWindow = klines.GetRange(klines.Count - (atrPeriod + 1), atrPeriod + 1);
            var atr14 = Evaluators.Indicators.Atr(atrWindow, atrPeriod);

            // TradeCountAvg — son tradeCountWindow bar dahil current.
            var tcWindow = klines.GetRange(klines.Count - tradeCountWindow, tradeCountWindow);
            var tradeCountAvg = Evaluators.Indicators.TradeCountAvg(tcWindow, tradeCountWindow);

            // Loop 77 — EMA200 trend gate. Buffer 200 bar dolduğunda anlamlı;
            // erken aşamada 0 döner ve evaluator gate'i "unavailable / açık"
            // sayar. Indicators.Ema endIndex < period-1 durumunda close fallback
            // dönüyor (gerçek EMA200 değil) — burada minBars eşik kontrolü
            // yapıyoruz, fallback'i bilinçli olarak engelle.
            var ema200 = klines.Count >= Ema200Period
                ? Evaluators.Indicators.Ema(klines, period: Ema200Period, endIndex: klines.Count - 1)
                : 0m;

            // Loop 77 — Bollinger Band Width (BBW) regime filter:
            //   BBW = (Upper - Lower) / Middle
            // Yetersiz bar (<20) durumunda BollingerBands flat band döner
            // (Upper == Lower == Middle), BBW 0 olur ve skor sistemi 0 puan
            // verir; emit'i tek başına engellemez.
            var (bbMid, bbUpper, bbLower) = Evaluators.Indicators.BollingerBands(
                klines, BollingerPeriod, BollingerStdDev);
            var bbw = bbMid > 0m ? (bbUpper - bbLower) / bbMid : 0m;

            return new KmsMomentumSnapshot(
                CurrentClose: current.ClosePrice,
                Rsi14: rsi14,
                Rsi14Prev: rsi14Prev,
                Ema9Now: ema9Now,
                Ema9Prev: ema9Prev,
                Atr14: atr14,
                AvgTradeCount20: tradeCountAvg,
                CurrentTradeCount: current.TradeCount,
                Ema200: ema200,
                BollingerBandWidth: bbw,
                LastBarOpenTime: current.OpenTime,
                AsOf: current.CloseTime);
        }
    }

    /// <summary>
    /// Loop 79 — BollingerBandReversal5m snapshot. KMS ile aynı 5m buffer'dan
    /// okur; pencere bir bar geriye kaydırılarak RSI prev hesaplanır. Warmup
    /// eşiği <c>max(rsiPeriod + 2, bbPeriod, atrPeriod + 1)</c>.
    /// </summary>
    public BbReversalSnapshot? TryGetBbReversalSnapshot(
        string symbol,
        int rsiPeriod,
        int bbPeriod,
        decimal bbStdDev,
        int atrPeriod)
    {
        if (string.IsNullOrWhiteSpace(symbol)) return null;
        if (rsiPeriod <= 0 || bbPeriod <= 0 || bbStdDev <= 0m || atrPeriod <= 0)
        {
            return null;
        }

        if (!_state.TryGetValue(symbol, out var state))
        {
            return null;
        }

        lock (state.SyncRoot)
        {
            var bars = state.FiveMinute.Snapshot();

            // Warmup eşiği. RSI prev için +2 (Indicators.Rsi son rsiPeriod
            // close-to-close diff yapar; prev için pencereyi bir bar geriye
            // kaydır → toplam rsiPeriod + 2 bar gerekli). BB sadece bbPeriod
            // bar; ATR için +1 (TR önceki close referansı).
            var minBars = Math.Max(
                Math.Max(rsiPeriod + 2, bbPeriod),
                atrPeriod + 1);
            if (bars.Count < minBars)
            {
                return null;
            }

            var klines = ToKlineList(bars);
            var current = klines[^1];

            // RSI curr — son (rsiPeriod + 1) bar üzerinde Wilder diff.
            var rsiCurrWindow = klines.GetRange(klines.Count - (rsiPeriod + 1), rsiPeriod + 1);
            var rsi14 = Evaluators.Indicators.Rsi(rsiCurrWindow, rsiPeriod);

            // RSI prev — current bar HARİÇ; pencereyi bir bar geriye kaydır.
            var rsiPrevWindow = klines.GetRange(klines.Count - 1 - (rsiPeriod + 1), rsiPeriod + 1);
            var rsi14Prev = Evaluators.Indicators.Rsi(rsiPrevWindow, rsiPeriod);

            // Bollinger Bands — son bbPeriod close.
            var (bbMean, bbUpper, bbLower) = Evaluators.Indicators.BollingerBands(
                klines, bbPeriod, bbStdDev);
            var bbw = bbMean > 0m ? (bbUpper - bbLower) / bbMean : 0m;

            // ATR — son (atrPeriod + 1) bar.
            var atrWindow = klines.GetRange(klines.Count - (atrPeriod + 1), atrPeriod + 1);
            var atr14 = Evaluators.Indicators.Atr(atrWindow, atrPeriod);

            return new BbReversalSnapshot(
                CurrentClose: current.ClosePrice,
                Rsi14: rsi14,
                Rsi14Prev: rsi14Prev,
                BollingerLower: bbLower,
                BollingerMean: bbMean,
                BollingerBandWidth: bbw,
                Atr14: atr14,
                LastBarOpenTime: current.OpenTime,
                AsOf: current.CloseTime);
        }
    }

    /// <summary>
    /// Test-friendly injection path — infrastructure tests seed the buffer
    /// directly without starting the hosted service. Returns <c>true</c> when
    /// the symbol is known (added via <c>Symbols</c> config) and the bar was
    /// upserted.
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
            if (buf is null) return false;
            buf.Upsert(bar);
        }
        return true;
    }

    /// <summary>Test helper — register a fresh symbol slot for direct seeding.</summary>
    internal void RegisterSymbolForTesting(string symbol) =>
        _state.TryAdd(symbol.ToUpperInvariant(), new SymbolState());

    private static IndicatorRollingBuffer? SelectBuffer(SymbolState state, KlineInterval interval) =>
        interval switch
        {
            // Loop 67 — yalnızca 5m buffer kalır.
            KlineInterval.FiveMinutes => state.FiveMinute,
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
            var reader = _stream.SubscribeKlines();
            await foreach (var payload in reader.ReadAllAsync(ct).WithCancellation(ct))
            {
                if (!payload.IsClosed) continue;
                if (payload.Interval != KlineInterval.FiveMinutes) continue;

                if (!_state.TryGetValue(payload.Symbol, out var state)) continue;

                lock (state.SyncRoot)
                {
                    state.FiveMinute.Upsert(payload);
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
        if (symbols.Length == 0) return;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var marketData = scope.ServiceProvider.GetRequiredService<IBinanceMarketData>();

        foreach (var symbol in symbols)
        {
            if (ct.IsCancellationRequested) return;

            await WarmupOneAsync(marketData, symbol, KlineInterval.FiveMinutes, FiveMinuteBufferCapacity, ct);
            await MaybePublishWarmupAsync(symbol, ct);
        }

        _logger.LogInformation("MarketIndicator warmup completed: {Count} symbol(s)", symbols.Length);
    }

    /// <summary>
    /// ADR-0016 §16.9.6 — once 5m buffer reaches threshold, publish
    /// <see cref="IndicatorWarmupCompletedEvent"/> exactly once per symbol.
    /// Loop 67: thresholds collapsed to 5m only (other intervals removed).
    /// </summary>
    private async Task MaybePublishWarmupAsync(string symbol, CancellationToken ct)
    {
        if (!_state.TryGetValue(symbol, out var state)) return;

        int fiveMinCount;
        lock (state.SyncRoot)
        {
            if (state.WarmupEventPublished) return;
            fiveMinCount = state.FiveMinute.Count;
            // Loop 77 — EMA200 trend gate aktif. Warmup eventi tam 200 bar
            // dolduğunda yayılır; 200'ün altında EMA200 fallback (close)
            // değeri döneceği için trend yorumu yanıltıcı olur.
            if (fiveMinCount < WarmupCompletedBarThreshold) return;
            state.WarmupEventPublished = true;
        }

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var publisher = scope.ServiceProvider.GetRequiredService<IPublisher>();
            // Loop 67 — eski event 1m + 1h count alanlarına sahip; 5m sayısını
            // her iki alana da geçirerek backward-compat (downstream sayıyı
            // logger amaçlı kullanıyor, semantiği sıfırlandı).
            await publisher.Publish(
                new IndicatorWarmupCompletedEvent(symbol, fiveMinCount, fiveMinCount),
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
            // Binance hard-cap /api/v3/klines?limit is 1000.
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

                if (pageBars.Count == 0) break;

                var merged = new List<RestKlineDto>(pageBars.Count + bars.Count);
                merged.AddRange(pageBars);
                merged.AddRange(bars);
                bars = merged;

                endTime = pageBars[0].OpenTime.AddMilliseconds(-1);

                if (pageBars.Count < pageLimit) break;
            }

            if (bars.Count == 0)
            {
                _logger.LogWarning(
                    "MarketIndicator warmup returned 0 bars for {Symbol} {Interval}",
                    symbol, interval);
                return;
            }

            if (!_state.TryGetValue(symbol, out var state)) return;

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

        // Loop 67 — yalnızca 5m bar buffer kalır. Önceki 1m/1h/30s/15m
        // buffer'lar (legacy 7-strateji ailesi) kaldırıldı; her yeni
        // strateji additive olarak kendi buffer'ını talep eder.
        public IndicatorRollingBuffer FiveMinute { get; } = new(FiveMinuteBufferCapacity);

        public bool WarmupEventPublished { get; set; }
    }
}
