using System.Collections.Concurrent;
using BinanceBot.Application.Abstractions;
using BinanceBot.Application.Abstractions.Binance;

namespace BinanceBot.Infrastructure.Strategies;

/// <summary>
/// Loop 67 KMS — thread-safe in-memory cache holding the latest
/// <see cref="WsBookTickerPayload"/> per symbol. Singleton lifetime; populated
/// by <see cref="BookTickerCacheWorker"/> via <c>IBinanceMarketStream</c>
/// fan-out, consumed by KMS evaluator for spread (Ask-Bid)/Ask filtering.
///
/// Storage: <see cref="ConcurrentDictionary{TKey,TValue}"/> with upper-case
/// symbol key. Last-writer-wins update semantics (BookTicker streams are
/// idempotent at the symbol level — UpdateId monotone but cache only stores
/// "latest", no history).
/// </summary>
public sealed class BookTickerCache : IBookTickerReader
{
    private readonly ConcurrentDictionary<string, WsBookTickerPayload> _latest =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Worker-side write path. Caller is the singleton background consumer; no
    /// allocation in the hot path beyond the dictionary update itself.
    /// </summary>
    public void Apply(WsBookTickerPayload payload)
    {
        if (payload is null) return;
        if (string.IsNullOrEmpty(payload.Symbol)) return;
        _latest[payload.Symbol.ToUpperInvariant()] = payload;
    }

    public WsBookTickerPayload? GetLatest(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol)) return null;
        return _latest.TryGetValue(symbol.ToUpperInvariant(), out var p) ? p : null;
    }
}
