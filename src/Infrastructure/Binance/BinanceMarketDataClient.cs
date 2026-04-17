using System.Globalization;
using System.Text.Json;
using BinanceBot.Application.Abstractions.Binance;
using BinanceBot.Domain.MarketData;
using Microsoft.Extensions.Logging;

namespace BinanceBot.Infrastructure.Binance;

public sealed class BinanceMarketDataClient : IBinanceMarketData
{
    public const string HttpClientName = "binance-rest";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;
    private readonly ILogger<BinanceMarketDataClient> _logger;

    public BinanceMarketDataClient(HttpClient http, ILogger<BinanceMarketDataClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task PingAsync(CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync("/api/v3/ping", cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<BinanceServerTimeDto> GetServerTimeAsync(CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync("/api/v3/time", cancellationToken);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var serverTime = doc.RootElement.GetProperty("serverTime").GetInt64();
        return new BinanceServerTimeDto(serverTime);
    }

    public async Task<IReadOnlyList<ExchangeInfoSymbolDto>> GetExchangeInfoAsync(
        IReadOnlyCollection<string> symbols,
        CancellationToken cancellationToken)
    {
        var url = "/api/v3/exchangeInfo";
        if (symbols.Count > 0)
        {
            var joined = "[" + string.Join(",", symbols.Select(s => $"\"{s.ToUpperInvariant()}\"")) + "]";
            url += "?symbols=" + Uri.EscapeDataString(joined);
        }

        using var response = await _http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var list = new List<ExchangeInfoSymbolDto>();
        foreach (var sym in doc.RootElement.GetProperty("symbols").EnumerateArray())
        {
            var symbol = sym.GetProperty("symbol").GetString()!;
            var baseAsset = sym.GetProperty("baseAsset").GetString()!;
            var quoteAsset = sym.GetProperty("quoteAsset").GetString()!;
            var status = sym.GetProperty("status").GetString()!;

            decimal tickSize = 0, stepSize = 0, minNotional = 0, minQty = 0, maxQty = 0;

            foreach (var filter in sym.GetProperty("filters").EnumerateArray())
            {
                var filterType = filter.GetProperty("filterType").GetString();
                switch (filterType)
                {
                    case "PRICE_FILTER":
                        tickSize = ParseDecimal(filter.GetProperty("tickSize").GetString());
                        break;
                    case "LOT_SIZE":
                        stepSize = ParseDecimal(filter.GetProperty("stepSize").GetString());
                        minQty = ParseDecimal(filter.GetProperty("minQty").GetString());
                        maxQty = ParseDecimal(filter.GetProperty("maxQty").GetString());
                        break;
                    case "MIN_NOTIONAL":
                    case "NOTIONAL":
                        if (filter.TryGetProperty("minNotional", out var mn))
                        {
                            minNotional = ParseDecimal(mn.GetString());
                        }
                        break;
                }
            }

            list.Add(new ExchangeInfoSymbolDto(
                symbol, baseAsset, quoteAsset, status,
                tickSize, stepSize, minNotional, minQty, maxQty));
        }

        _logger.LogInformation("ExchangeInfo fetched for {Count} symbols", list.Count);
        return list;
    }

    public async Task<IReadOnlyList<RestKlineDto>> GetKlinesAsync(
        string symbol,
        KlineInterval interval,
        int limit,
        DateTimeOffset? startTime,
        DateTimeOffset? endTime,
        CancellationToken cancellationToken)
    {
        var url = $"/api/v3/klines?symbol={symbol.ToUpperInvariant()}" +
                  $"&interval={interval.ToBinanceCode()}" +
                  $"&limit={limit}";

        if (startTime is not null)
        {
            url += "&startTime=" + startTime.Value.ToUnixTimeMilliseconds();
        }
        if (endTime is not null)
        {
            url += "&endTime=" + endTime.Value.ToUnixTimeMilliseconds();
        }

        using var response = await _http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var klines = new List<RestKlineDto>();
        foreach (var row in doc.RootElement.EnumerateArray())
        {
            var openTime = DateTimeOffset.FromUnixTimeMilliseconds(row[0].GetInt64());
            var open = ParseDecimal(row[1].GetString());
            var high = ParseDecimal(row[2].GetString());
            var low = ParseDecimal(row[3].GetString());
            var close = ParseDecimal(row[4].GetString());
            var volume = ParseDecimal(row[5].GetString());
            var closeTime = DateTimeOffset.FromUnixTimeMilliseconds(row[6].GetInt64());
            var quoteVolume = ParseDecimal(row[7].GetString());
            var tradeCount = row[8].GetInt32();
            var takerBuyBase = ParseDecimal(row[9].GetString());
            var takerBuyQuote = ParseDecimal(row[10].GetString());

            klines.Add(new RestKlineDto(
                openTime, closeTime, open, high, low, close,
                volume, quoteVolume, tradeCount, takerBuyBase, takerBuyQuote));
        }

        return klines;
    }

    public async Task<OrderBookSnapshotDto> GetOrderBookSnapshotAsync(
        string symbol,
        int limit,
        CancellationToken cancellationToken)
    {
        var url = $"/api/v3/depth?symbol={symbol.ToUpperInvariant()}&limit={limit}";

        using var response = await _http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var lastUpdateId = doc.RootElement.GetProperty("lastUpdateId").GetInt64();
        var bids = ParseLevels(doc.RootElement.GetProperty("bids"));
        var asks = ParseLevels(doc.RootElement.GetProperty("asks"));

        return new OrderBookSnapshotDto(lastUpdateId, bids, asks);
    }

    private static IReadOnlyList<OrderBookLevelDto> ParseLevels(JsonElement array)
    {
        var levels = new List<OrderBookLevelDto>(array.GetArrayLength());
        foreach (var entry in array.EnumerateArray())
        {
            var price = ParseDecimal(entry[0].GetString());
            var qty = ParseDecimal(entry[1].GetString());
            levels.Add(new OrderBookLevelDto(price, qty));
        }
        return levels;
    }

    private static decimal ParseDecimal(string? raw) =>
        decimal.Parse(raw ?? "0", NumberStyles.Any, CultureInfo.InvariantCulture);
}
