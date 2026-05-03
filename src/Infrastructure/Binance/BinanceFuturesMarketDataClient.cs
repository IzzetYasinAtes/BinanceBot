using System.Globalization;
using System.Text.Json;
using BinanceBot.Application.Abstractions.Binance;
using BinanceBot.Domain.MarketData;
using Microsoft.Extensions.Logging;

namespace BinanceBot.Infrastructure.Binance;

/// <summary>
/// Loop 92 — Binance USDT-M Futures market data client (ADR-0025). Replaces the
/// Spot-only <c>BinanceMarketDataClient</c>. Endpoint paths:
/// <list type="bullet">
///   <item>GET /fapi/v1/exchangeInfo — sembol filtreleri (PRICE_FILTER, LOT_SIZE, MIN_NOTIONAL).</item>
///   <item>GET /fapi/v1/klines — kline backfill.</item>
///   <item>GET /fapi/v1/depth — order book snapshot.</item>
///   <item>GET /fapi/v1/time — server zaman senkronizasyonu.</item>
///   <item>GET /fapi/v1/ping — heartbeat.</item>
///   <item>GET /fapi/v1/ticker/24hr — 24 saatlik ticker (volume + change).</item>
/// </list>
///
/// Public market data endpoint'lerinin signing'e ihtiyacı yok; ticker24h batch
/// formatı Spot'la özdeş (?symbols=["BTCUSDT","ETHUSDT"] JSON array).
/// </summary>
public sealed class BinanceFuturesMarketDataClient : IBinanceMarketData
{
    public const string HttpClientName = "binance-futures-market";

    private readonly HttpClient _http;
    private readonly ILogger<BinanceFuturesMarketDataClient> _logger;

