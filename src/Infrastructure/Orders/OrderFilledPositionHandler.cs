using BinanceBot.Application.Abstractions;
using BinanceBot.Application.Positions.Commands.OpenOrUpdatePosition;
using BinanceBot.Domain.Orders.Events;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BinanceBot.Infrastructure.Orders;

public sealed class OrderFilledPositionHandler : INotificationHandler<OrderFilledEvent>
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OrderFilledPositionHandler> _logger;

    public OrderFilledPositionHandler(
        IServiceScopeFactory scopeFactory,
        ILogger<OrderFilledPositionHandler> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task Handle(OrderFilledEvent notification, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var order = await db.Orders
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.ClientOrderId == notification.ClientOrderId
                                   && o.Mode == notification.Mode, cancellationToken);

        if (order is null)
        {
            _logger.LogWarning("OrderFilled handler: order {Cid} {Mode} not found",
                notification.ClientOrderId, notification.Mode);
            return;
        }

        if (order.ExecutedQuantity == 0m)
        {
            return;
        }

        var avgPrice = order.CumulativeQuoteQty / order.ExecutedQuantity;

        var cmd = new OpenOrUpdatePositionCommand(
            notification.Symbol,
            order.Side.ToString(),
            order.ExecutedQuantity,
            avgPrice,
            order.StrategyId,
            order.Mode);

        var result = await mediator.Send(cmd, cancellationToken);
        if (!result.IsSuccess)
        {
            _logger.LogWarning("Position open/update failed for filled order {Cid}: {Errors}",
                notification.ClientOrderId, string.Join(";", result.Errors));
        }
        else
        {
            _logger.LogInformation("Position {PositionId} updated from OrderFilled {Cid}",
                result.Value, notification.ClientOrderId);
        }
    }
}
