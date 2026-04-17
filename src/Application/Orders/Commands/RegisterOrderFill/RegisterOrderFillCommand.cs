using Ardalis.Result;
using BinanceBot.Application.Abstractions;
using BinanceBot.Domain.Common;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BinanceBot.Application.Orders.Commands.RegisterOrderFill;

public sealed record RegisterOrderFillCommand(
    string ClientOrderId,
    long ExchangeTradeId,
    decimal Price,
    decimal Quantity,
    decimal Commission,
    string CommissionAsset,
    DateTimeOffset FilledAt) : IRequest<Result>;

public sealed class RegisterOrderFillCommandValidator : AbstractValidator<RegisterOrderFillCommand>
{
    public RegisterOrderFillCommandValidator()
    {
        RuleFor(c => c.ClientOrderId).NotEmpty();
        RuleFor(c => c.ExchangeTradeId).GreaterThan(0);
        RuleFor(c => c.Price).GreaterThan(0m);
        RuleFor(c => c.Quantity).GreaterThan(0m);
    }
}

public sealed class RegisterOrderFillCommandHandler : IRequestHandler<RegisterOrderFillCommand, Result>
{
    private readonly IApplicationDbContext _db;

    public RegisterOrderFillCommandHandler(IApplicationDbContext db) => _db = db;

    public async Task<Result> Handle(RegisterOrderFillCommand request, CancellationToken ct)
    {
        var order = await _db.Orders
            .Include(o => o.Fills)
            .FirstOrDefaultAsync(o => o.ClientOrderId == request.ClientOrderId, ct);

        if (order is null)
        {
            return Result.NotFound($"Order '{request.ClientOrderId}' not found.");
        }

        try
        {
            order.RegisterFill(
                request.ExchangeTradeId, request.Price, request.Quantity,
                request.Commission, request.CommissionAsset, request.FilledAt);
        }
        catch (DomainException ex)
        {
            return Result.Error(ex.Message);
        }

        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
