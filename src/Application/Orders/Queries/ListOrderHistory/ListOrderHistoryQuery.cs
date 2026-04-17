using Ardalis.Result;
using BinanceBot.Application.Abstractions;
using BinanceBot.Application.Common;
using BinanceBot.Application.Orders.Queries;
using BinanceBot.Domain.ValueObjects;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BinanceBot.Application.Orders.Queries.ListOrderHistory;

public sealed record ListOrderHistoryQuery(
    string? Symbol,
    DateTimeOffset? From,
    DateTimeOffset? To,
    int Skip,
    int Take) : IRequest<Result<PagedList<OrderDto>>>;

public sealed class ListOrderHistoryQueryValidator : AbstractValidator<ListOrderHistoryQuery>
{
    public ListOrderHistoryQueryValidator()
    {
        RuleFor(q => q.Skip).GreaterThanOrEqualTo(0);
        RuleFor(q => q.Take).InclusiveBetween(1, 200);
        RuleFor(q => q)
            .Must(q => q.From is null || q.To is null || q.To >= q.From)
            .WithMessage("To must be >= From");
    }
}

public sealed class ListOrderHistoryQueryHandler
    : IRequestHandler<ListOrderHistoryQuery, Result<PagedList<OrderDto>>>
{
    private readonly IApplicationDbContext _db;

    public ListOrderHistoryQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<Result<PagedList<OrderDto>>> Handle(ListOrderHistoryQuery request, CancellationToken ct)
    {
        var query = _db.Orders.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.Symbol))
        {
            var symbolVo = Symbol.From(request.Symbol);
            query = query.Where(o => o.Symbol == symbolVo);
        }
        if (request.From is not null)
        {
            query = query.Where(o => o.CreatedAt >= request.From);
        }
        if (request.To is not null)
        {
            query = query.Where(o => o.CreatedAt <= request.To);
        }

        var total = await query.LongCountAsync(ct);
        var rows = await query
            .OrderByDescending(o => o.CreatedAt)
            .Skip(request.Skip)
            .Take(request.Take)
            .ToListAsync(ct);

        var items = rows.Select(OrderMapper.ToDto).ToList();
        return Result.Success(new PagedList<OrderDto>(items, total, request.Skip, request.Take));
    }
}
