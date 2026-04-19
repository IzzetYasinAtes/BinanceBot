using BinanceBot.Application.Abstractions;
using BinanceBot.Application.Abstractions.Binance;
using BinanceBot.Domain.MarketData;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BinanceBot.Infrastructure.Binance.Workers;

/// <summary>
/// One-shot REST kline backfill executed at host start. Probes the WS readiness
/// (max <see cref="WsProbeBudget"/>) before issuing REST requests so we do not
/// race the WS subscriber, then for each (symbol, interval) pair fetches up to
/// <see cref="BinanceOptions.BackfillLimit"/> historical bars and persists them
/// through the same <see cref="KlinePersister"/> the WS path uses. Per-symbol
/// failures are logged and skipped — the worker is best-effort and never
/// fail-fasts the host.
/// </summary>
/// <remarks>ADR-0009 — REST kline backfill on boot.</remarks>
public sealed class KlineBackfillWorker : IHostedService
{
    internal static readonly TimeSpan WsProbeBudget = TimeSpan.FromSeconds(10);
    internal static readonly TimeSpan WsProbePollInterval = TimeSpan.FromMilliseconds(200);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<BinanceOptions> _options;
    private readonly IWsReadinessProbe _probe;
    private readonly IClock _clock;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<KlineBackfillWorker> _logger;
    private readonly TimeSpan _wsProbeBudget;
    private readonly TimeSpan _wsProbePollInterval;

    private readonly CancellationTokenSource _cts = new();
    private Task? _execution;

    public KlineBackfillWorker(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<BinanceOptions> options,
        IWsReadinessProbe probe,
        IClock clock,
        ILogger<KlineBackfillWorker> logger)
        : this(scopeFactory, options, probe, clock, TimeProvider.System, logger,
               WsProbeBudget, WsProbePollInterval)
    {
    }

    // Test-friendly constructor: permits compressed timeouts and a fake TimeProvider.
    internal KlineBackfillWorker(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<BinanceOptions> options,
        IWsReadinessProbe probe,
        IClock clock,
        TimeProvider timeProvider,
        ILogger<KlineBackfillWorker> logger,
        TimeSpan wsProbeBudget,
        TimeSpan wsProbePollInterval)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _probe = probe;
        _clock = clock;
        _timeProvider = timeProvider;
        _logger = logger;
        _wsProbeBudget = wsProbeBudget;
        _wsProbePollInterval = wsProbePollInterval;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Fire-and-forget so the host startup is not blocked by REST latency.
        // The linked CTS is cancelled in StopAsync to drain on shutdown.
        _execution = Task.Run(() => BackfillAllAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            _cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already stopped — nothing to cancel.
        }

        if (_execution is null)
        {
            _cts.Dispose();
            return;
        }

        try
        {
            // Wait for the backfill to finish or for the host's stop deadline,
            // whichever comes first. We never want StopAsync to throw.
            await Task.WhenAny(_execution, Task.Delay(Timeout.Infinite, cancellationToken));
        }
        catch
        {
            // Swallow — host shutdown should not propagate.
        }
        finally
        {
            _cts.Dispose();
        }
    }

    /// <summary>
    /// Exposed for tests: runs the backfill on the current thread and awaits completion.
    /// Production code uses <see cref="StartAsync"/> which fires this in the background.
    /// </summary>
    internal Task RunOnceAsync(CancellationToken ct) => BackfillAllAsync(ct);

    private async Task BackfillAllAsync(CancellationToken ct)
    {
        var options = _options.CurrentValue;

        if (!options.BackfillEnabled)
        {
            _logger.LogInformation("Kline backfill disabled by configuration; skipping.");
            return;
        }

        await WaitForWsReadyAsync(ct);

        if (ct.IsCancellationRequested)
        {
            return;
        }

        var symbols = options.Symbols.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var intervals = options.BackfillIntervals
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var limit = options.BackfillLimit;

        _logger.LogInformation(
            "Kline backfill starting: {SymbolCount} symbol(s) x {IntervalCount} interval(s), limit={Limit}",
            symbols.Length, intervals.Length, limit);

        foreach (var symbol in symbols)
        {
            if (ct.IsCancellationRequested) return;

            foreach (var intervalCode in intervals)
            {
                if (ct.IsCancellationRequested) return;

                try
                {
                    await BackfillOneAsync(symbol, intervalCode, limit, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Kline backfill failed for {Symbol} {Interval}; continuing with next pair",
                        symbol, intervalCode);
                }
            }
        }

        _logger.LogInformation("Kline backfill completed.");
    }

    private async Task WaitForWsReadyAsync(CancellationToken ct)
    {
        var deadline = _timeProvider.GetUtcNow() + _wsProbeBudget;
        while (!_probe.IsReady && _timeProvider.GetUtcNow() < deadline)
        {
            if (ct.IsCancellationRequested) return;
            try
            {
                await Task.Delay(_wsProbePollInterval, _timeProvider, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        if (!_probe.IsReady)
        {
            _logger.LogWarning(
                "WS not ready within {BudgetMs}ms; proceeding with REST backfill anyway",
                (int)_wsProbeBudget.TotalMilliseconds);
        }
    }

    private async Task BackfillOneAsync(
        string symbol,
        string intervalCode,
        int limit,
        CancellationToken ct)
    {
        KlineInterval interval;
        try
        {
            interval = KlineIntervalExtensions.FromBinanceCode(intervalCode);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            _logger.LogError(ex,
                "Unknown backfill interval code {Interval}; skipping {Symbol}",
                intervalCode, symbol);
            return;
        }

        // Loop 23 blocker fix (BLOCKER-1): Binance REST /api/v3/klines does NOT
        // accept interval=30s (400 Bad Request). Guard defensively even if config
        // regresses — 30s bars come from WS streams only.
        if (interval == KlineInterval.ThirtySeconds)
        {
            _logger.LogInformation(
                "Kline backfill {Symbol} 30s: REST skipped (Binance does not support 30s on " +
                "REST); WS stream will populate bars",
                symbol);
            return;
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var marketData = scope.ServiceProvider.GetRequiredService<IBinanceMarketData>();
        var persister = scope.ServiceProvider.GetRequiredService<IKlinePersister>();

        var bars = await marketData.GetKlinesAsync(
            symbol, interval, limit, startTime: null, endTime: null, ct);

        if (bars.Count == 0)
        {
            _logger.LogWarning(
                "Backfill returned 0 bars for {Symbol} {Interval}; WS warmup gap possible",
                symbol, intervalCode);
            return;
        }

        var nowUtc = _clock.UtcNow;
        var persisted = 0;

        foreach (var bar in bars)
        {
            if (ct.IsCancellationRequested) return;

            var payload = ToWsPayload(symbol, interval, bar, nowUtc);
            // ADR-0010: backfill must not replay historical bars as live signals.
            await persister.PersistAsync(payload, ct, emitDomainEvents: false);
            persisted++;
        }

        _logger.LogInformation(
            "Kline backfill {Symbol} {Interval}: persisted {Count} bar(s)",
            symbol, intervalCode, persisted);
    }

    private static WsKlinePayload ToWsPayload(
        string symbol,
        KlineInterval interval,
        RestKlineDto bar,
        DateTimeOffset nowUtc)
    {
        var isClosed = bar.CloseTime < nowUtc;
        return new WsKlinePayload(
            Symbol: symbol.ToUpperInvariant(),
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
            IsClosed: isClosed);
    }
}
