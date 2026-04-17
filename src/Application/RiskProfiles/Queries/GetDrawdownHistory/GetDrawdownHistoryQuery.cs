using Ardalis.Result;
using BinanceBot.Application.Abstractions;
using BinanceBot.Application.RiskProfiles.Queries;
using BinanceBot.Domain.Positions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BinanceBot.Application.RiskProfiles.Queries.GetDrawdownHistory;

public sealed record GetDrawdownHistoryQuery(int Days) : IRequest<Result<IReadOnlyList<DrawdownPointDto>>>;

public sealed class GetDrawdownHistoryQueryValidator : AbstractValidator<GetDrawdownHistoryQuery>
{
    public GetDrawdownHistoryQueryValidator()
    {
        RuleFor(q => q.Days).InclusiveBetween(1, 365);
    }
}

public sealed class GetDrawdownHistoryQueryHandler
    : IRequestHandler<GetDrawdownHistoryQuery, Result<IReadOnlyList<DrawdownPointDto>>>
{
    private readonly IApplicationDbContext _db;

    public GetDrawdownHistoryQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<Result<IReadOnlyList<DrawdownPointDto>>> Handle(
        GetDrawdownHistoryQuery request, CancellationToken ct)
    {
        var since = DateTimeOffset.UtcNow.AddDays(-request.Days);

        var closed = await _db.Positions
            .AsNoTracking()
            .Where(p => p.Status == PositionStatus.Closed
                     && p.ClosedAt != null
                     && p.ClosedAt >= since)
            .OrderBy(p => p.ClosedAt)
            .Select(p => new { p.ClosedAt, p.RealizedPnl })
            .ToListAsync(ct);

        var points = new List<DrawdownPointDto>(closed.Count);
        decimal equity = 0m;
        decimal peak = 0m;

        foreach (var row in closed)
        {
            equity += row.RealizedPnl;
            if (equity > peak)
            {
                peak = equity;
            }
            var dd = peak > 0m
                ? Math.Max(0m, (peak - equity) / peak)
                : 0m;
            points.Add(new DrawdownPointDto(row.ClosedAt!.Value, equity, dd));
        }

        return Result.Success<IReadOnlyList<DrawdownPointDto>>(points);
    }
}
