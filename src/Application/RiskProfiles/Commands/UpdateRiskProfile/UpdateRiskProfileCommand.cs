using Ardalis.Result;
using BinanceBot.Application.Abstractions;
using BinanceBot.Domain.Common;
using BinanceBot.Domain.RiskProfiles;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BinanceBot.Application.RiskProfiles.Commands.UpdateRiskProfile;

public sealed record UpdateRiskProfileCommand(
    decimal RiskPerTradePct,
    decimal MaxPositionSizePct,
    decimal MaxDrawdown24hPct,
    decimal MaxDrawdownAllTimePct,
    int MaxConsecutiveLosses,
    int MaxOpenPositions) : IRequest<Result>;

public sealed class UpdateRiskProfileCommandValidator : AbstractValidator<UpdateRiskProfileCommand>
{
    // Loop 14 (research-paper-live-and-sizing.md §C2/C3): bounds widened to match the
    // domain — riskPerTradePct ≤ 5%, maxPositionSizePct ≤ 60%; new MaxOpenPositions
    // throttle is [1, 10]. Domain still has the final word via UpdateLimits.
    public UpdateRiskProfileCommandValidator()
    {
        RuleFor(c => c.RiskPerTradePct).GreaterThan(0m).LessThanOrEqualTo(0.05m);
        RuleFor(c => c.MaxPositionSizePct).GreaterThan(0m).LessThanOrEqualTo(0.60m);
        RuleFor(c => c.MaxDrawdown24hPct).GreaterThan(0m).LessThanOrEqualTo(0.30m);
        RuleFor(c => c.MaxDrawdownAllTimePct).GreaterThan(0m).LessThanOrEqualTo(0.60m);
        RuleFor(c => c.MaxConsecutiveLosses).InclusiveBetween(1, 15);
        RuleFor(c => c.MaxOpenPositions).InclusiveBetween(1, 10);
    }
}

public sealed class UpdateRiskProfileCommandHandler : IRequestHandler<UpdateRiskProfileCommand, Result>
{
    private readonly IApplicationDbContext _db;
    private readonly IClock _clock;

    public UpdateRiskProfileCommandHandler(IApplicationDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Result> Handle(UpdateRiskProfileCommand request, CancellationToken ct)
    {
        var profile = await _db.RiskProfiles.FirstOrDefaultAsync(r => r.Id == RiskProfile.IdFor(TradingMode.Paper), ct);
        if (profile is null) return Result.NotFound("Risk profile missing.");

        try
        {
            profile.UpdateLimits(
                request.RiskPerTradePct,
                request.MaxPositionSizePct,
                request.MaxDrawdown24hPct,
                request.MaxDrawdownAllTimePct,
                request.MaxConsecutiveLosses,
                request.MaxOpenPositions,
                _clock.UtcNow);
        }
        catch (DomainException ex)
        {
            return Result.Error(ex.Message);
        }

        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
