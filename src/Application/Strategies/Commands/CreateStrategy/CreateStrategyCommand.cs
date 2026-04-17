using Ardalis.Result;
using BinanceBot.Application.Abstractions;
using BinanceBot.Domain.Strategies;
using BinanceBot.Domain.ValueObjects;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BinanceBot.Application.Strategies.Commands.CreateStrategy;

public sealed record CreateStrategyCommand(
    string Name,
    string Type,
    IReadOnlyList<string> Symbols,
    string ParametersJson) : IRequest<Result<long>>;

public sealed class CreateStrategyCommandValidator : AbstractValidator<CreateStrategyCommand>
{
    public CreateStrategyCommandValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(80);
        RuleFor(c => c.Type)
            .Must(t => Enum.TryParse<StrategyType>(t, true, out _))
            .WithMessage("Type must be Grid/TrendFollowing/MeanReversion");
        RuleFor(c => c.Symbols).NotEmpty();
    }
}

public sealed class CreateStrategyCommandHandler
    : IRequestHandler<CreateStrategyCommand, Result<long>>
{
    private readonly IApplicationDbContext _db;
    private readonly IClock _clock;

    public CreateStrategyCommandHandler(IApplicationDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Result<long>> Handle(CreateStrategyCommand request, CancellationToken ct)
    {
        if (!Enum.TryParse<StrategyType>(request.Type, true, out var type))
        {
            return Result<long>.Invalid(new ValidationError(
                nameof(CreateStrategyCommand.Type), "Invalid type", null, ValidationSeverity.Error));
        }

        var name = request.Name.Trim();
        var duplicate = await _db.Strategies
            .AsNoTracking()
            .AnyAsync(s => s.Name == name, ct);
        if (duplicate)
        {
            return Result<long>.Conflict($"Strategy '{name}' already exists.");
        }

        var symbols = request.Symbols
            .Select(Symbol.From)
            .ToList();

        var strategy = Strategy.Create(name, type, symbols, request.ParametersJson, _clock.UtcNow);
        _db.Strategies.Add(strategy);
        await _db.SaveChangesAsync(ct);

        return Result.Success(strategy.Id);
    }
}
