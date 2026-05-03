using System.Buffers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using BinanceBot.Application.Abstractions.Binance;
using BinanceBot.Infrastructure.Binance.Handlers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BinanceBot.Infrastructure.Binance.Streams;

/// <summary>
/// Loop 92 — Futures user-data stream worker (ADR-0025). listenKey lifecycle:
/// <list type="bullet">
///   <item>POST /fapi/v1/listenKey — yeni key veya mevcut key (60dk geçerli).</item>
///   <item>PUT /fapi/v1/listenKey — her 30 dakikada bir yenile.</item>
///   <item>DELETE /fapi/v1/listenKey — shutdown'da.</item>
///   <item>WS: <c>wss://fstream.binancefuture.com/ws/&lt;listenKey&gt;</c>.</item>
/// </list>
///
/// Olaylar:
/// <list type="bullet">
///   <item>ORDER_TRADE_UPDATE — emir durumu (filled, partial, cancelled).</item>
///   <item>ACCOUNT_UPDATE — bakiye + pozisyon değişimi.</item>
///   <item>MARGIN_CALL — margin uyarısı.</item>
///   <item>listenKeyExpired — key doldu, yeni POST gerekli.</item>
///   <item>ACCOUNT_CONFIG_UPDATE — leverage/marginType değişti.</item>
/// </list>
///
/// MVP: olayları INFO log'lar. Order/Position aggregate update'leri commit
/// 9+'da entegre edilecek (mevcut <c>OrderFilledPositionHandler</c> notification
/// flow'u). Reconnect+heartbeat aynı pattern <see cref="FuturesWsSupervisor"/>'a
/// benzer; private stream'in kendi listen key'i olduğu için ayrı supervisor.
/// </summary>
public sealed class FuturesUserDataStreamWorker : BackgroundService
{
    private static readonly TimeSpan ListenKeyRefreshInterval = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan ReconnectInitialDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ReconnectMaxDelay = TimeSpan.FromSeconds(30);

    private readonly IHttpClientFactory _httpFactory;
    private readonly IOptionsMonitor<BinanceOptions> _options;
    private readonly ILogger<FuturesUserDataStreamWorker> _logger;
    private readonly IBinanceCredentialsProvider _credentials;

    public FuturesUserDataStreamWorker(
        IHttpClientFactory httpFactory,
        IOptionsMonitor<BinanceOptions> options,
        IBinanceCredentialsProvider credentials,
        ILogger<FuturesUserDataStreamWorker> logger)
    {
        _httpFactory = httpFactory;
        _options = options;
        _credentials = credentials;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_credentials.HasTestnetCredentials())
        {
            _logger.LogInformation("FuturesUserDataStream skipped (no testnet credentials)");
            return;
        }

        var attempt = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            string? listenKey = null;
            try
            {
                attempt++;
                listenKey = await CreateListenKeyAsync(stoppingToken);
                if (string.IsNullOrEmpty(listenKey))
                {
                    _logger.LogWarning("listenKey null, retrying");
                    await DelayBackoffAsync(attempt, stoppingToken);
                    continue;
                }

                _logger.LogInformation("Futures listenKey acquired (suffix={Suffix})",
                    listenKey.Length > 8 ? listenKey[^8..] : listenKey);

                using var refreshTimer = new PeriodicTimer(ListenKeyRefreshInterval);
                _ = RefreshLoopAsync(listenKey, refreshTimer, stoppingToken);

                await ConnectAndStreamAsync(listenKey, stoppingToken);
                attempt = 0;  // success — reset backoff
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FuturesUserDataStream error attempt={Attempt}", attempt);
            }
            finally
            {
                if (!string.IsNullOrEmpty(listenKey))
                {
                    try
                    {
                        await DeleteListenKeyAsync(listenKey, CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "listenKey DELETE failed (non-fatal)");
                    }
                }
            }

