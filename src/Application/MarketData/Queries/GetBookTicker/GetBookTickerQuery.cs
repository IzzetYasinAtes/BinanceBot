using Ardalis.Result;
using BinanceBot.Application.Abstractions;
using BinanceBot.Application.MarketData.Queries;
using BinanceBot.Domain.ValueObjects;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BinanceBot.Application.MarketData.Queries.GetBookTicker;

public sealed record GetBookTickerQuery(string Symbol) : IRequest<Result<BookTickerDto>>;

public sealed class GetBookTickerQueryValidator : AbstractValidator<GetBookTickerQuery>
{
    public GetBookTickerQueryValidator()
    {
        RuleFor(q => q.Symbol).NotEmpty();
    }
}

public sealed class GetBookTickerQueryHandler
    : IRequestHandler<GetBookTickerQuery, Result<BookTickerDto>>
{
    private readonly IApplicationDbContext _db;

    public GetBookTickerQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<Result<BookTickerDto>> Handle(GetBookTickerQuery request, CancellationToken ct)
    {
        var symbolVo = Symbol.From(request.Symbol);
        var row = await _db.BookTickers
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Symbol == symbolVo, ct);

        if (row is null)
        {
            return Result<BookTickerDto>.NotFound($"BookTicker for {symbolVo} not found.");
        }

        var mid = (row.BidPrice + row.AskPrice) / 2m;
        var spreadBps = mid > 0m
            ? ((row.AskPrice - row.BidPrice) / mid) * 10000m
            : 0m;

        return Result.Success(new BookTickerDto(
            row.Symbol.Value,
            row.BidPrice, row.BidQuantity,
            row.AskPrice, row.AskQuantity,
            mid, spreadBps,
            row.UpdatedAt));
    }
}