    public BinanceFuturesMarketDataClient(
        HttpClient http,
        ILogger<BinanceFuturesMarketDataClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ExchangeInfoSymbolDto>> GetExchangeInfoAsync(
        IReadOnlyCollection<string> symbols, CancellationToken cancellationToken)
    {
        using var resp = await _http.GetAsync("/fapi/v1/exchangeInfo", cancellationToken);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadAsStringAsync(cancellationToken);

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var list = new List<ExchangeInfoSymbolDto>();
        var requested = symbols.Select(s => s.ToUpperInvariant()).ToHashSet();

        foreach (var s in root.GetProperty("symbols").EnumerateArray())
        {
            var sym = s.GetProperty("symbol").GetString() ?? "";
            if (requested.Count > 0 && !requested.Contains(sym)) continue;

            decimal tickSize = 0m, stepSize = 0m, minNotional = 0m, minQty = 0m, maxQty = 0m;
            foreach (var f in s.GetProperty("filters").EnumerateArray())
            {
                var ft = f.GetProperty("filterType").GetString();
                switch (ft)
                {
                    case "PRICE_FILTER":
                        tickSize = ParseDecimal(f.GetProperty("tickSize").GetString());
                        break;
                    case "LOT_SIZE":
                        stepSize = ParseDecimal(f.GetProperty("stepSize").GetString());
                        minQty = ParseDecimal(f.GetProperty("minQty").GetString());
                        maxQty = ParseDecimal(f.GetProperty("maxQty").GetString());
                        break;
                    case "MIN_NOTIONAL":
                        if (f.TryGetProperty("notional", out var n)) minNotional = ParseDecimal(n.GetString());
                        break;
                }
            }
            list.Add(new ExchangeInfoSymbolDto(
                sym,
                s.GetProperty("baseAsset").GetString() ?? "",
                s.GetProperty("quoteAsset").GetString() ?? "",
                s.GetProperty("status").GetString() ?? "TRADING",
                tickSize, stepSize, minNotional, minQty, maxQty));
        }
        return list;
    }

    public async Task<IReadOnlyList<RestKlineDto>> GetKlinesAsync(
        string symbol, KlineInterval interval, int limit,
        DateTimeOffset? startTime, DateTimeOffset? endTime, CancellationToken cancellationToken)
    {
        var qs = $"symbol={symbol.ToUpperInvariant()}&interval={interval.ToBinanceCode()}&limit={limit}";
        if (startTime is DateTimeOffset st) qs += $"&startTime={st.ToUnixTimeMilliseconds()}";
        if (endTime is DateTimeOffset et) qs += $"&endTime={et.ToUnixTimeMilliseconds()}";

        using var resp = await _http.GetAsync("/fapi/v1/klines?" + qs, cancellationToken);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadAsStringAsync(cancellationToken);

        using var doc = JsonDocument.Parse(body);
        var list = new List<RestKlineDto>();
        foreach (var k in doc.RootElement.EnumerateArray())
        {
            list.Add(new RestKlineDto(
                DateTimeOffset.FromUnixTimeMilliseconds(k[0].GetInt64()),
                DateTimeOffset.FromUnixTimeMilliseconds(k[6].GetInt64()),
                ParseDecimal(k[1].GetString()),
                ParseDecimal(k[2].GetString()),
                ParseDecimal(k[3].GetString()),
                ParseDecimal(k[4].GetString()),
                ParseDecimal(k[5].GetString()),
                ParseDecimal(k[7].GetString()),
                k[8].GetInt32(),
                ParseDecimal(k[9].GetString()),
                ParseDecimal(k[10].GetString())));
        }
        return list;
    }

    public async Task<OrderBookSnapshotDto> GetOrderBookSnapshotAsync(
        string symbol, int limit, CancellationToken cancellationToken)
    {
        using var resp = await _http.GetAsync(
            $"/fapi/v1/depth?symbol={symbol.ToUpperInvariant()}&limit={limit}",
            cancellationToken);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadAsStringAsync(cancellationToken);

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var lastUpdate = root.GetProperty("lastUpdateId").GetInt64();
        var bids = ParseLevels(root.GetProperty("bids"));
        var asks = ParseLevels(root.GetProperty("asks"));
        return new OrderBookSnapshotDto(lastUpdate, bids, asks);
    }

    public async Task<BinanceServerTimeDto> GetServerTimeAsync(CancellationToken cancellationToken)
    {
        using var resp = await _http.GetAsync("/fapi/v1/time", cancellationToken);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(body);
        return new BinanceServerTimeDto(doc.RootElement.GetProperty("serverTime").GetInt64());
    }

    public async Task PingAsync(CancellationToken cancellationToken)
    {
        using var resp = await _http.GetAsync("/fapi/v1/ping", cancellationToken);
        resp.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<Ticker24hDto>> GetTicker24hAsync(
        IReadOnlyCollection<string> symbols, CancellationToken cancellationToken)
    {
        // Futures /fapi/v1/ticker/24hr accepts ?symbol=X (single) ya da ?symbols=["A","B"] JSON array.
        // Ardışıl symbol parametresi kabul etmiyor — JSON array kullan.
        var sysn = symbols.Select(s => s.ToUpperInvariant()).ToArray();
        var query = sysn.Length switch
        {
            0 => "",
            1 => $"?symbol={sysn[0]}",
            _ => "?symbols=" + Uri.EscapeDataString("[" + string.Join(",", sysn.Select(s => $"\"{s}\"")) + "]"),
        };

        using var resp = await _http.GetAsync("/fapi/v1/ticker/24hr" + query, cancellationToken);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadAsStringAsync(cancellationToken);

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var list = new List<Ticker24hDto>();
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var t in root.EnumerateArray())
            {
                list.Add(ParseTicker(t));
            }
        }
        else
        {
            list.Add(ParseTicker(root));
        }
        return list;
    }

    private static Ticker24hDto ParseTicker(JsonElement t)
    {
        var closeMs = t.TryGetProperty("closeTime", out var ct) ? ct.GetInt64() : 0L;
        return new Ticker24hDto(
            Symbol: t.GetProperty("symbol").GetString() ?? "",
            LastPrice: ParseDecimal(t.GetProperty("lastPrice").GetString()),
            PriceChangePct: ParseDecimal(t.GetProperty("priceChangePercent").GetString()),
            HighPrice: ParseDecimal(t.GetProperty("highPrice").GetString()),
            LowPrice: ParseDecimal(t.GetProperty("lowPrice").GetString()),
            QuoteVolume: ParseDecimal(t.GetProperty("quoteVolume").GetString()),
            CloseTime: closeMs > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(closeMs) : DateTimeOffset.UtcNow);
    }

    private static IReadOnlyList<OrderBookLevelDto> ParseLevels(JsonElement arr)
    {
        var list = new List<OrderBookLevelDto>();
        foreach (var lvl in arr.EnumerateArray())
        {
            list.Add(new OrderBookLevelDto(
                ParseDecimal(lvl[0].GetString()),
                ParseDecimal(lvl[1].GetString())));
        }
        return list;
    }

    private static decimal ParseDecimal(string? raw) =>
        decimal.Parse(raw ?? "0", NumberStyles.Any, CultureInfo.InvariantCulture);
}
