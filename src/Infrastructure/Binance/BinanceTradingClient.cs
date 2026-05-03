using System.Text.Json;
using Ardalis.Result;
using BinanceBot.Application.Abstractions.Binance;
using BinanceBot.Application.Abstractions.Exchange;
using BinanceBot.Infrastructure.Binance.Handlers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BinanceBot.Infrastructure.Binance;

public sealed class BinanceTradingClient : IExchangeClient
{
    public const string HttpClientName = "binance-trade";

    private readonly HttpClient _http;
    private readonly IOptionsMonitor<BinanceOptions> _options;
    private readonly ILogger<BinanceTradingClient> _logger;

    public BinanceTradingClient(
        HttpClient http,
        IOptionsMonitor<BinanceOptions> options,
        ILogger<BinanceTradingClient> logger)
    {
        _http = http;
        _options = options;
        _logger = logger;
    }

    private bool HasApiCredentials =>
        !string.IsNullOrWhiteSpace(_options.CurrentValue.ApiKey)
        && !string.IsNullOrWhiteSpace(_options.CurrentValue.ApiSecret);

    // Loop 92 — IExchangeClient adapter shims (Result<T> wrapping). Commit 4'te
    // BinanceFuturesClient ile tamamen rewrite edilecek; bu sürüm Spot endpoint'leriyle
    // koşuyor ama interface contract'a uyacak şekilde Result<T> dönüyor.
    Task<Result<TestOrderResponse>> IExchangeClient.PlaceTestOrderAsync(PlaceOrderRequest request, CancellationToken cancellationToken) =>
        WrapAsync(() => PlaceTestOrderAsync(request, cancellationToken));

    Task<Result<LiveOrderResponse>> IExchangeClient.PlaceLiveOrderAsync(PlaceOrderRequest request, CancellationToken cancellationToken) =>
        WrapAsync(() => PlaceLiveOrderAsync(request, cancellationToken));

    Task<Result<CancelOrderResponse>> IExchangeClient.CancelLiveOrderAsync(string symbol, string clientOrderId, CancellationToken cancellationToken) =>
        WrapAsync(() => CancelLiveOrderAsync(symbol, clientOrderId, cancellationToken));

    Task<Result<CancelOrderResponse>> IExchangeClient.CancelTestOrderAsync(string symbol, string clientOrderId, CancellationToken cancellationToken) =>
        WrapAsync(() => CancelTestOrderAsync(symbol, clientOrderId, cancellationToken));

    Task<Result> IExchangeClient.SetLeverageAsync(string symbol, int leverage, CancellationToken cancellationToken)
    {
        // Spot mode'da no-op (futures-only setting). Commit 4'te BinanceFuturesClient gerçek call yapacak.
        _logger.LogDebug("SetLeverage no-op (Spot client) symbol={Symbol} leverage={Leverage}", symbol, leverage);
        return Task.FromResult(Result.Success());
    }

    Task<Result> IExchangeClient.SetMarginTypeAsync(string symbol, MarginType marginType, CancellationToken cancellationToken)
    {
        _logger.LogDebug("SetMarginType no-op (Spot client) symbol={Symbol} marginType={MarginType}", symbol, marginType);
        return Task.FromResult(Result.Success());
    }

    Task<Result> IExchangeClient.SetPositionModeAsync(bool dualSidePosition, CancellationToken cancellationToken)
    {
        _logger.LogDebug("SetPositionMode no-op (Spot client) dualSide={Dual}", dualSidePosition);
        return Task.FromResult(Result.Success());
    }

    Task<Result<ExchangeAccountInfo>> IExchangeClient.GetAccountInfoAsync(CancellationToken cancellationToken)
    {
        // Spot client placeholder — commit 4'te /fapi/v3/balance gerçek call yapacak.
        return Task.FromResult(Result.Success(new ExchangeAccountInfo(0m, 0m, 0m)));
    }

