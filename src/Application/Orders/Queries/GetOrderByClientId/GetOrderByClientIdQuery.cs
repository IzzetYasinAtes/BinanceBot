using Ardalis.Result;
using BinanceBot.Application.Abstractions;
using BinanceBot.Application.Orders.Queries;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BinanceBot.Application.Orders.Queries.GetOrderByClientId;

public sealed record GetOrderByClientIdQuery(string ClientOrderId) : IRequest<Result<OrderDto>>;

public sealed class GetOrderByClientIdQueryValidator : AbstractValidator<GetOrderByClientIdQuery>
{
    public GetOrderByClientIdQueryValidator()
    {
        RuleFor(q => q.ClientOrderId).NotEmpty().MaximumLength(36);
    }
}

public sealed class GetOrderByClientIdQueryHandler
    : IRequestHandler<GetOrderByClientIdQuery, Result<OrderDto>>
{
    private readonly IApplicationDbContext _db;

    public GetOrderByClientIdQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<Result<OrderDto>> Handle(GetOrderByClientIdQuery request, CancellationToken ct)
    {
        var order = await _db.Orders
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.ClientOrderId == request.ClientOrderId, ct);

        if (order is null)
        {
            return Result<OrderDto>.NotFound($"Order '{request.ClientOrderId}' not found.");
        }

        return Result.Success(OrderMapper.ToDto(order));
    }
}
