using Ardalis.Result;
using BinanceBot.Application.Abstractions;
using BinanceBot.Application.Strategies.Queries;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BinanceBot.Application.Strategies.Queries.GetStrategyDetail;

public sealed record GetStrategyDetailQuery(long Id) : IRequest<Result<StrategyDetailDto>>;

public sealed class GetStrategyDetailQueryValidator : AbstractValidator<GetStrategyDetailQuery>
{
    public GetStrategyDetailQueryValidator()
    {
        RuleFor(q => q.Id).GreaterThan(0);
    }
}

public sealed class GetStrategyDetailQueryHandler
    : IRequestHandler<GetStrategyDetailQuery, Result<StrategyDetailDto>>
{
    private readonly IApplicationDbContext _db;

    public GetStrategyDetailQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<Result<StrategyDetailDto>> Handle(
        GetStrategyDetailQuery request, CancellationToken ct)
    {
        var strategy = await _db.Strategies
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == request.Id, ct);

        if (strategy is null)
        {
            return Result<StrategyDetailDto>.NotFound($"Strategy {request.Id} not found.");
        }

        var since24h = DateTimeOffset.UtcNow.AddDays(-1);
        var signalCount = await _db.StrategySignals
            .AsNoTracking()
            .CountAsync(s => s.StrategyId == request.Id && s.EmittedAt >= since24h, ct);

        return Result.Success(StrategyMapper.ToDetailDto(strategy, signalCount));
    }
}
