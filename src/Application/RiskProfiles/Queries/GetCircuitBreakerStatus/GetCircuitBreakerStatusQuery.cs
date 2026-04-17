using Ardalis.Result;
using BinanceBot.Application.Abstractions;
using BinanceBot.Application.RiskProfiles.Queries;
using BinanceBot.Domain.Common;
using BinanceBot.Domain.RiskProfiles;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BinanceBot.Application.RiskProfiles.Queries.GetCircuitBreakerStatus;

public sealed record GetCircuitBreakerStatusQuery() : IRequest<Result<CircuitBreakerStatusDto>>;

public sealed class GetCircuitBreakerStatusQueryHandler
    : IRequestHandler<GetCircuitBreakerStatusQuery, Result<CircuitBreakerStatusDto>>
{
    private readonly IApplicationDbContext _db;

    public GetCircuitBreakerStatusQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<Result<CircuitBreakerStatusDto>> Handle(
        GetCircuitBreakerStatusQuery request, CancellationToken ct)
    {
        var r = await _db.RiskProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(rp => rp.Id == RiskProfile.IdFor(TradingMode.Paper), ct);

        if (r is null)
        {
            return Result<CircuitBreakerStatusDto>.NotFound("Risk profile not initialised.");
        }

        return Result.Success(new CircuitBreakerStatusDto(
            r.CircuitBreakerStatus.ToString(),
            r.CircuitBreakerReason,
            r.CircuitBreakerTrippedAt,
            r.CurrentDrawdownPct));
    }
}
