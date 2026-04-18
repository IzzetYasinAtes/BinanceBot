namespace BinanceBot.Application.Abstractions.Binance;

/// <summary>
/// Persists a single kline payload (originating from either the WS push stream
/// or the REST backfill snapshot) into the database using the (Symbol, Interval,
/// OpenTime) idempotent upsert contract (ADR-0003 §3.1).
/// </summary>
/// <remarks>
/// The <paramref name="emitDomainEvents"/> flag (ADR-0010) controls whether the
/// aggregate's accumulated <c>IDomainEvent</c>s are published after
/// <c>SaveChangesAsync</c>. The WS push path keeps the default <c>true</c> so
/// downstream signal handlers fire for live bars; the REST backfill worker
/// passes <c>false</c> to avoid replaying historical bars as if they were live
/// (ADR-0010 §10.4 — backfill must not emit signals).
/// </remarks>
public interface IKlinePersister
{
    Task PersistAsync(WsKlinePayload payload, CancellationToken ct, bool emitDomainEvents = true);
}