            if (!stoppingToken.IsCancellationRequested)
            {
                await DelayBackoffAsync(attempt, stoppingToken);
            }
        }
    }

    private async Task<string?> CreateListenKeyAsync(CancellationToken ct)
    {
        var http = _httpFactory.CreateClient(BinanceFuturesClient.HttpClientName);
        using var msg = new HttpRequestMessage(HttpMethod.Post, "/fapi/v1/listenKey");
        // ListenKey endpoint API key gerektirir, signature gerektirmez.
        // ApiKeyHandler X-MBX-APIKEY header'ını ekler.
        using var resp = await http.SendAsync(msg, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("listenKey POST failed {Status} {Body}", resp.StatusCode, body);
            return null;
        }

        var payload = await resp.Content.ReadFromJsonAsync<ListenKeyResponse>(ct);
        return payload?.ListenKey;
    }

    private async Task RefreshLoopAsync(string listenKey, PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                var http = _httpFactory.CreateClient(BinanceFuturesClient.HttpClientName);
                using var msg = new HttpRequestMessage(HttpMethod.Put, "/fapi/v1/listenKey");
                using var resp = await http.SendAsync(msg, ct);
                if (resp.IsSuccessStatusCode)
                {
                    _logger.LogDebug("listenKey refreshed");
                }
                else
                {
                    var body = await resp.Content.ReadAsStringAsync(ct);
                    _logger.LogWarning("listenKey PUT failed {Status} {Body}", resp.StatusCode, body);
                }
            }
        }
        catch (OperationCanceledException) { /* shutdown */ }
        catch (Exception ex)
        {
            _logger.LogError(ex, "listenKey refresh loop crashed");
        }
    }

    private async Task DeleteListenKeyAsync(string listenKey, CancellationToken ct)
    {
        var http = _httpFactory.CreateClient(BinanceFuturesClient.HttpClientName);
        using var msg = new HttpRequestMessage(HttpMethod.Delete, "/fapi/v1/listenKey");
        using var resp = await http.SendAsync(msg, ct);
        if (resp.IsSuccessStatusCode)
        {
            _logger.LogDebug("listenKey deleted");
        }
    }

    private async Task ConnectAndStreamAsync(string listenKey, CancellationToken ct)
    {
        var opts = _options.CurrentValue;
        var url = $"{opts.WsBaseUrl.TrimEnd('/')}/ws/{listenKey}";
        _logger.LogInformation("Connecting Futures user-data WS {Url}", MaskListenKey(url));

        using var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);

        await socket.ConnectAsync(new Uri(url), ct);
        _logger.LogInformation("Futures user-data WS connected");

        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        var ms = new MemoryStream();
        try
        {
            while (!ct.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                ms.SetLength(0);
                ValueWebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(buffer.AsMemory(), ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        _logger.LogWarning("user-data WS close frame {Status}", socket.CloseStatus);
                        return;
                    }
                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                DispatchEvent(ms.ToArray());
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private void DispatchEvent(byte[] raw)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (!root.TryGetProperty("e", out var evType)) return;
            var type = evType.GetString();
            switch (type)
            {
                case "ORDER_TRADE_UPDATE":
                    LogOrderTradeUpdate(root);
                    break;
                case "ACCOUNT_UPDATE":
                    LogAccountUpdate(root);
                    break;
                case "MARGIN_CALL":
                    _logger.LogWarning("MARGIN_CALL received: {Body}", System.Text.Encoding.UTF8.GetString(raw));
                    break;
                case "listenKeyExpired":
                    _logger.LogWarning("listenKey expired — supervisor will reconnect");
                    break;
                case "ACCOUNT_CONFIG_UPDATE":
                    _logger.LogInformation("ACCOUNT_CONFIG_UPDATE: {Body}", System.Text.Encoding.UTF8.GetString(raw));
                    break;
                default:
                    _logger.LogDebug("Unknown user-data event type={Type}", type);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "user-data event dispatch error");
        }
    }

    private void LogOrderTradeUpdate(System.Text.Json.JsonElement root)
    {
        if (!root.TryGetProperty("o", out var o)) return;
        var symbol = o.TryGetProperty("s", out var s) ? s.GetString() : "?";
        var side = o.TryGetProperty("S", out var sd) ? sd.GetString() : "?";
        var status = o.TryGetProperty("X", out var st) ? st.GetString() : "?";
        var clientId = o.TryGetProperty("c", out var c) ? c.GetString() : "?";
        _logger.LogInformation(
            "ORDER_TRADE_UPDATE symbol={Symbol} side={Side} status={Status} cid={Cid}",
            symbol, side, status, clientId);
    }

    private void LogAccountUpdate(System.Text.Json.JsonElement root)
    {
        if (!root.TryGetProperty("a", out var a)) return;
        var reason = a.TryGetProperty("m", out var m) ? m.GetString() : "?";
        _logger.LogInformation("ACCOUNT_UPDATE reason={Reason}", reason);
    }

    private async Task DelayBackoffAsync(int attempt, CancellationToken ct)
    {
        var capped = Math.Min(
            ReconnectMaxDelay.TotalMilliseconds,
            ReconnectInitialDelay.TotalMilliseconds * Math.Pow(2, Math.Min(8, attempt)));
        var jitter = Random.Shared.NextDouble() * 0.3;
        var delay = TimeSpan.FromMilliseconds(capped * (1 + jitter));
        try
        {
            await Task.Delay(delay, ct);
        }
        catch (OperationCanceledException) { }
    }

    private static string MaskListenKey(string url)
    {
        // Log'da listenKey suffix'inin son 8 karakterini bırakır, kalan kısmı maskeler.
        var idx = url.LastIndexOf('/');
        if (idx < 0 || idx + 8 >= url.Length) return url;
        var prefix = url[..(idx + 1)];
        var key = url[(idx + 1)..];
        return prefix + new string('*', Math.Max(0, key.Length - 8)) + key[^8..];
    }

    private sealed record ListenKeyResponse(string ListenKey);
}
