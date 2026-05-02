using System.Collections.Concurrent;
using BinanceBot.Application.Abstractions;
using BinanceBot.Application.Abstractions.Binance;
using BinanceBot.Application.Strategies.Indicators;
using BinanceBot.Application.Strategies.Patterns;
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
/// Loop 81 — Pattern-composite scalping pivot. Per-symbol 5m rolling buffer
/// (200 bar) tutar; <see cref="TryGetBarSnapshot"/> ile tek paylaşılan
/// <see cref="BarSnapshot"/> üretir. KMS+BBR snapshot method'ları silindi
/// (ADR-0024).
///
/// Lifecycle:
///   1. Boot'ta REST warmup 5m × 200 bar (Binance hard-cap 1000).
///   2. <see cref="IBinanceMarketStream.SubscribeKlines"/> kanalından kapalı
///      5m bar'ları üst tut.
///
/// Thread model: per-symbol lock; reads (TryGetBarSnapshot) lock altında bar
/// kopyalar, sonra pure decimal math (&lt;1ms).
/// </summary>
public sealed class MarketIndicatorService : IMarketIndicatorService, IHostedService
{
    // Loop 81 — 5m rolling buffer kapasitesi. Pattern detector ihtiyaçları:
    //   - RSI14 prev: 16 bar
    //   - EMA9/21/50/200: 200 bar (EMA200 zorunlu)
    //   - ATR14: 15 bar
    //   - Bollinger(20, 2) + BBW window 6: 20 + 5 = 25 bar
    //   - Donchian20: 20 bar
    //   - VWAP: tüm window
    //   - MACD(12, 26): 27 bar
    //   - AvgVolume20 / AvgTradeCount20: 20 bar
    //   - RecentBars 30 bar (Donchian + Higher-Low için)
    // 200 bar tüm ihtiyacı karşılar; Binance REST 1 sayfa yeter.
    internal const int FiveMinuteBufferCapacity = 200;

    // Loop 81 — Bollinger sabit (20, 2.0) — pattern detector'lar paylaşır.
    internal const int BollingerPeriod = 20;
    internal const decimal BollingerStdDev = 2.0m;
    internal const int BollingerBbwMinWindow = 6;

    // Loop 81 — ADX(14) sabit periyot.
    internal const int AdxPeriod = 14;

    // Loop 81 — Donchian channel periyot.
    internal const int DonchianPeriod = 20;

    // Loop 81 — Volume / trade-count window (volume-surge gate).
    internal const int VolumeWindow = 20;

    // Loop 81 — RecentBars uzunluğu — pattern detector ihtiyacı (Donchian + 3-bar
    // higher-low için 30 bar yeterli; memory hafif ~2.4KB).
    internal const int RecentBarsCount = 30;

    // Loop 81 — Indikator sabit periyotlar.
    internal const int Rsi14Period = 14;
    internal const int Ema9Period = 9;
    internal const int Ema21Period = 21;
    internal const int Ema50Period = 50;
    internal const int Ema200Period = 200;
    internal const int Atr14Period = 14;
    internal const int Macd12 = 12;
    internal const int Macd26 = 26;

    // Loop 81 — Snapshot warmup eşiği. EMA200 zorunlu olduğu için 200 bar
    // dolmadan snapshot null döner.
    internal const int WarmupCompletedBarThreshold = 200;

    // Loop 81 — BookTicker yoksa SpreadPct için fallback (anormal yüksek
    // değer ⇒ SpreadGuardGate hard-skip eder). 1.0 = %100 → her threshold'u aşar.
    internal const decimal MissingSpreadFallback = 1m;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IBinanceMarketStream _stream;
    private readonly IOptionsMonitor<BinanceOptions> _options;
    private readonly IBookTickerReader _bookTickers;
    private readonly ILogger<MarketIndicatorService> _logger;

    private readonly ConcurrentDictionary<string, SymbolState> _state =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly CancellationTokenSource _cts = new();
    private Task? _consumerTask;

    public MarketIndicatorService(
        IServiceScopeFactory scopeFactory,
        IBinanceMarketStream stream,
        IOptionsMonitor<BinanceOptions> options,
        IBookTickerReader bookTickers,
        ILogger<MarketIndicatorService> logger)
    {
        _scopeFactory = scopeFactory;
        _stream = stream;
        _options = options;
        _bookTickers = bookTickers;
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
    /// Loop 81 — Tek paylaşılan BarSnapshot. 200 bar dolmadan <c>null</c>
    /// döner. Pattern detector'lar bu snapshot üzerinden çalışır.
    /// </summary>
    public BarSnapshot? TryGetBarSnapshot(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol)) return null;
        if (!_state.TryGetValue(symbol, out var state)) return null;

        IReadOnlyList<WsKlinePayload> bars;
        lock (state.SyncRoot)
        {
            bars = state.FiveMinute.Snapshot();
        }

        if (bars.Count < WarmupCompletedBarThreshold)
        {
            return null;
        }

        var klines = ToKlineList(bars);
        var current = klines[^1];
        var previous = klines[^2];

        // RSI14 curr — son (RsiPeriod + 1) bar
        var rsiCurrWindow = klines.GetRange(klines.Count - (Rsi14Period + 1), Rsi14Period + 1);
        var rsi14 = Evaluators.Indicators.Rsi(rsiCurrWindow, Rsi14Period);

        // RSI14 prev — pencereyi 1 bar geriye kaydır
        var rsiPrevWindow = klines.GetRange(klines.Count - 1 - (Rsi14Period + 1), Rsi14Period + 1);
        var rsi14Prev = Evaluators.Indicators.Rsi(rsiPrevWindow, Rsi14Period);

