using Ardalis.Result;
using BinanceBot.Application.Abstractions;
using BinanceBot.Application.MarketData.Queries;
using BinanceBot.Domain.MarketData;
using BinanceBot.Domain.ValueObjects;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BinanceBot.Application.MarketData.Queries.GetMarketSummary;

public sealed record GetMarketSummaryQuery(IReadOnlyList<string> Symbols)
    : IRequest<Result<IReadOnlyList<MarketSummaryDto>>>;

public sealed class GetMarketSummaryQueryValidator : AbstractValidator<GetMarketSummaryQuery>
{
    public GetMarketSummaryQueryValidator()
    {
        RuleFor(q => q.Symbols).NotEmpty().Must(s => s.Count <= 10);
    }
}

public sealed class GetMarketSummaryQueryHandler
    : IRequestHandler<GetMarketSummaryQuery, Result<IReadOnlyList<MarketSummaryDto>>>
{
    private readonly IApplicationDbContext _db;

    public GetMarketSummaryQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<Result<IReadOnlyList<MarketSummaryDto>>> Handle(
        GetMarketSummaryQuery request, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var day = now.AddHours(-24);
        var results = new List<MarketSummaryDto>(request.Symbols.Count);

        foreach (var raw in request.Symbols)
        {
            var symbol = Symbol.From(raw);

            var latestClose = await _db.Klines
                .AsNoTracking()
                .Where(k => k.Symbol == symbol && k.Interval == KlineInterval.OneMinute)
                .OrderByDescending(k => k.OpenTime)
                .Select(k => new { k.ClosePrice, k.OpenTime })
                .FirstOrDefaultAsync(ct);

            if (latestClose is null)
            {
                continue;
            }

            var dayAgoClose = await _db.Klines
                .AsNoTracking()
                .Where(k => k.Symbol == symbol
                         && k.Interval == KlineInterval.OneMinute
                         && k.OpenTime <= day)
                .OrderByDescending(k => k.OpenTime)
                .Select(k => (decimal?)k.ClosePrice)
                .FirstOrDefaultAsync(ct);

            var changePct = dayAgoClose is > 0m
                ? ((latestClose.ClosePrice - dayAgoClose.Value) / dayAgoClose.Value) * 100m
                : 0m;

            var volume24h = await _db.Klines
                .AsNoTracking()
                .Where(k => k.Symbol == symbol
                         && k.Interval == KlineInterval.OneMinute
                         && k.OpenTime >= day)
                .SumAsync(k => (decimal?)k.QuoteVolume, ct) ?? 0m;

            var bookTicker = await _db.BookTickers
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.Symbol == symbol, ct);

            var markPrice = bookTicker is not null
                ? (bookTicker.BidPrice + bookTicker.AskPrice) / 2m
                : latestClose.ClosePrice;

            results.Add(new MarketSummaryDto(
                symbol.Value,
                latestClose.ClosePrice,
                markPrice,
                changePct,
                volume24h,
                now));
        }

        return Result.Success<IReadOnlyList<MarketSummaryDto>>(results);
    }
}
