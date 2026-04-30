using BinanceBot.Application.Abstractions.Binance;

namespace BinanceBot.Application.Abstractions;

/// <summary>
/// Loop 67 KMS — read-only abstraction over the in-memory latest BookTicker
/// snapshot per symbol. Evaluator path consumes spread (Ask-Bid)/Ask in real
/// time; <see cref="WsBookTickerPayload"/> is fan-out from
/// <see cref="Binance.IBinanceMarketStream.SubscribeBookTickers"/>.
///
/// Returns <c>null</c> when the symbol has no payload yet (fresh boot, before
/// the first WS message landed). Evaluator must treat null as "no data — skip
/// signal" (defensive, never throw).
///
/// Concurrency: implementation MUST be safe for many concurrent readers
/// (singleton ConcurrentDictionary backing). The reader path is hot (one
/// lookup per evaluator call) and must not allocate or lock for read.
/// </summary>
public interface IBookTickerReader
{
    /// <summary>
    /// Returns the most recently received BookTicker for <paramref name="symbol"/>,
    /// or <c>null</c> when no payload has arrived yet. Symbol comparison is
    /// case-insensitive (caller may pass either casing).
    /// </summary>
    WsBookTickerPayload? GetLatest(string symbol);
}
