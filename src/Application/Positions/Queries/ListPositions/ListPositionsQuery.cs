using Ardalis.Result;
using BinanceBot.Application.Abstractions;
using BinanceBot.Application.Positions.Queries;
using BinanceBot.Domain.Positions;
using BinanceBot.Domain.ValueObjects;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BinanceBot.Application.Positions.Queries.ListPositions;

public sealed record ListPositionsQuery(
    string? Status,
    string? Symbol,
    DateTimeOffset? From,
    DateTimeOffset? To) : IRequest<Result<IReadOnlyList<PositionDto>>>;

public sealed class ListPositionsQueryValidator : AbstractValidator<ListPositionsQuery>
{
    public ListPositionsQueryValidator()
    {
        RuleFor(q => q.Status)
            .Must(s => s is null
                || s.Equals("open", StringComparison.OrdinalIgnoreCase)
                || s.Equals("closed", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Status must be 'open' or 'closed'.");
        RuleFor(q => q)
            .Must(q => q.From is null || q.To is null || q.To >= q.From);
    }
}

public sealed class ListPositionsQueryHandler
    : IRequestHandler<ListPositionsQuery, Result<IReadOnlyList<PositionDto>>>
{
    private readonly IApplicationDbContext _db;

    public ListPositionsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<Result<IReadOnlyList<PositionDto>>> Handle(
        ListPositionsQuery request, CancellationToken ct)
    {
        var query = _db.Positions.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var status = request.Status.Equals("open", StringComparison.OrdinalIgnoreCase)
                ? PositionStatus.Open
                : PositionStatus.Closed;
            query = query.Where(p => p.Status == status);
        }
        if (!string.IsNullOrWhiteSpace(request.Symbol))
        {
            var symbolVo = Symbol.From(request.Symbol);
            query = query.Where(p => p.Symbol == symbolVo);
        }
        if (request.From is not null)
        {
            query = query.Where(p => p.OpenedAt >= request.From);
        }
        if (request.To is not null)
        {
            query = query.Where(p => p.OpenedAt <= request.To);
        }

        var rows = await query
            .OrderByDescending(p => p.UpdatedAt)
            .Take(500)
            .ToListAsync(ct);

        var items = rows.Select(PositionMapper.ToDto).ToList();
        return Result.Success<IReadOnlyList<PositionDto>>(items);
    }
}
