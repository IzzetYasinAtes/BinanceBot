using Ardalis.Result;
using BinanceBot.Application.Abstractions;
using BinanceBot.Domain.Common;
using BinanceBot.Domain.RiskProfiles;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BinanceBot.Application.RiskProfiles.Commands.OverrideRiskCaps;

public sealed record OverrideRiskCapsCommand(
    decimal RiskPerTradeCap,
    decimal MaxPositionCap,
    string AdminNote) : IRequest<Result>;

public sealed class OverrideRiskCapsCommandValidator : AbstractValidator<OverrideRiskCapsCommand>
{
    public OverrideRiskCapsCommandValidator()
    {
        RuleFor(c => c.RiskPerTradeCap).GreaterThan(0m).LessThanOrEqualTo(0.10m);
        RuleFor(c => c.MaxPositionCap).GreaterThan(0m).LessThanOrEqualTo(0.50m);
        RuleFor(c => c.AdminNote).NotEmpty().MaximumLength(500);
    }
}

public sealed class OverrideRiskCapsCommandHandler : IRequestHandler<OverrideRiskCapsCommand, Result>
{
    private readonly IApplicationDbContext _db;
    private readonly IClock _clock;

    public OverrideRiskCapsCommandHandler(IApplicationDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Result> Handle(OverrideRiskCapsCommand request, CancellationToken ct)
    {
        var profile = await _db.RiskProfiles.FirstOrDefaultAsync(r => r.Id == RiskProfile.IdFor(TradingMode.Paper), ct);
        if (profile is null) return Result.NotFound("Risk profile missing.");

        try
        {
            profile.OverrideCaps(request.RiskPerTradeCap, request.MaxPositionCap, request.AdminNote, _clock.UtcNow);
        }
        catch (DomainException ex)
        {
            return Result.Error(ex.Message);
        }

        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
