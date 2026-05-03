using System.Globalization;
using System.Text.Json;
using Ardalis.Result;
using BinanceBot.Application.Abstractions.Binance;
using BinanceBot.Application.Abstractions.Exchange;
using BinanceBot.Domain.Common;
using BinanceBot.Infrastructure.Binance.Handlers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BinanceBot.Infrastructure.Binance;

/// <summary>
/// Loop 92 — Binance USDT-M Futures REST client (ADR-0025). Replaces the
/// Spot-only <c>BinanceTradingClient</c>. Endpoints follow binance-expert spec:
/// <list type="bullet">
///   <item>POST /fapi/v1/order — emir aç/iptal — positionSide=BOTH zorunlu (One-way mode).</item>
///   <item>DELETE /fapi/v1/order — emir iptal.</item>
///   <item>POST /fapi/v1/leverage — kaldıraç ayarla (1-125 aralık).</item>
///   <item>POST /fapi/v1/marginType — ISOLATED/CROSSED.</item>
///   <item>POST /fapi/v1/positionSide/dual — One-way mode (false).</item>
///   <item>GET /fapi/v3/balance — wallet + crossUnPnl + availableBalance.</item>
///   <item>GET /fapi/v3/positionRisk — pozisyon listesi (positionAmt: +long/-short/0 flat).</item>
/// </list>
///
/// Stop emirlerinde <c>workingType=MARK_PRICE</c> + <c>priceProtect=true</c> kullanılır
/// (last price manipülasyonuna karşı). HMAC-SHA256 signing
/// <see cref="SignedRequestHandler"/> aracılığıyla yapılır (Spot ile özdeş).
/// </summary>
public sealed class BinanceFuturesClient : IExchangeClient
{
    public const string HttpClientName = "binance-futures";

    private readonly HttpClient _http;
    private readonly IOptionsMonitor<BinanceOptions> _options;
    private readonly ILogger<BinanceFuturesClient> _logger;

    public BinanceFuturesClient(
        HttpClient http,
        IOptionsMonitor<BinanceOptions> options,
        ILogger<BinanceFuturesClient> logger)
    {
        _http = http;
        _options = options;
        _logger = logger;
    }

    private bool HasApiCredentials =>
        !string.IsNullOrWhiteSpace(_options.CurrentValue.ApiKey)
        && !string.IsNullOrWhiteSpace(_options.CurrentValue.ApiSecret);

