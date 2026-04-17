using Ardalis.Result;
using BinanceBot.Application.Abstractions;
using BinanceBot.Application.BacktestRuns.Queries;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BinanceBot.Application.BacktestRuns.Queries.GetBacktestResult;

public sealed record GetBacktestResultQuery(long Id) : IRequest<Result<BacktestResultDto>>;

public sealed class GetBacktestResultQueryValidator : AbstractValidator<GetBacktestResultQuery>
{
    public GetBacktestResultQueryValidator()
    {
        RuleFor(q => q.Id).GreaterThan(0);
    }
}

public sealed class GetBacktestResultQueryHandler
    : IRequestHandler<GetBacktestResultQuery, Result<BacktestResultDto>>
{
    private readonly IApplicationDbContext _db;

    public GetBacktestResultQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<Result<BacktestResultDto>> Handle(GetBacktestResultQuery request, CancellationToken ct)
    {
        var run = await _db.BacktestRuns
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == request.Id, ct);

        if (run is null)
        {
            return Result<BacktestResultDto>.NotFound($"BacktestRun {request.Id} not found.");
        }

        var tradeCount = await _db.BacktestTrades
            .AsNoTracking()
            .CountAsync(t => t.BacktestRunId == run.Id, ct);

        return Result.Success(new BacktestResultDto(
            run.Id, run.StrategyId, run.Status.ToString(),
            run.FromUtc, run.ToUtc,
            run.InitialBalance, run.FinalBalance,
            run.Sharpe, run.MaxDrawdownPct, run.WinRate,
            tradeCount, run.FailureReason,
            run.StartedAt, run.CompletedAt));
    }
}
