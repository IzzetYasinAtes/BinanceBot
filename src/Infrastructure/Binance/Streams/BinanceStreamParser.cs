using System.Globalization;
using System.Text.Json;
using BinanceBot.Application.Abstractions.Binance;
using BinanceBot.Domain.MarketData;

namespace BinanceBot.Infrastructure.Binance.Streams;

public static class BinanceStreamParser
{
    public static bool TryParseCombinedEnvelope(
        ReadOnlySpan<byte> rawJson,
        out string streamName,
        out JsonElement data)
    {
        streamName = string.Empty;
        data = default;

        using var doc = JsonDocument.Parse(rawJson.ToArray());
        var root = doc.RootElement;

        if (!root.TryGetProperty("stream", out var streamEl) ||
            !root.TryGetProperty("data", out var dataEl))
        {
            return false;
        }

        streamName = streamEl.GetString() ?? string.Empty;
        data = dataEl.Clone();
        return true;
    }

    public static bool TryParseKline(JsonElement data, DateTimeOffset receivedAt, out WsKlinePayload payload)
    {
        payload = null!;
        if (!data.TryGetProperty("k", out var k)) return false;

        var symbol = data.GetProperty("s").GetString() ?? string.Empty;
        var intervalCode = k.GetProperty("i").GetString() ?? string.Empty;
        var interval = KlineIntervalExtensions.FromBinanceCode(intervalCode);
        var openTime = DateTimeOffset.FromUnixTimeMilliseconds(k.GetProperty("t").GetInt64());
        var closeTime = DateTimeOffset.FromUnixTimeMilliseconds(k.GetProperty("T").GetInt64());
        var open = ParseDecimal(k.GetProperty("o").GetString());
        var high = ParseDecimal(k.GetProperty("h").GetString());
        var low = ParseDecimal(k.GetProperty("l").GetString());
        var close = ParseDecimal(k.GetProperty("c").GetString());
        var volume = ParseDecimal(k.GetProperty("v").GetString());
        var quoteVolume = ParseDecimal(k.GetProperty("q").GetString());
        var tradeCount = k.GetProperty("n").GetInt32();
        var takerBuyBase = ParseDecimal(k.GetProperty("V").GetString());
        var takerBuyQuote = ParseDecimal(k.GetProperty("Q").GetString());
        var isClosed = k.GetProperty("x").GetBoolean();

        payload = new WsKlinePayload(
            symbol, interval, openTime, closeTime,
            open, high, low, close, volume, quoteVolume,
            tradeCount, takerBuyBase, takerBuyQuote, isClosed);
        return true;
    }

    public static bool TryParseBookTicker(JsonElement data, DateTimeOffset receivedAt, out WsBookTickerPayload payload)
    {
        payload = null!;
        if (!data.TryGetProperty("u", out var uEl) || !data.TryGetProperty("s", out var sEl))
        {
            return false;
        }

        var symbol = sEl.GetString() ?? string.Empty;
        var updateId = uEl.GetInt64();
        var bidPrice = ParseDecimal(data.GetProperty("b").GetString());
        var bidQty = ParseDecimal(data.GetProperty("B").GetString());
        var askPrice = ParseDecimal(data.GetProperty("a").GetString());
        var askQty = ParseDecimal(data.GetProperty("A").GetString());

        payload = new WsBookTickerPayload(symbol, updateId, bidPrice, bidQty, askPrice, askQty, receivedAt);
        return true;
    }

    public static bool TryParseDepthDiff(JsonElement data, DateTimeOffset receivedAt, out WsDepthDiffPayload payload)
    {
        payload = null!;
        if (!data.TryGetProperty("U", out var firstEl) ||
            !data.TryGetProperty("u", out var finalEl) ||
            !data.TryGetProperty("s", out var sEl))
        {
            return false;
        }

        var symbol = sEl.GetString() ?? string.Empty;
        var firstUpdateId = firstEl.GetInt64();
        var finalUpdateId = finalEl.GetInt64();
        long? pu = null;
        if (data.TryGetProperty("pu", out var puEl))
        {
            pu = puEl.GetInt64();
        }

        var bids = ParseLevels(data.GetProperty("b"));
        var asks = ParseLevels(data.GetProperty("a"));

        payload = new WsDepthDiffPayload(symbol, firstUpdateId, finalUpdateId, pu, bids, asks, receivedAt);
        return true;
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
