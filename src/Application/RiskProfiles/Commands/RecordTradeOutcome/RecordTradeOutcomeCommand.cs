using Ardalis.Result;
using BinanceBot.Application.Abstractions;
using BinanceBot.Domain.RiskProfiles;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BinanceBot.Application.RiskProfiles.Commands.RecordTradeOutcome;

public sealed record RecordTradeOutcomeCommand(
    decimal RealizedPnl,
    decimal EquityAfter) : IRequest<Result>;

public sealed class RecordTradeOutcomeCommandHandler : IRequestHandler<RecordTradeOutcomeCommand, Result>
{
    private readonly IApplicationDbContext _db;
    private readonly IClock _clock;

    public RecordTradeOutcomeCommandHandler(IApplicationDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Result> Handle(RecordTradeOutcomeCommand request, CancellationToken ct)
    {
        var profile = await _db.RiskProfiles.FirstOrDefaultAsync(r => r.Id == RiskProfile.SingletonId, ct);
        if (profile is null) return Result.NotFound("Risk profile missing.");

        profile.RecordTradeOutcome(request.RealizedPnl, request.EquityAfter, _clock.UtcNow);

        if (profile.CircuitBreakerStatus == CircuitBreakerStatus.Healthy)
        {
            var tripped = false;
            string? reason = null;
            if (profile.ConsecutiveLosses >= profile.MaxConsecutiveLosses)
            {
                tripped = true;
                reason = $"consecutive_losses={profile.ConsecutiveLosses}";
            }
            else if (profile.CurrentDrawdownPct >= profile.MaxDrawdownAllTimePct)
            {
                tripped = true;
                reason = $"drawdown={profile.CurrentDrawdownPct:P2}";
            }

            if (tripped)
            {
                profile.TripCircuitBreaker(reason!, profile.CurrentDrawdownPct, _clock.UtcNow);
            }
        }

        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
