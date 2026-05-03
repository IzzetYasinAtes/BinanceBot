using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using BinanceBot.Application.Abstractions.Binance;
using BinanceBot.Domain.SystemEvents.Events;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BinanceBot.Infrastructure.Binance.Streams;

public sealed class FuturesWsSupervisor : BackgroundService, IWsReadinessProbe
{
    private readonly IOptionsMonitor<BinanceOptions> _options;
    private readonly BinanceStreamBus _bus;
    private readonly ILogger<FuturesWsSupervisor> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    private volatile WsSupervisorState _state = WsSupervisorState.Disconnected;
    private volatile bool _everConnected;

    public WsSupervisorState State => _state;

    /// <inheritdoc />
    /// <remarks>
    /// True after the first successful WS connect; remains true across reconnects so
    /// the backfill probe is not blocked by transient drops.
    /// </remarks>
    public bool IsReady => _everConnected;

    public FuturesWsSupervisor(
        IOptionsMonitor<BinanceOptions> options,
        BinanceStreamBus bus,
        ILogger<FuturesWsSupervisor> logger,
        IServiceScopeFactory scopeFactory)
    {
        _options = options;
        _bus = bus;
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var attempt = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            var options = _options.CurrentValue;
            try
            {
                attempt++;
                SetState(WsSupervisorState.Connecting);
                await using var session = new WsSession(options, _bus, _logger);
                await session.RunAsync(stoppingToken, SetState);
                attempt = 0;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WS supervisor loop exception (attempt {Attempt})", attempt);
                SetState(WsSupervisorState.Reconnecting);
            }

            if (stoppingToken.IsCancellationRequested) break;

            var delay = ComputeBackoff(options, attempt);
            _logger.LogWarning("Reconnecting in {DelayMs}ms (attempt {Attempt})", delay.TotalMilliseconds, attempt);
            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        SetState(WsSupervisorState.Disconnected);
    }

