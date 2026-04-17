using Ardalis.Result;
using BinanceBot.Application.Abstractions;
using BinanceBot.Domain.Common;
using BinanceBot.Domain.Orders;
using BinanceBot.Domain.Positions;
using BinanceBot.Domain.ValueObjects;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BinanceBot.Application.Positions.Commands.OpenOrUpdatePosition;

public sealed record OpenOrUpdatePositionCommand(
    string Symbol,
    string OrderSide,
    decimal Quantity,
    decimal Price,
    long? StrategyId,
    TradingMode Mode) : IRequest<Result<long>>;

public sealed class OpenOrUpdatePositionCommandHandler
    : IRequestHandler<OpenOrUpdatePositionCommand, Result<long>>
{
    private readonly IApplicationDbContext _db;
    private readonly IClock _clock;

    public OpenOrUpdatePositionCommandHandler(IApplicationDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Result<long>> Handle(OpenOrUpdatePositionCommand request, CancellationToken ct)
    {
        var symbolVo = Symbol.From(request.Symbol);
        if (!Enum.TryParse<OrderSide>(request.OrderSide, true, out var side))
        {
            return Result<long>.Invalid(new ValidationError(
                nameof(request.OrderSide), "Invalid side", null, ValidationSeverity.Error));
        }

        var positionSide = side == OrderSide.Buy ? PositionSide.Long : PositionSide.Short;

        var open = await _db.Positions
            .Where(p => p.Symbol == symbolVo
                     && p.Mode == request.Mode
                     && p.Status == PositionStatus.Open)
            .FirstOrDefaultAsync(ct);

        try
        {
            if (open is null)
            {
                var created = Position.Open(symbolVo, positionSide, request.Quantity, request.Price,
                    request.StrategyId, request.Mode, _clock.UtcNow);
                _db.Positions.Add(created);
                await _db.SaveChangesAsync(ct);
                return Result.Success(created.Id);
            }

            if (open.Side != positionSide)
            {
                return Result<long>.Conflict(
                    $"Open position {open.Id} has side {open.Side}, incoming fill side {positionSide}");
            }

            open.AddFill(request.Quantity, request.Price, _clock.UtcNow);
            await _db.SaveChangesAsync(ct);
            return Result.Success(open.Id);
        }
        catch (DomainException ex)
        {
            return Result.Error(ex.Message);
        }
    }
}
