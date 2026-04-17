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
public sealed class KlinePersister : IKlinePersister
{
    private readonly IApplicationDbContext _db;

    public KlinePersister(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task PersistAsync(WsKlinePayload p, CancellationToken ct)
    {
        var symbolVo = Symbol.From(p.Symbol);
        var existing = await ((DbSet<Kline>)_db.Klines)
            .FirstOrDefaultAsync(
                k => k.Symbol == symbolVo && k.Interval == p.Interval && k.OpenTime == p.OpenTime,
                ct);

        if (existing is null)
        {
            var kline = Kline.Ingest(
                symbolVo, p.Interval, p.OpenTime, p.CloseTime,
                p.Open, p.High, p.Low, p.Close,
                p.Volume, p.QuoteVolume, p.TradeCount,
                p.TakerBuyBase, p.TakerBuyQuote, p.IsClosed);
            _db.Klines.Add(kline);
        }
        else
        {
            existing.Upsert(
                p.Open, p.High, p.Low, p.Close,
                p.Volume, p.QuoteVolume, p.TradeCount,
                p.TakerBuyBase, p.TakerBuyQuote, p.IsClosed);
        }

        await _db.SaveChangesAsync(ct);
    }
}
