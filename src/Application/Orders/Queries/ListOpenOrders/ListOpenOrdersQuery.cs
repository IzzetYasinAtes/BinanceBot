using Ardalis.Result;
using BinanceBot.Application.Abstractions;
using BinanceBot.Application.Orders.Queries;
using BinanceBot.Domain.Orders;
using BinanceBot.Domain.ValueObjects;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BinanceBot.Application.Orders.Queries.ListOpenOrders;

public sealed record ListOpenOrdersQuery(string? Symbol) : IRequest<Result<IReadOnlyList<OrderDto>>>;

public sealed class ListOpenOrdersQueryHandler
    : IRequestHandler<ListOpenOrdersQuery, Result<IReadOnlyList<OrderDto>>>
{
    private readonly IApplicationDbContext _db;

    public ListOpenOrdersQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<Result<IReadOnlyList<OrderDto>>> Handle(ListOpenOrdersQuery request, CancellationToken ct)
    {
        var query = _db.Orders
            .AsNoTracking()
            .Where(o => o.Status == OrderStatus.New || o.Status == OrderStatus.PartiallyFilled);

        if (!string.IsNullOrWhiteSpace(request.Symbol))
        {
            var symbolVo = Symbol.From(request.Symbol);
            query = query.Where(o => o.Symbol == symbolVo);
        }

        var rows = await query
            .OrderByDescending(o => o.UpdatedAt)
            .Take(200)
            .ToListAsync(ct);

        var dtos = rows.Select(OrderMapper.ToDto).ToList();
        return Result.Success<IReadOnlyList<OrderDto>>(dtos);
    }
}