    private bool MainnetGuardTripped()
    {
        var opts = _options.CurrentValue;
        if (!opts.AllowMainnet
            && opts.RestBaseUrl.Contains("fapi.binance.com", StringComparison.OrdinalIgnoreCase)
            && !opts.RestBaseUrl.Contains("demo-fapi", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogCritical("Mainnet futures blocked by AllowMainnet=false (ADR-0006)");
            return true;
        }
        return false;
    }

    public async Task<Result<TestOrderResponse>> PlaceTestOrderAsync(
        PlaceOrderRequest request, CancellationToken cancellationToken)
    {
        if (!HasApiCredentials)
        {
            return Result.Success(new TestOrderResponse(true, null, "simulated_no_credentials"));
        }

        if (MainnetGuardTripped())
        {
            return Result.Success(new TestOrderResponse(false, "MAINNET_BLOCKED", "AllowMainnet=false guard"));
        }

        // Futures testnet '/fapi/v1/order/test' endpoint'i yoktur. Real endpoint kullanılır.
        // Test placeholder: simulated accept dön (testnet'te zaten gerçek emir atılıyor).
        _logger.LogInformation("Futures test order (no /test endpoint, simulated accept) {Cid} {Symbol}",
            request.ClientOrderId, request.Symbol);
        return Result.Success(new TestOrderResponse(true, null, "simulated_futures_test"));
    }

    public async Task<Result<LiveOrderResponse>> PlaceLiveOrderAsync(
        PlaceOrderRequest request, CancellationToken cancellationToken)
    {
        if (MainnetGuardTripped())
        {
            return Result.Success(new LiveOrderResponse(false, null, "Blocked", 0m, 0m,
                Array.Empty<LiveFillDto>(), "MAINNET_BLOCKED", "AllowMainnet=false guard"));
        }

        if (!HasApiCredentials)
        {
            return Result.Success(new LiveOrderResponse(false, null, "Rejected", 0m, 0m,
                Array.Empty<LiveFillDto>(), "NO_CREDENTIALS", "API key/secret missing"));
        }

        var form = BuildOrderForm(request);
        using var msg = new HttpRequestMessage(HttpMethod.Post, "/fapi/v1/order")
        {
            Content = new FormUrlEncodedContent(form),
        };
        msg.Headers.Add(SignedRequestHandler.SignMarker, "true");

        using var response = await _http.SendAsync(msg, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("FuturesOrder rejected {Status} {Body}", response.StatusCode, body);
            var (code, message) = ParseBinanceError(body);
            return Result.Success(new LiveOrderResponse(false, null, "Rejected", 0m, 0m,
                Array.Empty<LiveFillDto>(), code, message));
        }

        return Result.Success(ParseLiveOrder(body));
    }

    public async Task<Result<CancelOrderResponse>> CancelLiveOrderAsync(
        string symbol, string clientOrderId, CancellationToken cancellationToken)
    {
        if (MainnetGuardTripped())
        {
            return Result.Success(new CancelOrderResponse(false, "MAINNET_BLOCKED", "AllowMainnet=false guard"));
        }

        if (!HasApiCredentials)
        {
            return Result.Success(new CancelOrderResponse(false, "NO_CREDENTIALS", "API key/secret missing"));
        }

        var query = $"symbol={symbol.ToUpperInvariant()}&origClientOrderId={Uri.EscapeDataString(clientOrderId)}";
        using var msg = new HttpRequestMessage(HttpMethod.Delete, "/fapi/v1/order?" + query);
        msg.Headers.Add(SignedRequestHandler.SignMarker, "true");

        using var response = await _http.SendAsync(msg, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return Result.Success(new CancelOrderResponse(true, null, null));
        }

        var (code, message) = ParseBinanceError(body);
        return Result.Success(new CancelOrderResponse(false, code, message));
    }

    public Task<Result<CancelOrderResponse>> CancelTestOrderAsync(
        string symbol, string clientOrderId, CancellationToken cancellationToken)
    {
        // No /test endpoint on Futures; same path as live.
        return CancelLiveOrderAsync(symbol, clientOrderId, cancellationToken);
    }

    public async Task<Result> SetLeverageAsync(
        string symbol, int leverage, CancellationToken cancellationToken)
    {
        if (MainnetGuardTripped()) return Result.Error("MAINNET_BLOCKED");
        if (!HasApiCredentials) return Result.Error("NO_CREDENTIALS");
        if (leverage < 1 || leverage > 125)
        {
            return Result.Error($"Invalid leverage {leverage} (1-125 allowed)");
        }

        var form = new[]
        {
            new KeyValuePair<string, string>("symbol", symbol.ToUpperInvariant()),
            new KeyValuePair<string, string>("leverage", leverage.ToString(CultureInfo.InvariantCulture)),
        };
        using var msg = new HttpRequestMessage(HttpMethod.Post, "/fapi/v1/leverage")
        {
            Content = new FormUrlEncodedContent(form),
        };
        msg.Headers.Add(SignedRequestHandler.SignMarker, "true");

        using var response = await _http.SendAsync(msg, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            _logger.LogInformation("Futures leverage set symbol={Symbol} leverage={Leverage}", symbol, leverage);
            return Result.Success();
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var (code, message) = ParseBinanceError(body);
        _logger.LogWarning("SetLeverage failed {Code} {Message}", code, message);
        return Result.Error($"{code}: {message}");
    }

    public async Task<Result> SetMarginTypeAsync(
        string symbol, MarginType marginType, CancellationToken cancellationToken)
    {
        if (MainnetGuardTripped()) return Result.Error("MAINNET_BLOCKED");
        if (!HasApiCredentials) return Result.Error("NO_CREDENTIALS");

        var form = new[]
        {
            new KeyValuePair<string, string>("symbol", symbol.ToUpperInvariant()),
            new KeyValuePair<string, string>("marginType", marginType == MarginType.Isolated ? "ISOLATED" : "CROSSED"),
        };
        using var msg = new HttpRequestMessage(HttpMethod.Post, "/fapi/v1/marginType")
        {
            Content = new FormUrlEncodedContent(form),
        };
        msg.Headers.Add(SignedRequestHandler.SignMarker, "true");

        using var response = await _http.SendAsync(msg, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return Result.Success();
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var (code, message) = ParseBinanceError(body);
        // Binance "-4046 No need to change margin type" idempotency hatası — sessizce başarılı say.
        if (code == "-4046")
        {
            _logger.LogDebug("MarginType already set symbol={Symbol} (-4046 idempotent)", symbol);
            return Result.Success();
        }
        _logger.LogWarning("SetMarginType failed {Code} {Message}", code, message);
        return Result.Error($"{code}: {message}");
    }

    public async Task<Result> SetPositionModeAsync(
        bool dualSidePosition, CancellationToken cancellationToken)
    {
        if (MainnetGuardTripped()) return Result.Error("MAINNET_BLOCKED");
        if (!HasApiCredentials) return Result.Error("NO_CREDENTIALS");

        var form = new[]
        {
            new KeyValuePair<string, string>("dualSidePosition", dualSidePosition ? "true" : "false"),
        };
        using var msg = new HttpRequestMessage(HttpMethod.Post, "/fapi/v1/positionSide/dual")
        {
            Content = new FormUrlEncodedContent(form),
        };
        msg.Headers.Add(SignedRequestHandler.SignMarker, "true");

        using var response = await _http.SendAsync(msg, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return Result.Success();
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var (code, message) = ParseBinanceError(body);
        if (code == "-4059")
        {
            // "No need to change position side" — already in requested mode.
            return Result.Success();
        }
        _logger.LogWarning("SetPositionMode failed {Code} {Message}", code, message);
        return Result.Error($"{code}: {message}");
    }

    public async Task<Result<ExchangeAccountInfo>> GetAccountInfoAsync(CancellationToken cancellationToken)
    {
        if (MainnetGuardTripped())
        {
            return Result<ExchangeAccountInfo>.Error("MAINNET_BLOCKED");
        }
        if (!HasApiCredentials)
        {
            return Result<ExchangeAccountInfo>.Error("NO_CREDENTIALS");
        }

        using var msg = new HttpRequestMessage(HttpMethod.Get, "/fapi/v3/balance");
        msg.Headers.Add(SignedRequestHandler.SignMarker, "true");

        using var response = await _http.SendAsync(msg, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return Result<ExchangeAccountInfo>.Error(body);
        }

        using var doc = JsonDocument.Parse(body);
        var usdt = doc.RootElement
            .EnumerateArray()
            .FirstOrDefault(e => e.TryGetProperty("asset", out var a) && a.GetString() == "USDT");

        if (usdt.ValueKind == JsonValueKind.Undefined)
        {
            return Result.Success(new ExchangeAccountInfo(0m, 0m, 0m));
        }

        var wallet = ParseDecimal(usdt.GetProperty("balance").GetString());
        var available = ParseDecimal(usdt.GetProperty("availableBalance").GetString());
        var crossUnPnl = usdt.TryGetProperty("crossUnPnl", out var cup) ? ParseDecimal(cup.GetString()) : 0m;
        return Result.Success(new ExchangeAccountInfo(wallet, available, crossUnPnl));
    }

    public async Task<Result<IReadOnlyList<ExchangePositionDto>>> GetOpenPositionsAsync(CancellationToken cancellationToken)
    {
        if (MainnetGuardTripped())
        {
            return Result<IReadOnlyList<ExchangePositionDto>>.Error("MAINNET_BLOCKED");
        }
        if (!HasApiCredentials)
        {
            return Result<IReadOnlyList<ExchangePositionDto>>.Error("NO_CREDENTIALS");
        }

        using var msg = new HttpRequestMessage(HttpMethod.Get, "/fapi/v3/positionRisk");
        msg.Headers.Add(SignedRequestHandler.SignMarker, "true");

        using var response = await _http.SendAsync(msg, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return Result<IReadOnlyList<ExchangePositionDto>>.Error(body);
        }

        using var doc = JsonDocument.Parse(body);
        var list = new List<ExchangePositionDto>();
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            var posAmt = ParseDecimal(e.GetProperty("positionAmt").GetString());
            if (posAmt == 0m) continue;  // flat — skip

            var direction = posAmt > 0m ? TradeDirection.Long : TradeDirection.Short;
            list.Add(new ExchangePositionDto(
                Symbol: e.GetProperty("symbol").GetString() ?? "",
                Direction: direction,
                Quantity: Math.Abs(posAmt),
                EntryPrice: ParseDecimal(e.GetProperty("entryPrice").GetString()),
                MarkPrice: ParseDecimal(e.GetProperty("markPrice").GetString()),
                UnrealizedPnl: ParseDecimal(e.GetProperty("unRealizedProfit").GetString()),
                LiquidationPrice: e.TryGetProperty("liquidationPrice", out var lp) ? ParseDecimal(lp.GetString()) : 0m,
                IsolatedMargin: e.TryGetProperty("isolatedMargin", out var im) ? ParseDecimal(im.GetString()) : 0m));
        }
        return Result.Success((IReadOnlyList<ExchangePositionDto>)list);
    }

    private static List<KeyValuePair<string, string>> BuildOrderForm(PlaceOrderRequest req)
    {
        var typeNorm = NormaliseType(req.Type);
        var form = new List<KeyValuePair<string, string>>
        {
            new("symbol", req.Symbol.ToUpperInvariant()),
            new("side", req.Side.ToUpperInvariant()),
            // One-way mode: positionSide=BOTH zorunlu (binance-expert spec §5).
            new("positionSide", "BOTH"),
            new("type", typeNorm),
            new("newClientOrderId", req.ClientOrderId),
        };

        // Quantity: closePosition=true ile birlikte gönderilemez. Stop-only emirler
        // closePosition kullanır; entry/exit emirler quantity gönderir.
        var isStopOnly = typeNorm is "STOP_MARKET" or "TAKE_PROFIT_MARKET";
        if (!isStopOnly)
        {
            form.Add(new("quantity", req.Quantity.ToString("0.########", CultureInfo.InvariantCulture)));
        }

        if (RequiresTif(typeNorm))
        {
            form.Add(new("timeInForce", req.TimeInForce.ToUpperInvariant()));
        }
        if (req.Price is not null)
        {
            form.Add(new("price", req.Price.Value.ToString("0.########", CultureInfo.InvariantCulture)));
        }
        if (req.StopPrice is not null)
        {
            form.Add(new("stopPrice", req.StopPrice.Value.ToString("0.########", CultureInfo.InvariantCulture)));
            // Stop emirleri için workingType=MARK_PRICE + priceProtect=true (binance-expert §10).
            form.Add(new("workingType", "MARK_PRICE"));
            form.Add(new("priceProtect", "true"));
            if (isStopOnly)
            {
                form.Add(new("closePosition", "true"));
            }
        }
        return form;
    }

    private static string NormaliseType(string raw) => raw.ToUpperInvariant() switch
    {
        "STOPLOSS" or "STOP_LOSS" or "STOP" => "STOP",
        "STOPLOSSLIMIT" or "STOP_LOSS_LIMIT" => "STOP",
        "STOPMARKET" or "STOP_MARKET" => "STOP_MARKET",
        "TAKEPROFIT" or "TAKE_PROFIT" => "TAKE_PROFIT",
        "TAKEPROFITLIMIT" or "TAKE_PROFIT_LIMIT" => "TAKE_PROFIT",
        "TAKEPROFITMARKET" or "TAKE_PROFIT_MARKET" => "TAKE_PROFIT_MARKET",
        "LIMITMAKER" or "LIMIT_MAKER" => "LIMIT",
        _ => raw.ToUpperInvariant(),
    };

    private static bool RequiresTif(string type) => type
        is "LIMIT" or "STOP" or "TAKE_PROFIT";

    private static LiveOrderResponse ParseLiveOrder(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        long? orderId = root.TryGetProperty("orderId", out var oid) ? oid.GetInt64() : null;
        var status = root.TryGetProperty("status", out var st) ? st.GetString() ?? "NEW" : "NEW";
        var executed = root.TryGetProperty("executedQty", out var eq) ? ParseDecimal(eq.GetString()) : 0m;
        var cummulative = root.TryGetProperty("cumQuote", out var cq) ? ParseDecimal(cq.GetString()) : 0m;

        // Futures POST /fapi/v1/order yanıtında fills array her zaman gelmez (NEW emir).
        // Fill akışı User Data Stream ORDER_TRADE_UPDATE olayları üzerinden gelir.
        IReadOnlyList<LiveFillDto> fills = Array.Empty<LiveFillDto>();
        if (root.TryGetProperty("fills", out var fillsArr) && fillsArr.ValueKind == JsonValueKind.Array)
        {
            var list = new List<LiveFillDto>();
            foreach (var f in fillsArr.EnumerateArray())
            {
                list.Add(new LiveFillDto(
                    f.TryGetProperty("tradeId", out var tid) ? tid.GetInt64() : 0L,
                    ParseDecimal(f.GetProperty("price").GetString()),
                    ParseDecimal(f.GetProperty("qty").GetString()),
                    ParseDecimal(f.GetProperty("commission").GetString()),
                    f.TryGetProperty("commissionAsset", out var ca) ? ca.GetString() ?? "USDT" : "USDT"));
            }
            fills = list;
        }

        return new LiveOrderResponse(true, orderId, status, executed, cummulative, fills, null, null);
    }

    private static decimal ParseDecimal(string? raw) =>
        decimal.Parse(raw ?? "0", NumberStyles.Any, CultureInfo.InvariantCulture);

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
