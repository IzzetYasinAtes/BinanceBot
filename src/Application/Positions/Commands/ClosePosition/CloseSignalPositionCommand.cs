using Ardalis.Result;
using BinanceBot.Application.Abstractions;
using BinanceBot.Application.Orders.Commands.PlaceOrder;
using BinanceBot.Domain.Common;
using BinanceBot.Domain.Orders;
using BinanceBot.Domain.Positions;
using BinanceBot.Domain.ValueObjects;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BinanceBot.Application.Positions.Commands.ClosePosition;

/// <summary>
/// Signal-driven exit: looks up the open position for (Symbol, Mode), submits a reverse-side
/// MARKET order, and lets <c>OrderFilledPositionHandler</c> reactively close the
/// <see cref="Position"/> aggregate (ADR-0011 §11.6 + decision-sizing.md Commit 7).
///
/// Distinct from the admin <c>ClosePositionCommand</c> which closes deterministically with a
/// caller-supplied <c>ExitPrice</c>. This command is used by
/// <c>StrategySignalToOrderHandler</c> when an Exit signal fires per mode.
/// </summary>
public sealed record CloseSignalPositionCommand(
    string Symbol,
    long? StrategyId,
    TradingMode Mode,
    string Reason,
    string CorrelationCidPrefix) : IRequest<Result<ClosedSignalPositionDto>>;

public sealed record ClosedSignalPositionDto(
    long PositionId,
    decimal ApproxPnl,
    string Reason,
    string CloseClientOrderId);

public sealed class CloseSignalPositionCommandValidator : AbstractValidator<CloseSignalPositionCommand>
{
    public CloseSignalPositionCommandValidator()
    {
        RuleFor(c => c.Symbol).NotEmpty();
        RuleFor(c => c.Reason).NotEmpty().MaximumLength(200);
        // 64 = DB column ClientOrderId max length; reserve ~10 chars for `-x-{modeSuffix}`
        // suffix appended in the handler (e.g. `-x-paper`, `-x-livet`, `-x-livem`).
        // Worst case prefix `sig-{long.MaxValue}-{unix}` ≈ 37 chars, well under 54.
        RuleFor(c => c.CorrelationCidPrefix).NotEmpty().MaximumLength(54);
        RuleFor(c => c.Mode).IsInEnum();
    }
}

public sealed class CloseSignalPositionCommandHandler
    : IRequestHandler<CloseSignalPositionCommand, Result<ClosedSignalPositionDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly IMediator _mediator;

    public CloseSignalPositionCommandHandler(IApplicationDbContext db, IMediator mediator)
    {
        _db = db;
        _mediator = mediator;
    }

    public async Task<Result<ClosedSignalPositionDto>> Handle(
        CloseSignalPositionCommand req, CancellationToken ct)
    {
        // EF Core cannot translate the `Symbol.Value` property accessor in WHERE predicates
        // (Symbol is a value object mapped via HasConversion). Compare against the VO directly.
        var symbolVo = Symbol.From(req.Symbol);
        var position = await _db.Positions
            .AsNoTracking()
            .FirstOrDefaultAsync(p =>
                p.Symbol == symbolVo &&
                p.Mode == req.Mode &&
                p.Status == PositionStatus.Open, ct);
        if (position is null)
        {
            return Result<ClosedSignalPositionDto>.NotFound(
                $"No open position for {req.Symbol} mode={req.Mode}");
        }

        var reverseSide = position.Side == PositionSide.Long ? OrderSide.Sell : OrderSide.Buy;
        // Suffix "x" disambiguates exit cid from entry cid for the same bar (ADR-0008 §8.2).
        var cid = $"{req.CorrelationCidPrefix}-x-{req.Mode.ToCidSuffix()}";

        var placeResult = await _mediator.Send(new PlaceOrderCommand(
            cid,
            req.Symbol,
            reverseSide.ToString(),
            OrderType.Market.ToString(),
            TimeInForce.Ioc.ToString(),
            position.Quantity,
            null,
            null,
            req.StrategyId,
            req.Mode), ct);

        if (!placeResult.IsSuccess)
        {
            return Result<ClosedSignalPositionDto>.Error(string.Join(";", placeResult.Errors));
        }

        // Position close happens reactively via OrderFilledPositionHandler (ADR-0011 §11.6).
        // The PnL returned here is an approximate mark-to-market value at request time.
        return Result.Success(new ClosedSignalPositionDto(
            position.Id, position.UnrealizedPnl, req.Reason, cid));
    }
}
