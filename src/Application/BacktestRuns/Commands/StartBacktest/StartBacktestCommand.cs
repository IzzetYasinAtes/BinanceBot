using Ardalis.Result;
using BinanceBot.Application.Abstractions;
using BinanceBot.Domain.BacktestRuns;
using BinanceBot.Domain.Common;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BinanceBot.Application.BacktestRuns.Commands.StartBacktest;

public sealed record StartBacktestCommand(
    long StrategyId,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    decimal InitialBalance) : IRequest<Result<long>>;

public sealed class StartBacktestCommandValidator : AbstractValidator<StartBacktestCommand>
{
    public StartBacktestCommandValidator()
    {
        RuleFor(c => c.StrategyId).GreaterThan(0);
        RuleFor(c => c.InitialBalance).GreaterThan(0m);
        RuleFor(c => c).Must(c => c.ToUtc > c.FromUtc).WithMessage("ToUtc must be after FromUtc");
    }
}

public sealed class StartBacktestCommandHandler : IRequestHandler<StartBacktestCommand, Result<long>>
{
    private readonly IApplicationDbContext _db;
    private readonly IClock _clock;

    public StartBacktestCommandHandler(IApplicationDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Result<long>> Handle(StartBacktestCommand request, CancellationToken ct)
    {
        var strategyExists = await _db.Strategies
            .AsNoTracking()
            .AnyAsync(s => s.Id == request.StrategyId, ct);
        if (!strategyExists)
        {
            return Result<long>.NotFound($"Strategy {request.StrategyId} not found.");
        }

        try
        {
            var run = BacktestRun.Start(request.StrategyId, request.FromUtc, request.ToUtc,
                request.InitialBalance, _clock.UtcNow);
            _db.BacktestRuns.Add(run);
            await _db.SaveChangesAsync(ct);
            return Result.Success(run.Id);
        }
        catch (DomainException ex)
        {
            return Result<long>.Error(ex.Message);
        }
    }
}