    private void SetState(WsSupervisorState next)
    {
        if (next == WsSupervisorState.Connected)
        {
            _everConnected = true;
        }
        if (_state == next) return;
        var previous = _state;
        _logger.LogInformation("WS state {From} -> {To}", previous, next);
        _state = next;

        // ADR-0016 §16.9.6 — fire-and-forget WS state transition to the SystemEvents
        // pipe. Telemetry path; supervisor loop must never be blocked by it.
        _ = Task.Run(async () =>
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var publisher = scope.ServiceProvider.GetRequiredService<IPublisher>();
                await publisher.Publish(
                    new WsConnectionStateChangedEvent(previous.ToString(), next.ToString()),
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "WsConnectionStateChanged publish failed");
            }
        });
    }

    private static TimeSpan ComputeBackoff(BinanceOptions options, int attempt)
    {
        if (attempt <= 1)
        {
            return TimeSpan.FromMilliseconds(options.WsReconnectInitialDelayMs);
        }

        var capped = Math.Min(
            options.WsReconnectMaxDelayMs,
            options.WsReconnectInitialDelayMs * Math.Pow(2, Math.Min(10, attempt - 1)));
        var jitter = Random.Shared.NextDouble() * 0.3;
        return TimeSpan.FromMilliseconds(capped * (1 + jitter));
    }

    private sealed class WsSession : IAsyncDisposable
    {
        private readonly BinanceOptions _options;
        private readonly BinanceStreamBus _bus;
        private readonly ILogger _logger;
        private readonly ClientWebSocket _socket = new();

        public WsSession(BinanceOptions options, BinanceStreamBus bus, ILogger logger)
        {
            _options = options;
            _bus = bus;
            _logger = logger;
            _socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
        }

        public async Task RunAsync(CancellationToken cancellationToken, Action<WsSupervisorState> setState)
        {
            var url = BuildStreamUrl();
            _logger.LogInformation("Connecting WS {Url}", url);

            await _socket.ConnectAsync(new Uri(url), cancellationToken);
            setState(WsSupervisorState.Connected);
            setState(WsSupervisorState.Subscribing);
            setState(WsSupervisorState.Streaming);

            await ReceiveLoopAsync(cancellationToken);
        }

        private string BuildStreamUrl()
        {
            var parts = new HashSet<string>(StringComparer.Ordinal);
            foreach (var symbol in _options.Symbols.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var s = symbol.ToLowerInvariant();
                foreach (var interval in _options.KlineIntervals.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    parts.Add($"{s}@kline_{interval}");
                }
                parts.Add($"{s}@bookTicker");
                parts.Add($"{s}@depth@100ms");
            }
            var streams = string.Join("/", parts);
            return $"{_options.WsBaseUrl.TrimEnd('/')}/stream?streams={streams}";
        }

        private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
            var ms = new MemoryStream();
            var lastActivity = DateTimeOffset.UtcNow;
            var pongTimeout = TimeSpan.FromMilliseconds(_options.WsPongTimeoutMs);

            try
            {
                while (!cancellationToken.IsCancellationRequested && _socket.State == WebSocketState.Open)
                {
                    ms.SetLength(0);
                    ValueWebSocketReceiveResult result;

                    try
                    {
                        result = await _socket.ReceiveAsync(buffer.AsMemory(), cancellationToken)
                            .AsTask()
                            .WaitAsync(pongTimeout, cancellationToken);
                    }
                    catch (TimeoutException)
                    {
                        _logger.LogWarning("WS inactive > {PongMs}ms, forcing reconnect", _options.WsPongTimeoutMs);
                        throw new WebSocketException("Activity watchdog timeout");
                    }

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        _logger.LogWarning("WS close frame {Status} {Desc}",
                            _socket.CloseStatus, _socket.CloseStatusDescription);
                        await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, cancellationToken);
                        return;
                    }

                    ms.Write(buffer, 0, result.Count);

                    while (!result.EndOfMessage)
                    {
                        result = await _socket.ReceiveAsync(buffer.AsMemory(), cancellationToken);
                        ms.Write(buffer, 0, result.Count);
                    }

                    lastActivity = DateTimeOffset.UtcNow;
                    DispatchFrame(ms.ToArray(), lastActivity);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        private void DispatchFrame(byte[] raw, DateTimeOffset receivedAt)
        {
            string streamName = string.Empty;
            try
            {
                if (!FuturesStreamParser.TryParseCombinedEnvelope(raw, out streamName, out var data))
                {
                    // Loop 24 diagnostic — non-envelope frames (subscription acks, error
                    // payloads) were silently dropped. Log the first 200 bytes so we can
                    // see subscription errors (e.g. "Invalid stream" for unsupported
                    // intervals on SPOT testnet).
                    var preview = System.Text.Encoding.UTF8.GetString(raw, 0, Math.Min(raw.Length, 200));
                    _logger.LogWarning("WS non-envelope frame ({Size}B): {Preview}", raw.Length, preview);
                    return;
                }

                if (streamName.Contains("@kline_", StringComparison.OrdinalIgnoreCase))
                {
                    if (FuturesStreamParser.TryParseKline(data, receivedAt, out var kline))
                    {
                        // Loop 24 diagnostic — explicit per-frame kline log so we can
                        // verify 30s bars actually arrive from the WS. MicroScalper ingestion
                        // debugging requires this to be visible at INFO level.
                        _logger.LogInformation(
                            "Kline received {Symbol} {Interval} openTime={OpenTime:HH:mm:ss} closed={IsClosed} close={Close}",
                            kline.Symbol, kline.Interval, kline.OpenTime, kline.IsClosed, kline.Close);
                        _bus.PublishKline(kline);
                    }
                    else
                    {
                        _logger.LogWarning("Kline parse failed stream={Stream}", streamName);
                    }
                }
                else if (streamName.Contains("@bookTicker", StringComparison.OrdinalIgnoreCase))
                {
                    if (FuturesStreamParser.TryParseBookTicker(data, receivedAt, out var bt))
                    {
                        _bus.PublishBookTicker(bt);
                    }
                }
                else if (streamName.Contains("@depth", StringComparison.OrdinalIgnoreCase))
                {
                    if (FuturesStreamParser.TryParseDepthDiff(data, receivedAt, out var depth))
                    {
                        _bus.PublishDepth(depth);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "WS frame dispatch error stream={Stream} size={Size}",
                    streamName, raw.Length);
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "shutdown", cts.Token);
                }
            }
            catch { }
            _socket.Dispose();
        }
    }
}
