using BinanceBot.Application.Abstractions;
using BinanceBot.Application.Abstractions.Binance;
using BinanceBot.Domain.MarketData;
using BinanceBot.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace BinanceBot.Infrastructure.Binance.Workers;

/// <summary>
/// Scoped service that upserts a single kline payload (WS or REST-backfill) into
/// the database. Extracted out of <see cref="KlineIngestionWorker"/> so that the
/// REST backfill worker can reuse the exact same persistence path.
/// </summary>
/// <remarks>
/// ADR-0010: when <c>emitDomainEvents</c> is <c>false</c> the aggregate's
/// pending <c>IDomainEvent</c>s are cleared <em>before</em>
/// <see cref="IApplicationDbContext.SaveChangesAsync"/> is called so the
/// context's MediatR publisher does not fan out backfilled bars as live
/// signals. Per-call granularity keeps WS overlap (ADR-0009 §9.6 idempotency)
/// safe: a WS bar arriving for the same key still publishes through the WS
/// path with <c>true</c>.
/// </remarks>
public sealed class KlinePersister : IKlinePersister
{
    private readonly IApplicationDbContext _db;

    public KlinePersister(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task PersistAsync(WsKlinePayload p, CancellationToken ct, bool emitDomainEvents = true)
    {
        var symbolVo = Symbol.From(p.Symbol);
        var existing = await ((DbSet<Kline>)_db.Klines)
            .FirstOrDefaultAsync(
                k => k.Symbol == symbolVo && k.Interval == p.Interval && k.OpenTime == p.OpenTime,
                ct);

        Kline aggregate;
        if (existing is null)
        {
            aggregate = Kline.Ingest(
                symbolVo, p.Interval, p.OpenTime, p.CloseTime,
                p.Open, p.High, p.Low, p.Close,
                p.Volume, p.QuoteVolume, p.TradeCount,
                p.TakerBuyBase, p.TakerBuyQuote, p.IsClosed);
            _db.Klines.Add(aggregate);
        }
        else
        {
            existing.Upsert(
                p.Open, p.High, p.Low, p.Close,
                p.Volume, p.QuoteVolume, p.TradeCount,
                p.TakerBuyBase, p.TakerBuyQuote, p.IsClosed);
            aggregate = existing;
        }

        if (!emitDomainEvents)
        {
            // ADR-0010: suppress publish for backfill — must precede SaveChangesAsync
            // because the DbContext drains DomainEvents inside its override.
            aggregate.ClearDomainEvents();
        }

        await _db.SaveChangesAsync(ct);
    }
}