    Task<Result<IReadOnlyList<ExchangePositionDto>>> IExchangeClient.GetOpenPositionsAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(Result.Success((IReadOnlyList<ExchangePositionDto>)Array.Empty<ExchangePositionDto>()));
    }

    private static async Task<Result<T>> WrapAsync<T>(Func<Task<T>> op)
    {
        var value = await op();
        return Result.Success(value);
    }

    public async Task<TestOrderResponse> PlaceTestOrderAsync(PlaceOrderRequest request, CancellationToken cancellationToken)
    {
        if (!HasApiCredentials)
        {
            _logger.LogInformation("TestOrder simulated-accept (no API credentials) {Cid} {Symbol}",
                request.ClientOrderId, request.Symbol);
            return new TestOrderResponse(true, null, "simulated_no_credentials");
        }

        var form = BuildOrderForm(request);
        using var msg = new HttpRequestMessage(HttpMethod.Post, "/api/v3/order/test")
        {
            Content = new FormUrlEncodedContent(form),
        };
        msg.Headers.Add(SignedRequestHandler.SignMarker, "true");

        using var response = await _http.SendAsync(msg, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            _logger.LogInformation("TestOrder accepted {Cid} {Symbol}",
                request.ClientOrderId, request.Symbol);
            return new TestOrderResponse(true, null, null);
        }

        _logger.LogWarning("TestOrder rejected {Status} {Body}", response.StatusCode, body);
        var (code, message) = ParseBinanceError(body);
        return new TestOrderResponse(false, code, message);
    }

    public async Task<LiveOrderResponse> PlaceLiveOrderAsync(PlaceOrderRequest request, CancellationToken cancellationToken)
    {
        if (!_options.CurrentValue.AllowMainnet
            && _options.CurrentValue.RestBaseUrl.Contains("binance.com", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogCritical("Mainnet blocked by AllowMainnet=false (ADR-0006)");
            return new LiveOrderResponse(false, null, "Blocked", 0m, 0m, Array.Empty<LiveFillDto>(),
                "MAINNET_BLOCKED", "AllowMainnet=false guard");
        }
        if (!HasApiCredentials)
        {
            return new LiveOrderResponse(false, null, "Rejected", 0m, 0m, Array.Empty<LiveFillDto>(),
                "NO_CREDENTIALS", "API key/secret missing");
        }

        var form = BuildOrderForm(request);
        using var msg = new HttpRequestMessage(HttpMethod.Post, "/api/v3/order")
        {
            Content = new FormUrlEncodedContent(form),
        };
        msg.Headers.Add(SignedRequestHandler.SignMarker, "true");

        using var response = await _http.SendAsync(msg, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("LiveOrder rejected {Status} {Body}", response.StatusCode, body);
            var (code, message) = ParseBinanceError(body);
            return new LiveOrderResponse(false, null, "Rejected", 0m, 0m, Array.Empty<LiveFillDto>(), code, message);
        }

        return ParseLiveOrder(body);
    }

    public async Task<CancelOrderResponse> CancelLiveOrderAsync(
        string symbol, string clientOrderId, CancellationToken cancellationToken)
    {
        if (!HasApiCredentials)
        {
            return new CancelOrderResponse(false, "NO_CREDENTIALS", "API key/secret missing");
        }

        var query = $"symbol={symbol.ToUpperInvariant()}&origClientOrderId={Uri.EscapeDataString(clientOrderId)}";
        using var msg = new HttpRequestMessage(HttpMethod.Delete, "/api/v3/order?" + query);
        msg.Headers.Add(SignedRequestHandler.SignMarker, "true");

        using var response = await _http.SendAsync(msg, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return new CancelOrderResponse(true, null, null);
        }
        var (code, message) = ParseBinanceError(body);
        return new CancelOrderResponse(false, code, message);
    }

    private static LiveOrderResponse ParseLiveOrder(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        long? orderId = root.TryGetProperty("orderId", out var oid) ? oid.GetInt64() : null;
        var status = root.TryGetProperty("status", out var st) ? st.GetString() ?? "New" : "New";
        var executed = root.TryGetProperty("executedQty", out var eq) ? ParseDecimal(eq.GetString()) : 0m;
        var cummulative = root.TryGetProperty("cummulativeQuoteQty", out var cq) ? ParseDecimal(cq.GetString()) : 0m;

        var fills = new List<LiveFillDto>();
        if (root.TryGetProperty("fills", out var fillsArr) && fillsArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var f in fillsArr.EnumerateArray())
            {
                fills.Add(new LiveFillDto(
                    f.GetProperty("tradeId").GetInt64(),
                    ParseDecimal(f.GetProperty("price").GetString()),
                    ParseDecimal(f.GetProperty("qty").GetString()),
                    ParseDecimal(f.GetProperty("commission").GetString()),
                    f.GetProperty("commissionAsset").GetString() ?? "USDT"));
            }
        }

        return new LiveOrderResponse(true, orderId, status, executed, cummulative, fills, null, null);
    }

    private static decimal ParseDecimal(string? raw) =>
        decimal.Parse(raw ?? "0",
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture);

    public async Task<CancelOrderResponse> CancelTestOrderAsync(
        string symbol, string clientOrderId, CancellationToken cancellationToken)
    {
        if (!HasApiCredentials)
        {
            _logger.LogInformation("CancelOrder simulated-accept {Cid} {Symbol}", clientOrderId, symbol);
            return new CancelOrderResponse(true, null, "simulated_no_credentials");
        }

        var form = new List<KeyValuePair<string, string>>
        {
            new("symbol", symbol.ToUpperInvariant()),
            new("origClientOrderId", clientOrderId),
        };
        var query = string.Join("&", form.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));
        using var msg = new HttpRequestMessage(HttpMethod.Delete, "/api/v3/order?" + query);
        msg.Headers.Add(SignedRequestHandler.SignMarker, "true");

        using var response = await _http.SendAsync(msg, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return new CancelOrderResponse(true, null, null);
        }

        var (code, message) = ParseBinanceError(body);
        return new CancelOrderResponse(false, code, message);
    }

    private static List<KeyValuePair<string, string>> BuildOrderForm(PlaceOrderRequest req)
    {
        var form = new List<KeyValuePair<string, string>>
        {
            new("symbol", req.Symbol.ToUpperInvariant()),
            new("side", req.Side.ToUpperInvariant()),
            new("type", NormaliseType(req.Type)),
            new("quantity", req.Quantity.ToString("0.########", System.Globalization.CultureInfo.InvariantCulture)),
            new("newClientOrderId", req.ClientOrderId),
        };
        if (RequiresTif(req.Type))
        {
            form.Add(new("timeInForce", req.TimeInForce.ToUpperInvariant()));
        }
        if (req.Price is not null)
        {
            form.Add(new("price", req.Price.Value.ToString("0.########", System.Globalization.CultureInfo.InvariantCulture)));
        }
        if (req.StopPrice is not null)
        {
            form.Add(new("stopPrice", req.StopPrice.Value.ToString("0.########", System.Globalization.CultureInfo.InvariantCulture)));
        }
        return form;
    }

    private static string NormaliseType(string raw) => raw.ToUpperInvariant() switch
    {
        "STOPLOSS" => "STOP_LOSS",
        "STOPLOSSLIMIT" => "STOP_LOSS_LIMIT",
        "TAKEPROFIT" => "TAKE_PROFIT",
        "TAKEPROFITLIMIT" => "TAKE_PROFIT_LIMIT",
        "LIMITMAKER" => "LIMIT_MAKER",
        _ => raw.ToUpperInvariant(),
    };

    private static bool RequiresTif(string type) => type.ToUpperInvariant()
        is "LIMIT" or "STOP_LOSS_LIMIT" or "TAKE_PROFIT_LIMIT" or "STOPLOSSLIMIT" or "TAKEPROFITLIMIT";

    private static (string? Code, string? Message) ParseBinanceError(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return (null, null);
        try
        {
            using var doc = JsonDocument.Parse(body);
            var code = doc.RootElement.TryGetProperty("code", out var c) ? c.GetRawText() : null;
            var msg = doc.RootElement.TryGetProperty("msg", out var m) ? m.GetString() : null;
            return (code, msg);
        }
        catch
        {
            return (null, body);
        }
    }
}
