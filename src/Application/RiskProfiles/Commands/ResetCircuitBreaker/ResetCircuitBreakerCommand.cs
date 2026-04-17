using Ardalis.Result;
using BinanceBot.Application.Abstractions;
using BinanceBot.Domain.Common;
using BinanceBot.Domain.RiskProfiles;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BinanceBot.Application.RiskProfiles.Commands.ResetCircuitBreaker;

public sealed record ResetCircuitBreakerCommand(string AdminNote) : IRequest<Result>;

public sealed class ResetCircuitBreakerCommandValidator : AbstractValidator<ResetCircuitBreakerCommand>
{
    public ResetCircuitBreakerCommandValidator()
    {
        RuleFor(c => c.AdminNote).NotEmpty().MaximumLength(500);
    }
}

public sealed class ResetCircuitBreakerCommandHandler : IRequestHandler<ResetCircuitBreakerCommand, Result>
{
    private readonly IApplicationDbContext _db;
    private readonly IClock _clock;

    public ResetCircuitBreakerCommandHandler(IApplicationDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Result> Handle(ResetCircuitBreakerCommand request, CancellationToken ct)
    {
        var profile = await _db.RiskProfiles.FirstOrDefaultAsync(r => r.Id == RiskProfile.SingletonId, ct);
        if (profile is null) return Result.NotFound("Risk profile missing.");

        try
        {
            profile.ResetCircuitBreaker(request.AdminNote, _clock.UtcNow);
        }
        catch (DomainException ex)
        {
            return Result.Error(ex.Message);
        }

        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
