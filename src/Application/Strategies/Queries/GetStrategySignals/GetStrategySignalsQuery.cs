using Ardalis.Result;
using BinanceBot.Application.Abstractions;
using BinanceBot.Application.Strategies.Queries;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BinanceBot.Application.Strategies.Queries.GetStrategySignals;

public sealed record GetStrategySignalsQuery(
    long StrategyId,
    DateTimeOffset? From,
    DateTimeOffset? To) : IRequest<Result<IReadOnlyList<StrategySignalDto>>>;

public sealed class GetStrategySignalsQueryValidator : AbstractValidator<GetStrategySignalsQuery>
{
    public GetStrategySignalsQueryValidator()
    {
        RuleFor(q => q.StrategyId).GreaterThan(0);
        RuleFor(q => q)
            .Must(q => q.From is null || q.To is null || q.To >= q.From);
    }
}

public sealed class GetStrategySignalsQueryHandler
    : IRequestHandler<GetStrategySignalsQuery, Result<IReadOnlyList<StrategySignalDto>>>
{
    private readonly IApplicationDbContext _db;

    public GetStrategySignalsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<Result<IReadOnlyList<StrategySignalDto>>> Handle(
        GetStrategySignalsQuery request, CancellationToken ct)
    {
        var query = _db.StrategySignals
            .AsNoTracking()
            .Where(s => s.StrategyId == request.StrategyId);

        if (request.From is not null)
        {
            query = query.Where(s => s.EmittedAt >= request.From);
        }
        if (request.To is not null)
        {
            query = query.Where(s => s.EmittedAt <= request.To);
        }

        var rows = await query
            .OrderByDescending(s => s.EmittedAt)
            .Take(500)
            .ToListAsync(ct);

        var items = rows.Select(StrategyMapper.ToSignalDto).ToList();
        return Result.Success<IReadOnlyList<StrategySignalDto>>(items);
    }
}
