using Ardalis.Result;
using BinanceBot.Application.Abstractions;
using BinanceBot.Application.BacktestRuns.Queries;
using BinanceBot.Application.Common;
using BinanceBot.Domain.BacktestRuns;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BinanceBot.Application.BacktestRuns.Queries.ListBacktestRuns;

public sealed record ListBacktestRunsQuery(
    long? StrategyId,
    string? Status,
    int Skip,
    int Take) : IRequest<Result<PagedList<BacktestRunSummaryDto>>>;

public sealed class ListBacktestRunsQueryValidator : AbstractValidator<ListBacktestRunsQuery>
{
    public ListBacktestRunsQueryValidator()
    {
        RuleFor(q => q.Skip).GreaterThanOrEqualTo(0);
        RuleFor(q => q.Take).InclusiveBetween(1, 100);
        RuleFor(q => q.Status)
            .Must(s => s is null || Enum.TryParse<BacktestStatus>(s, true, out _));
    }
}

public sealed class ListBacktestRunsQueryHandler
    : IRequestHandler<ListBacktestRunsQuery, Result<PagedList<BacktestRunSummaryDto>>>
{
    private readonly IApplicationDbContext _db;

    public ListBacktestRunsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<Result<PagedList<BacktestRunSummaryDto>>> Handle(
        ListBacktestRunsQuery request, CancellationToken ct)
    {
        var query = _db.BacktestRuns.AsNoTracking().AsQueryable();

        if (request.StrategyId is not null)
        {
            query = query.Where(r => r.StrategyId == request.StrategyId);
        }
        if (!string.IsNullOrWhiteSpace(request.Status)
            && Enum.TryParse<BacktestStatus>(request.Status, true, out var status))
        {
            query = query.Where(r => r.Status == status);
        }

        var total = await query.LongCountAsync(ct);
        var rows = await query
            .OrderByDescending(r => r.StartedAt)
            .Skip(request.Skip)
            .Take(request.Take)
            .ToListAsync(ct);

        var items = rows.Select(r => new BacktestRunSummaryDto(
            r.Id, r.StrategyId, r.Status.ToString(),
            r.FromUtc, r.ToUtc,
            r.InitialBalance, r.FinalBalance,
            r.Sharpe, r.MaxDrawdownPct, r.WinRate,
            r.StartedAt, r.CompletedAt)).ToList();

        return Result.Success(new BinanceBot.Application.Common.PagedList<BacktestRunSummaryDto>(items, total, request.Skip, request.Take));
    }
}
