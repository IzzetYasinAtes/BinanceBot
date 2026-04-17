using Ardalis.Result;
using BinanceBot.Application.Abstractions;
using BinanceBot.Domain.Common;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BinanceBot.Application.Strategies.Commands.ActivateStrategy;

public sealed record ActivateStrategyCommand(long Id) : IRequest<Result>;

public sealed class ActivateStrategyCommandValidator : AbstractValidator<ActivateStrategyCommand>
{
    public ActivateStrategyCommandValidator()
    {
        RuleFor(c => c.Id).GreaterThan(0);
    }
}

public sealed class ActivateStrategyCommandHandler : IRequestHandler<ActivateStrategyCommand, Result>
{
    private readonly IApplicationDbContext _db;
    private readonly IClock _clock;

    public ActivateStrategyCommandHandler(IApplicationDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Result> Handle(ActivateStrategyCommand request, CancellationToken ct)
    {
        var strategy = await _db.Strategies.FirstOrDefaultAsync(s => s.Id == request.Id, ct);
        if (strategy is null)
        {
            return Result.NotFound($"Strategy {request.Id} not found.");
        }

        try
        {
            strategy.Activate(_clock.UtcNow);
        }
        catch (DomainException ex)
        {
            return Result.Error(ex.Message);
        }

        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
