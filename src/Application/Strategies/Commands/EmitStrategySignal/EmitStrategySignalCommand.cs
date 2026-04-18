using Ardalis.Result;
using BinanceBot.Application.Abstractions;
using BinanceBot.Domain.Common;
using BinanceBot.Domain.Strategies;
using BinanceBot.Domain.ValueObjects;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BinanceBot.Application.Strategies.Commands.EmitStrategySignal;

public sealed record EmitStrategySignalCommand(
    long StrategyId,
    string Symbol,
    DateTimeOffset BarOpenTime,
    string Direction,
    decimal SuggestedQuantity,
    decimal? SuggestedPrice,
    decimal? SuggestedStopPrice,
    string ContextJson,
    // Loop 10 take-profit fix — default null preserves backward compatibility.
    decimal? SuggestedTakeProfit = null) : IRequest<Result<long>>;

public sealed class EmitStrategySignalCommandValidator : AbstractValidator<EmitStrategySignalCommand>
{
    public EmitStrategySignalCommandValidator()
    {
        RuleFor(c => c.StrategyId).GreaterThan(0);
        RuleFor(c => c.Symbol).NotEmpty();
        RuleFor(c => c.Direction)
            .Must(d => Enum.TryParse<StrategySignalDirection>(d, true, out _))
            .WithMessage("Direction must be Long/Short/Exit");
        RuleFor(c => c.SuggestedQuantity).GreaterThan(0m);
    }
}

public sealed class EmitStrategySignalCommandHandler
    : IRequestHandler<EmitStrategySignalCommand, Result<long>>
{
    private readonly IApplicationDbContext _db;
    private readonly IClock _clock;

    public EmitStrategySignalCommandHandler(IApplicationDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Result<long>> Handle(EmitStrategySignalCommand request, CancellationToken ct)
    {
        var strategy = await _db.Strategies
            .Include(s => s.Signals)
            .FirstOrDefaultAsync(s => s.Id == request.StrategyId, ct);
        if (strategy is null)
        {
            return Result<long>.NotFound($"Strategy {request.StrategyId} not found.");
        }

        if (!Enum.TryParse<StrategySignalDirection>(request.Direction, true, out var direction))
        {
            return Result<long>.Invalid(new ValidationError(
                nameof(request.Direction), "Invalid direction", null, ValidationSeverity.Error));
        }

        var symbolVo = Symbol.From(request.Symbol);
        try
        {
            var signal = strategy.EmitSignal(
                symbolVo,
                request.BarOpenTime,
                direction,
                request.SuggestedQuantity,
                request.SuggestedPrice,
                request.SuggestedStopPrice,
                request.ContextJson,
                _clock.UtcNow,
                takeProfit: request.SuggestedTakeProfit);

            await _db.SaveChangesAsync(ct);
            return Result.Success(signal.Id);
        }
        catch (DomainException ex)
        {
            return Result.Error(ex.Message);
        }
    }
}
