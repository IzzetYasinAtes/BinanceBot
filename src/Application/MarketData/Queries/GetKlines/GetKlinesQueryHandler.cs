using Ardalis.Result;
using BinanceBot.Application.Abstractions;
using BinanceBot.Domain.MarketData;
using BinanceBot.Domain.ValueObjects;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BinanceBot.Application.MarketData.Queries.GetKlines;

public sealed class GetKlinesQueryHandler
    : IRequestHandler<GetKlinesQuery, Result<IReadOnlyList<KlineDto>>>
{
    private readonly IApplicationDbContext _db;

    public GetKlinesQueryHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<Result<IReadOnlyList<KlineDto>>> Handle(
        GetKlinesQuery request,
        CancellationToken cancellationToken)
    {
        var interval = KlineIntervalExtensions.FromBinanceCode(request.Interval);
        var symbol = Symbol.From(request.Symbol);

        var query = _db.Klines
            .AsNoTracking()
            .Where(k => k.Symbol == symbol && k.Interval == interval);

        if (request.StartTime is not null)
        {
            query = query.Where(k => k.OpenTime >= request.StartTime.Value);
        }

        if (request.EndTime is not null)
        {
            query = query.Where(k => k.OpenTime <= request.EndTime.Value);
        }

        var projection = await query
            .OrderByDescending(k => k.OpenTime)
            .Take(request.Limit)
            .Select(k => new
            {
                k.Symbol,
                k.OpenTime,
                k.CloseTime,
                k.OpenPrice,
                k.HighPrice,
                k.LowPrice,
                k.ClosePrice,
                k.Volume,
                k.QuoteVolume,
                k.TradeCount,
                k.IsClosed,
            })
            .ToListAsync(cancellationToken);

        var rows = projection
            .Select(k => new KlineDto(
                k.Symbol.Value,
                request.Interval,
                k.OpenTime,
                k.CloseTime,
                k.OpenPrice,
                k.HighPrice,
                k.LowPrice,
                k.ClosePrice,
                k.Volume,
                k.QuoteVolume,
                k.TradeCount,
                k.IsClosed))
            .ToList();

        rows.Reverse();
        return Result.Success<IReadOnlyList<KlineDto>>(rows);
    }
}
