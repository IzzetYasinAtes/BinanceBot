using Ardalis.Result;
using BinanceBot.Application.Abstractions;
using BinanceBot.Domain.Common;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BinanceBot.Application.Strategies.Commands.UpdateStrategyParameters;

public sealed record UpdateStrategyParametersCommand(long Id, string ParametersJson) : IRequest<Result>;

public sealed class UpdateStrategyParametersCommandValidator : AbstractValidator<UpdateStrategyParametersCommand>
{
    public UpdateStrategyParametersCommandValidator()
    {
        RuleFor(c => c.Id).GreaterThan(0);
        RuleFor(c => c.ParametersJson).NotEmpty().MaximumLength(8000);
    }
}

public sealed class UpdateStrategyParametersCommandHandler
    : IRequestHandler<UpdateStrategyParametersCommand, Result>
{
    private readonly IApplicationDbContext _db;
    private readonly IClock _clock;

    public UpdateStrategyParametersCommandHandler(IApplicationDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Result> Handle(UpdateStrategyParametersCommand request, CancellationToken ct)
    {
        var strategy = await _db.Strategies.FirstOrDefaultAsync(s => s.Id == request.Id, ct);
        if (strategy is null)
        {
            return Result.NotFound($"Strategy {request.Id} not found.");
        }

        try
        {
            strategy.UpdateParameters(request.ParametersJson, _clock.UtcNow);
        }
        catch (DomainException ex)
        {
            return Result.Error(ex.Message);
        }

        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
