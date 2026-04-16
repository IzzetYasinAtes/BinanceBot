---
name: backend-polly-resiliency
description: REST için Polly retry + circuit breaker + timeout pattern'ı. WS için BackgroundService supervisor + Channel<T> producer/consumer + reconnect+replay. Binance özelinde 429/418 handling. backend-dev agent'ın resiliency yazarken kullandığı skill.
---

# backend-polly-resiliency

Binance gibi harici servislerle konuşmak = flaky. Polly (REST) + custom supervisor (WS) şart.

## REST — IHttpClientFactory + Polly

```csharp
// Infrastructure/DependencyInjection.cs
using Microsoft.Extensions.Http.Resilience;
using Polly;

services.AddHttpClient("binance-rest", client =>
{
    client.BaseAddress = new Uri("https://api.binance.com");
    client.Timeout = TimeSpan.FromSeconds(10);
})
.AddStandardResilienceHandler(options =>
{
    options.Retry.MaxRetryAttempts = 3;
    options.Retry.Delay = TimeSpan.FromSeconds(1);
    options.Retry.BackoffType = DelayBackoffType.Exponential;
    options.Retry.UseJitter = true;
    options.Retry.ShouldHandle = args => new ValueTask<bool>(
        args.Outcome.Result is { StatusCode: System.Net.HttpStatusCode.TooManyRequests } ||
        args.Outcome.Result is { StatusCode: System.Net.HttpStatusCode.ServiceUnavailable });

    options.CircuitBreaker.FailureRatio = 0.5;
    options.CircuitBreaker.MinimumThroughput = 10;
    options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(30);

    options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(5);
});
```

### Binance Özel: 429 / 418 Handling

```csharp
// DelegatingHandler — Retry-After parse
public sealed class BinanceRetryAfterHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        var resp = await base.SendAsync(req, ct);
        if (resp.StatusCode == (System.Net.HttpStatusCode)429 || resp.StatusCode == (System.Net.HttpStatusCode)418)
        {
            if (resp.Headers.TryGetValues("Retry-After", out var values) &&
                int.TryParse(values.FirstOrDefault(), out var seconds))
            {
                await Task.Delay(TimeSpan.FromSeconds(seconds), ct);
                return await base.SendAsync(req, ct);   // tek retry — Polly zaten N retry yapar
            }
        }
        return resp;
    }
}
```

### Weight Header Log

```csharp
public sealed class BinanceWeightLoggingHandler : DelegatingHandler
{
    private readonly ILogger<BinanceWeightLoggingHandler> _logger;
    public BinanceWeightLoggingHandler(ILogger<BinanceWeightLoggingHandler> logger) => _logger = logger;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        var resp = await base.SendAsync(req, ct);
        if (resp.Headers.TryGetValues("X-MBX-USED-WEIGHT-1M", out var weight))
            _logger.LogInformation("Binance weight 1m: {Weight}", weight.FirstOrDefault());
        return resp;
    }
}
```

## WebSocket — BackgroundService Supervisor Pattern

```csharp
// Infrastructure/MarketData/BinanceWsSupervisor.cs
using System.Net.WebSockets;
using System.Threading.Channels;

public sealed class BinanceWsSupervisor : BackgroundService
{
    private readonly Channel<WsEvent> _channel = Channel.CreateUnbounded<WsEvent>();
    private readonly ILogger<BinanceWsSupervisor> _logger;
    private readonly IEnumerable<string> _streamNames;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var attempt = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                attempt++;
                await ConnectAndPumpAsync(stoppingToken);
                attempt = 0; // başarılı connection → reset
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "WS disconnected, attempt #{Attempt}", attempt);
                var delay = Math.Min(30, (int)Math.Pow(2, attempt));
                await Task.Delay(TimeSpan.FromSeconds(delay), stoppingToken);
            }
        }
    }

    private async Task ConnectAndPumpAsync(CancellationToken ct)
    {
        using var ws = new ClientWebSocket();
        ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);

        var url = new Uri($"wss://stream.binance.com:9443/stream?streams={string.Join('/', _streamNames)}");
        await ws.ConnectAsync(url, ct);

        var buffer = new byte[64 * 1024];
        while (ws.State == WebSocketState.Open)
        {
            var result = await ws.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "server close", ct);
                break;
            }
            var text = System.Text.Encoding.UTF8.GetString(buffer, 0, result.Count);
            await _channel.Writer.WriteAsync(new WsEvent(DateTime.UtcNow, text), ct);
        }
    }

    public ChannelReader<WsEvent> Reader => _channel.Reader;
}

public sealed record WsEvent(DateTime ReceivedUtc, string Payload);
```

Consumer ayrı `BackgroundService` — `Reader.ReadAllAsync(ct)` ile tüketir, handler idempotent olmalı (bkz. `binance-ws-stream-design`).

## Kurallar

- WS reconnect'te **subscribe replay** şart; sessizce yeniden bağlanıp eski stream'leri unutma (veri kaybı).
- Exponential backoff + **jitter** — 2 ^ attempt + Random(0, 1000)ms.
- WS'de max backoff cap 30 saniye.
- REST 418 (IP ban) → durma; admin alarm tetikle, retry'a devam etme.
- Handler'da try/catch — bir mesaj bozulması tüm stream'i durdurmasın; sadece o mesajı dead-letter-log'a at.
- `CancellationToken` her awaited çağrıya zincirle.

## Kaynak

- https://learn.microsoft.com/en-us/dotnet/core/resilience/http-resilience
- https://github.com/App-vNext/Polly
- https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services
- https://learn.microsoft.com/en-us/dotnet/standard/threading/channels
