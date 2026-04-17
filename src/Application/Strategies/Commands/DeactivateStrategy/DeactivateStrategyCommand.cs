using Ardalis.Result;
using BinanceBot.Application.Abstractions;
using BinanceBot.Domain.Common;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BinanceBot.Application.Strategies.Commands.DeactivateStrategy;

public sealed record DeactivateStrategyCommand(long Id, string Reason) : IRequest<Result>;

public sealed class DeactivateStrategyCommandValidator : AbstractValidator<DeactivateStrategyCommand>
{
    public DeactivateStrategyCommandValidator()
    {
        RuleFor(c => c.Id).GreaterThan(0);
        RuleFor(c => c.Reason).NotEmpty().MaximumLength(500);
    }
}

public sealed class DeactivateStrategyCommandHandler : IRequestHandler<DeactivateStrategyCommand, Result>
{
    private readonly IApplicationDbContext _db;
    private readonly IClock _clock;

    public DeactivateStrategyCommandHandler(IApplicationDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Result> Handle(DeactivateStrategyCommand request, CancellationToken ct)
    {
        var strategy = await _db.Strategies.FirstOrDefaultAsync(s => s.Id == request.Id, ct);
        if (strategy is null)
        {
            return Result.NotFound($"Strategy {request.Id} not found.");
        }

        try
        {
            strategy.Deactivate(request.Reason, _clock.UtcNow);
        }
        catch (DomainException ex)
        {
            return Result.Error(ex.Message);
        }

        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
