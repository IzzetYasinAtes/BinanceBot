using Ardalis.Result;
using BinanceBot.Application.Abstractions;
using BinanceBot.Domain.Common;
using BinanceBot.Domain.Positions;
using BinanceBot.Domain.ValueObjects;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BinanceBot.Application.Positions.Commands.ClosePosition;

public sealed record ClosePositionCommand(
    string Symbol,
    decimal ExitPrice,
    string Reason) : IRequest<Result<ClosedPositionDto>>;

public sealed record ClosedPositionDto(
    long Id,
    string Symbol,
    decimal RealizedPnl,
    decimal ExitPrice,
    string Reason);

public sealed class ClosePositionCommandValidator : AbstractValidator<ClosePositionCommand>
{
    public ClosePositionCommandValidator()
    {
        RuleFor(c => c.Symbol).NotEmpty();
        RuleFor(c => c.ExitPrice).GreaterThan(0m);
        RuleFor(c => c.Reason).NotEmpty().MaximumLength(200);
    }
}

public sealed class ClosePositionCommandHandler : IRequestHandler<ClosePositionCommand, Result<ClosedPositionDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly IClock _clock;

    public ClosePositionCommandHandler(IApplicationDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Result<ClosedPositionDto>> Handle(ClosePositionCommand request, CancellationToken ct)
    {
        var symbolVo = Symbol.From(request.Symbol);
        var position = await _db.Positions
            .Where(p => p.Symbol == symbolVo && p.Status == PositionStatus.Open)
            .OrderByDescending(p => p.UpdatedAt)
            .FirstOrDefaultAsync(ct);

        if (position is null)
        {
            return Result<ClosedPositionDto>.NotFound($"No open position for {symbolVo}.");
        }

        try
        {
            position.Close(request.ExitPrice, request.Reason, _clock.UtcNow);
        }
        catch (DomainException ex)
        {
            return Result.Error(ex.Message);
        }

        await _db.SaveChangesAsync(ct);
        return Result.Success(new ClosedPositionDto(
            position.Id, position.Symbol.Value,
            position.RealizedPnl, request.ExitPrice, request.Reason));
    }
}
