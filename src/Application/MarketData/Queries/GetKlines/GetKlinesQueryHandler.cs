using Ardalis.Result;
using BinanceBot.Application.Abstractions;
using BinanceBot.Application.Abstractions.Binance;
using BinanceBot.Domain.MarketData;
using BinanceBot.Domain.ValueObjects;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BinanceBot.Application.MarketData.Queries.GetKlines;

public sealed class GetKlinesQueryHandler
    : IRequestHandler<GetKlinesQuery, Result<IReadOnlyList<KlineDto>>>
{
    private readonly IApplicationDbContext _db;
    private readonly IBinanceMarketData _market;
    private readonly ILogger<GetKlinesQueryHandler> _logger;

    public GetKlinesQueryHandler(
        IApplicationDbContext db,
        IBinanceMarketData market,
        ILogger<GetKlinesQueryHandler> logger)
    {
        _db = db;
        _market = market;
        _logger = logger;
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

        if (projection.Count > 0)
        {
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

        try
        {
            var restRows = await _market.GetKlinesAsync(
                symbol.Value, interval, Math.Min(request.Limit, 1000),
                request.StartTime, request.EndTime, cancellationToken);

            _logger.LogInformation(
                "Kline REST fallback for {Symbol} {Interval}: returned {Count} bars",
                symbol, request.Interval, restRows.Count);

            var fallback = restRows
                .Select(r => new KlineDto(
                    symbol.Value,
                    request.Interval,
                    r.OpenTime, r.CloseTime,
                    r.Open, r.High, r.Low, r.Close,
                    r.Volume, r.QuoteVolume, r.TradeCount,
                    r.CloseTime <= DateTimeOffset.UtcNow))
                .ToList();
            return Result.Success<IReadOnlyList<KlineDto>>(fallback);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Kline REST fallback failed for {Symbol} {Interval}",
                symbol, request.Interval);
            return Result.Success<IReadOnlyList<KlineDto>>(Array.Empty<KlineDto>());
        }
    }
}
