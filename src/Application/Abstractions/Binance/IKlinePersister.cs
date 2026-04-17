namespace BinanceBot.Application.Abstractions.Binance;

/// <summary>
/// Persists a single kline payload (originating from either the WS push stream
/// or the REST backfill snapshot) into the database using the (Symbol, Interval,
/// OpenTime) idempotent upsert contract (ADR-0003 §3.1).
/// </summary>
public interface IKlinePersister
{
    Task PersistAsync(WsKlinePayload payload, CancellationToken ct);
}