        // EMA seti
        var ema9 = Evaluators.Indicators.Ema(klines, period: Ema9Period, endIndex: klines.Count - 1);
        var ema9Prev = Evaluators.Indicators.Ema(klines, period: Ema9Period, endIndex: klines.Count - 2);
        var ema21 = Evaluators.Indicators.Ema(klines, period: Ema21Period, endIndex: klines.Count - 1);
        var ema21Prev5 = klines.Count >= 6
            ? Evaluators.Indicators.Ema(klines, period: Ema21Period, endIndex: klines.Count - 6)
            : ema21;
        var ema50 = Evaluators.Indicators.Ema(klines, period: Ema50Period, endIndex: klines.Count - 1);
        var ema200 = Evaluators.Indicators.Ema(klines, period: Ema200Period, endIndex: klines.Count - 1);

        // ATR(14)
        var atrWindow = klines.GetRange(klines.Count - (Atr14Period + 1), Atr14Period + 1);
        var atr14 = Evaluators.Indicators.Atr(atrWindow, Atr14Period);

        // Bollinger Bands (20, 2)
        var (bbMid, bbUpper, bbLower) = Evaluators.Indicators.BollingerBands(
            klines, BollingerPeriod, BollingerStdDev);
        var bbw = bbMid > 0m ? (bbUpper - bbLower) / bbMid : 0m;
        var bbwMin6 = Evaluators.Indicators.BollingerBandWidthMin(
            klines, BollingerPeriod, BollingerStdDev, BollingerBbwMinWindow);

        // ADX(14)
        var adx14 = Evaluators.Indicators.Adx(klines, AdxPeriod);

        // Volume / TradeCount averages
        var avgVolume = Evaluators.Indicators.AverageVolume(klines, VolumeWindow);
        var tcWindow = klines.GetRange(klines.Count - VolumeWindow, VolumeWindow);
        var avgTradeCount = Evaluators.Indicators.TradeCountAvg(tcWindow, VolumeWindow);

        // Donchian (20)
        var (donchHigh, donchLow) = Evaluators.Indicators.Donchian(klines, DonchianPeriod);

        // VWAP — full buffer
        var vwap = Evaluators.Indicators.Vwap(klines);

        // MACD line curr + prev
        var macdLine = Evaluators.Indicators.Macd(klines, Macd12, Macd26);
        // Prev MACD: son barı çıkararak hesapla
        var klinesPrev = klines.GetRange(0, klines.Count - 1);
        var macdLinePrev = klinesPrev.Count >= Macd26 + 1
            ? Evaluators.Indicators.Macd(klinesPrev, Macd12, Macd26)
            : 0m;

        // SpreadPct — canlı BookTicker (yoksa anormal yüksek fallback)
        decimal spreadPct = MissingSpreadFallback;
        var ticker = _bookTickers.GetLatest(symbol);
        if (ticker is not null && ticker.AskPrice > 0m && ticker.BidPrice > 0m)
        {
            spreadPct = (ticker.AskPrice - ticker.BidPrice) / ticker.AskPrice;
        }

        // RecentBars — son 30 bar (oldest-first), Donchian/Higher-Low/RSI sequence için
        var recentStart = Math.Max(0, klines.Count - RecentBarsCount);
        var recentBars = klines.GetRange(recentStart, klines.Count - recentStart);

        return new BarSnapshot(
            Symbol: symbol.ToUpperInvariant(),
            BarOpenTime: current.OpenTime,
            BarCloseTime: current.CloseTime,
            Open: current.OpenPrice,
            High: current.HighPrice,
            Low: current.LowPrice,
            Close: current.ClosePrice,
            Volume: current.Volume,
            TradeCount: current.TradeCount,
            PrevOpen: previous.OpenPrice,
            PrevHigh: previous.HighPrice,
            PrevLow: previous.LowPrice,
            PrevClose: previous.ClosePrice,
            PrevTradeCount: previous.TradeCount,
            Rsi14: rsi14,
            Rsi14Prev: rsi14Prev,
            Ema9: ema9,
            Ema9Prev: ema9Prev,
            Ema21: ema21,
            Ema21Prev5: ema21Prev5,
            Ema50: ema50,
            Ema200: ema200,
            Atr14: atr14,
            BollingerLower: bbLower,
            BollingerMiddle: bbMid,
            BollingerUpper: bbUpper,
            BollingerBandWidth: bbw,
            BollingerBandWidthMin6: bbwMin6,
            Adx14: adx14,
            AvgVolume20: avgVolume,
            AvgTradeCount20: avgTradeCount,
            DonchianHigh20: donchHigh,
            DonchianLow20: donchLow,
            Vwap: vwap,
            MacdLine: macdLine,
            MacdLinePrev: macdLinePrev,
            SpreadPct: spreadPct,
            RecentBars: recentBars);
    }

    /// <summary>
    /// Test-friendly injection path — infrastructure tests seed the buffer
    /// directly without starting the hosted service.
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
    /// </summary>
    private async Task MaybePublishWarmupAsync(string symbol, CancellationToken ct)
    {
        if (!_state.TryGetValue(symbol, out var state)) return;

        int fiveMinCount;
        lock (state.SyncRoot)
        {
            if (state.WarmupEventPublished) return;
            fiveMinCount = state.FiveMinute.Count;
            if (fiveMinCount < WarmupCompletedBarThreshold) return;
            state.WarmupEventPublished = true;
        }

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var publisher = scope.ServiceProvider.GetRequiredService<IPublisher>();
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
        public IndicatorRollingBuffer FiveMinute { get; } = new(FiveMinuteBufferCapacity);
        public bool WarmupEventPublished { get; set; }
    }
}
