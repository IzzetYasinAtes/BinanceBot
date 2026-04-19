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
/// ADR-0015 §15.6 + §15.7. Maintains per-symbol rolling buffers of closed 1m and 1h
/// bars, computes <see cref="MarketIndicatorSnapshot"/> on demand for the evaluator.
///
/// Lifecycle:
///   1. On host start, warm up 1m (1440 bars) and 1h (60 bars) per symbol via REST
///      <c>GET /api/v3/klines</c>. Per-symbol failures are logged and skipped — the
///      service is best-effort and never fail-fasts the host.
///   2. Consume the shared <see cref="IBinanceMarketStream"/> kline channel and
///      upsert closed bars into the appropriate buffer.
///
/// Thread model:
///   - Writes (REST warmup + WS consumer) are serialised per (symbol, interval) by
///     a lightweight lock around <see cref="IndicatorRollingBuffer"/>.
///   - Reads (<see cref="TryGetSnapshot"/>) take the same lock, copy the buffer
///     contents and compute indicators — latency is O(bars) which is dominated by
///     the 1440-bar VWAP sum (&lt;1ms in practice).
/// </summary>
public sealed class MarketIndicatorService : IMarketIndicatorService, IHostedService
{
    // ADR-0015 §15.2 + §15.6 defaults. Parameters here are strictly service-level
    // (buffer sizing); strategy-level parameters live in evaluator JSON.
    internal const int OneMinuteBufferCapacity = 1440; // rolling 24h VWAP window
    internal const int OneHourBufferCapacity = 60;     // 21-period EMA needs ~50 bars warm

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
            var buf = interval == KlineInterval.OneMinute ? state.OneMinute : state.OneHour;
            buf.Upsert(bar);
        }
        return true;
    }

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
            await foreach (var payload in _stream.KlineUpdates(ct).WithCancellation(ct))
            {
                if (!payload.IsClosed)
                {
                    continue;
                }

                if (payload.Interval != KlineInterval.OneMinute
                    && payload.Interval != KlineInterval.OneHour)
                {
                    continue;
                }

                if (!_state.TryGetValue(payload.Symbol, out var state))
                {
                    continue;
                }

                lock (state.SyncRoot)
                {
                    var buf = payload.Interval == KlineInterval.OneMinute
                        ? state.OneMinute
                        : state.OneHour;
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
                var buf = interval == KlineInterval.OneMinute ? state.OneMinute : state.OneHour;
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

        // ADR-0016 §16.9.6 — one-shot latch: when warmup budget crosses threshold
        // we publish IndicatorWarmupCompletedEvent exactly once per symbol.
        public bool WarmupEventPublished { get; set; }
    }
}
