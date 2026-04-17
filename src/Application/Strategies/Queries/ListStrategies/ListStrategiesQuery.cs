using Ardalis.Result;
using BinanceBot.Application.Abstractions;
using BinanceBot.Application.Strategies.Queries;
using BinanceBot.Domain.Strategies;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BinanceBot.Application.Strategies.Queries.ListStrategies;

public sealed record ListStrategiesQuery(string? Status) : IRequest<Result<IReadOnlyList<StrategyDto>>>;

public sealed class ListStrategiesQueryValidator : AbstractValidator<ListStrategiesQuery>
{
    public ListStrategiesQueryValidator()
    {
        RuleFor(q => q.Status)
            .Must(s => s is null || Enum.TryParse<StrategyStatus>(s, true, out _))
            .WithMessage("Status must be Draft/Paused/Active or null.");
    }
}

public sealed class ListStrategiesQueryHandler
    : IRequestHandler<ListStrategiesQuery, Result<IReadOnlyList<StrategyDto>>>
{
    private readonly IApplicationDbContext _db;

    public ListStrategiesQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<Result<IReadOnlyList<StrategyDto>>> Handle(
        ListStrategiesQuery request, CancellationToken ct)
    {
        var query = _db.Strategies.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.Status)
            && Enum.TryParse<StrategyStatus>(request.Status, true, out var status))
        {
            query = query.Where(s => s.Status == status);
        }

        var rows = await query
            .OrderByDescending(s => s.UpdatedAt)
            .ToListAsync(ct);

        var items = rows.Select(StrategyMapper.ToDto).ToList();
        return Result.Success<IReadOnlyList<StrategyDto>>(items);
    }
}
