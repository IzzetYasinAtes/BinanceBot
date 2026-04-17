using Ardalis.Result;
using BinanceBot.Application.Abstractions;
using BinanceBot.Application.Abstractions.Binance;
using BinanceBot.Domain.Common;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BinanceBot.Application.Orders.Commands.CancelOrder;

public sealed record CancelOrderCommand(string ClientOrderId, string Reason) : IRequest<Result>;

public sealed class CancelOrderCommandValidator : AbstractValidator<CancelOrderCommand>
{
    public CancelOrderCommandValidator()
    {
        RuleFor(c => c.ClientOrderId).NotEmpty().MaximumLength(36);
        RuleFor(c => c.Reason).NotEmpty().MaximumLength(200);
    }
}

public sealed class CancelOrderCommandHandler : IRequestHandler<CancelOrderCommand, Result>
{
    private readonly IApplicationDbContext _db;
    private readonly IBinanceTrading _trading;
    private readonly IClock _clock;

    public CancelOrderCommandHandler(IApplicationDbContext db, IBinanceTrading trading, IClock clock)
    {
        _db = db;
        _trading = trading;
        _clock = clock;
    }

    public async Task<Result> Handle(CancelOrderCommand request, CancellationToken ct)
    {
        var order = await _db.Orders.FirstOrDefaultAsync(o => o.ClientOrderId == request.ClientOrderId, ct);
        if (order is null)
        {
            return Result.NotFound($"Order '{request.ClientOrderId}' not found.");
        }

        try
        {
            order.Cancel(_clock.UtcNow, request.Reason);
        }
        catch (DomainException ex)
        {
            return Result.Error(ex.Message);
        }

        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
