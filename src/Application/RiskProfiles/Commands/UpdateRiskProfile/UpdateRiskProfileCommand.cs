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
    int MaxConsecutiveLosses) : IRequest<Result>;

public sealed class UpdateRiskProfileCommandValidator : AbstractValidator<UpdateRiskProfileCommand>
{
    public UpdateRiskProfileCommandValidator()
    {
        RuleFor(c => c.RiskPerTradePct).GreaterThan(0m).LessThanOrEqualTo(0.02m);
        RuleFor(c => c.MaxPositionSizePct).GreaterThan(0m).LessThanOrEqualTo(0.20m);
        RuleFor(c => c.MaxDrawdown24hPct).GreaterThan(0m).LessThanOrEqualTo(0.10m);
        RuleFor(c => c.MaxDrawdownAllTimePct).GreaterThan(0m).LessThanOrEqualTo(0.50m);
        RuleFor(c => c.MaxConsecutiveLosses).InclusiveBetween(1, 10);
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
        var profile = await _db.RiskProfiles.FirstOrDefaultAsync(r => r.Id == RiskProfile.SingletonId, ct);
        if (profile is null) return Result.NotFound("Risk profile missing.");

        try
        {
            profile.UpdateLimits(
                request.RiskPerTradePct,
                request.MaxPositionSizePct,
                request.MaxDrawdown24hPct,
                request.MaxDrawdownAllTimePct,
                request.MaxConsecutiveLosses,
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
