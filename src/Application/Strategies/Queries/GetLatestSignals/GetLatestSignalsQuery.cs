using Ardalis.Result;
using BinanceBot.Application.Abstractions;
using BinanceBot.Application.Strategies.Queries;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BinanceBot.Application.Strategies.Queries.GetLatestSignals;

public sealed record GetLatestSignalsQuery(int Limit) : IRequest<Result<IReadOnlyList<LatestSignalDto>>>;

public sealed record LatestSignalDto(
    long Id,
    long StrategyId,
    string StrategyName,
    string StrategyType,
    string Symbol,
    string Direction,
    decimal SuggestedQuantity,
    decimal? SuggestedPrice,
    DateTimeOffset BarOpenTime,
    DateTimeOffset EmittedAt);

public sealed class GetLatestSignalsQueryValidator : AbstractValidator<GetLatestSignalsQuery>
{
    public GetLatestSignalsQueryValidator()
    {
        RuleFor(q => q.Limit).InclusiveBetween(1, 100);
    }
}

public sealed class GetLatestSignalsQueryHandler
    : IRequestHandler<GetLatestSignalsQuery, Result<IReadOnlyList<LatestSignalDto>>>
{
    private readonly IApplicationDbContext _db;

    public GetLatestSignalsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<Result<IReadOnlyList<LatestSignalDto>>> Handle(
        GetLatestSignalsQuery request, CancellationToken ct)
    {
        var rows = await (
            from sig in _db.StrategySignals.AsNoTracking()
            join s in _db.Strategies.AsNoTracking() on sig.StrategyId equals s.Id
            orderby sig.EmittedAt descending
            select new
            {
                sig.Id,
                sig.StrategyId,
                s.Name,
                s.Type,
                SymbolValue = sig.Symbol,
                sig.Direction,
                sig.SuggestedQuantity,
                sig.SuggestedPrice,
                sig.BarOpenTime,
                sig.EmittedAt,
            })
            .Take(request.Limit)
            .ToListAsync(ct);

        var items = rows.Select(r => new LatestSignalDto(
            r.Id, r.StrategyId, r.Name, r.Type.ToString(),
            r.SymbolValue.Value,
            r.Direction.ToString(),
            r.SuggestedQuantity, r.SuggestedPrice,
            r.BarOpenTime, r.EmittedAt)).ToList();

        return Result.Success<IReadOnlyList<LatestSignalDto>>(items);
    }
}
