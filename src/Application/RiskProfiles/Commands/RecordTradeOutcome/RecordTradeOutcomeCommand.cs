using Ardalis.Result;
using BinanceBot.Application.Abstractions;
using BinanceBot.Domain.Common;
using BinanceBot.Domain.RiskProfiles;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BinanceBot.Application.RiskProfiles.Commands.RecordTradeOutcome;

public sealed record RecordTradeOutcomeCommand(
    TradingMode Mode,
    decimal RealizedPnl,
    decimal EquityAfter) : IRequest<Result>;

public sealed class RecordTradeOutcomeCommandHandler : IRequestHandler<RecordTradeOutcomeCommand, Result>
{
    private readonly IApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly ILogger<RecordTradeOutcomeCommandHandler> _logger;

    public RecordTradeOutcomeCommandHandler(
        IApplicationDbContext db,
        IClock clock,
        ILogger<RecordTradeOutcomeCommandHandler> logger)
    {
        _db = db;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result> Handle(RecordTradeOutcomeCommand request, CancellationToken ct)
    {
        var profileId = RiskProfile.IdFor(request.Mode);
        var profile = await _db.RiskProfiles.FirstOrDefaultAsync(r => r.Id == profileId, ct);
        if (profile is null) return Result.NotFound("Risk profile missing.");

        // ADR-0012 §12.10: structured before/after audit so Loop 5 t30 grep can pinpoint
        // CB-trip root cause (handler not entered? consec didn't move? threshold not crossed?).
        _logger.LogInformation(
            "CB-AUDIT mode={Mode} pnl={Pnl} consecBefore={Before} statusBefore={StatusBefore} ddBefore={DDBefore}",
            request.Mode,
            request.RealizedPnl,
            profile.ConsecutiveLosses,
            profile.CircuitBreakerStatus,
            profile.CurrentDrawdownPct);

        var statusBefore = profile.CircuitBreakerStatus;
        profile.RecordTradeOutcome(request.RealizedPnl, request.EquityAfter, _clock.UtcNow);

        _logger.LogInformation(
            "CB-AUDIT mode={Mode} consecAfter={After} statusAfter={StatusAfter} ddAfter={DDAfter} maxLosses={MaxLosses}",
            request.Mode,
            profile.ConsecutiveLosses,
            profile.CircuitBreakerStatus,
            profile.CurrentDrawdownPct,
            profile.MaxConsecutiveLosses);

        // Loop 8 bug #19: trip evaluation (consec losses + 24h/all-time drawdown) now
        // lives in the domain (RiskProfile.RecordTradeOutcome → TripIfDrawdownBreached).
        // The handler stays the audit voice so the Loop 5 grep contract still works.
        if (statusBefore == CircuitBreakerStatus.Healthy
            && profile.CircuitBreakerStatus == CircuitBreakerStatus.Tripped)
        {
            _logger.LogWarning(
                "CB-AUDIT tripped mode={Mode} reason={Reason} consec={Consec}/{Max} drawdown={DD} dd24h={DD24h} ddAll={DDAll}",
                request.Mode,
                profile.CircuitBreakerReason,
                profile.ConsecutiveLosses, profile.MaxConsecutiveLosses,
                profile.CurrentDrawdownPct,
                profile.MaxDrawdown24hPct,
                profile.MaxDrawdownAllTimePct);
        }
        else if (profile.CircuitBreakerStatus == CircuitBreakerStatus.Healthy)
        {
            _logger.LogInformation(
                "CB-AUDIT not-tripped mode={Mode} consec={Consec}/{Max} drawdown={DD} dd24h={DD24h} ddAll={DDAll}",
                request.Mode,
                profile.ConsecutiveLosses, profile.MaxConsecutiveLosses,
                profile.CurrentDrawdownPct,
                profile.MaxDrawdown24hPct,
                profile.MaxDrawdownAllTimePct);
        }

        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
