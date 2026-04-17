using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BinanceBot.Infrastructure.Binance.Streams;

public sealed class BinanceWsSupervisor : BackgroundService
{
    private readonly IOptionsMonitor<BinanceOptions> _options;
    private readonly BinanceStreamBus _bus;
    private readonly ILogger<BinanceWsSupervisor> _logger;

    private volatile WsSupervisorState _state = WsSupervisorState.Disconnected;
    public WsSupervisorState State => _state;

    public BinanceWsSupervisor(
        IOptionsMonitor<BinanceOptions> options,
        BinanceStreamBus bus,
        ILogger<BinanceWsSupervisor> logger)
    {
        _options = options;
        _bus = bus;
        _logger = logger;
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
        if (_state == next) return;
        _logger.LogInformation("WS state {From} -> {To}", _state, next);
        _state = next;
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
            try
            {
                if (!BinanceStreamParser.TryParseCombinedEnvelope(raw, out var streamName, out var data))
                {
                    return;
                }

                if (streamName.Contains("@kline_", StringComparison.OrdinalIgnoreCase))
                {
                    if (BinanceStreamParser.TryParseKline(data, receivedAt, out var kline))
                    {
                        _bus.KlineWriter.TryWrite(kline);
                    }
                }
                else if (streamName.Contains("@bookTicker", StringComparison.OrdinalIgnoreCase))
                {
                    if (BinanceStreamParser.TryParseBookTicker(data, receivedAt, out var bt))
                    {
                        _bus.BookTickerWriter.TryWrite(bt);
                    }
                }
                else if (streamName.Contains("@depth", StringComparison.OrdinalIgnoreCase))
                {
                    if (BinanceStreamParser.TryParseDepthDiff(data, receivedAt, out var depth))
                    {
                        _bus.DepthWriter.TryWrite(depth);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WS frame dispatch error. Frame size: {Size}", raw.Length);
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
