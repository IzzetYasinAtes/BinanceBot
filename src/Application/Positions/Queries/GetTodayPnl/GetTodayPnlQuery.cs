using Ardalis.Result;
using BinanceBot.Application.Abstractions;
using BinanceBot.Application.Positions.Queries;
using BinanceBot.Domain.Positions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BinanceBot.Application.Positions.Queries.GetTodayPnl;

public sealed record GetTodayPnlQuery() : IRequest<Result<TodayPnlDto>>;

public sealed class GetTodayPnlQueryHandler
    : IRequestHandler<GetTodayPnlQuery, Result<TodayPnlDto>>
{
    private readonly IApplicationDbContext _db;

    public GetTodayPnlQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<Result<TodayPnlDto>> Handle(GetTodayPnlQuery request, CancellationToken ct)
    {
        var todayStart = new DateTimeOffset(
            DateTime.UtcNow.Date, TimeSpan.Zero);

        var realizedToday = await _db.Positions
            .AsNoTracking()
            .Where(p => p.Status == PositionStatus.Closed
                     && p.ClosedAt != null
                     && p.ClosedAt >= todayStart)
            .SumAsync(p => (decimal?)p.RealizedPnl, ct) ?? 0m;

        var openStats = await _db.Positions
            .AsNoTracking()
            .Where(p => p.Status == PositionStatus.Open)
            .GroupBy(p => 1)
            .Select(g => new
            {
                Unrealized = g.Sum(p => p.UnrealizedPnl),
                Count = g.Count(),
            })
            .FirstOrDefaultAsync(ct);

        var unrealized = openStats?.Unrealized ?? 0m;
        var count = openStats?.Count ?? 0;

        return Result.Success(new TodayPnlDto(realizedToday, unrealized, count));
    }
}
