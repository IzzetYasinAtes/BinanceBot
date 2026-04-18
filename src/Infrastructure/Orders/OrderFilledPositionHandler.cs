using BinanceBot.Application.Abstractions;
using BinanceBot.Domain.Common;
using BinanceBot.Domain.Orders;
using BinanceBot.Domain.Orders.Events;
using BinanceBot.Domain.Positions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BinanceBot.Infrastructure.Orders;

/// <summary>
/// Position glue: when an order fills, open a new <see cref="Position"/> aggregate,
/// add to the existing one, or close the opposite-side one (ADR-0011 §11.6).
///
/// Per decision-sizing.md Commit 8 this also handles **reverse-side fills as closes**
/// so signal-driven exits (<c>CloseSignalPositionCommand</c>) reactively flip the
/// Position from Open to Closed without an admin call.
/// </summary>
public sealed class OrderFilledPositionHandler : INotificationHandler<OrderFilledEvent>
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IClock _clock;
    private readonly ILogger<OrderFilledPositionHandler> _logger;

    public OrderFilledPositionHandler(
        IServiceScopeFactory scopeFactory,
        IClock clock,
        ILogger<OrderFilledPositionHandler> logger)
    {
        _scopeFactory = scopeFactory;
        _clock = clock;
        _logger = logger;
    }

    public async Task Handle(OrderFilledEvent notification, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        // Tracking read so we can mutate the order's mirrored Position safely.
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

        var fillPrice = order.CumulativeQuoteQty / order.ExecutedQuantity;
        var fillQty = order.ExecutedQuantity;
        var now = _clock.UtcNow;

        var openPosition = await db.Positions
            .FirstOrDefaultAsync(p =>
                p.Symbol == order.Symbol &&
                p.Mode == order.Mode &&
                p.Status == PositionStatus.Open, cancellationToken);

        try
        {
            if (openPosition is null)
            {
                var newSide = order.Side == OrderSide.Buy ? PositionSide.Long : PositionSide.Short;
                // ADR-0012 §12.4: forward Order.StopPrice into Position so StopLossMonitorService
                // can soft-trigger an exit when mark price crosses the level.
                var pos = Position.Open(order.Symbol, newSide, fillQty, fillPrice,
                    order.StopPrice, order.StrategyId, order.Mode, now);
                db.Positions.Add(pos);
                _logger.LogInformation(
                    "Position opened from fill {Cid} side={Side} qty={Qty} price={Price}",
                    notification.ClientOrderId, newSide, fillQty, fillPrice);
            }
            else
            {
                var sameSide =
                    (openPosition.Side == PositionSide.Long && order.Side == OrderSide.Buy) ||
                    (openPosition.Side == PositionSide.Short && order.Side == OrderSide.Sell);

                if (sameSide)
                {
                    openPosition.AddFill(fillQty, fillPrice, now);
                    _logger.LogInformation(
                        "Position {Pos} added fill {Cid} qty={Qty}",
                        openPosition.Id, notification.ClientOrderId, fillQty);
                }
                else if (fillQty >= openPosition.Quantity)
                {
                    openPosition.Close(fillPrice, $"order_{notification.ClientOrderId}", now);
                    _logger.LogInformation(
                        "Position {Pos} closed by reverse fill {Cid} price={Price}",
                        openPosition.Id, notification.ClientOrderId, fillPrice);

                    var leftover = fillQty - openPosition.Quantity;
                    if (leftover > 0m)
                    {
                        var flipSide = order.Side == OrderSide.Buy
                            ? PositionSide.Long
                            : PositionSide.Short;
                        // The flip uses the same incoming stop hint as the closing entry order.
                        var flip = Position.Open(order.Symbol, flipSide, leftover, fillPrice,
                            order.StopPrice, order.StrategyId, order.Mode, now);
                        db.Positions.Add(flip);
                        _logger.LogInformation(
                            "Position {Pos} flipped from leftover qty={Qty}",
                            flip.Id, leftover);
                    }
                }
                else
                {
                    // Partial close: requires aggregate change (Position.Reduce) — not yet supported.
                    _logger.LogWarning(
                        "Partial close not yet supported pos={Pos} order={Cid} fillQty={Fq} posQty={Pq}",
                        openPosition.Id, notification.ClientOrderId, fillQty, openPosition.Quantity);
                }
            }

            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Domain.Common.DomainException ex)
        {
            _logger.LogWarning(ex,
                "Position update failed for filled order {Cid}: {Message}",
                notification.ClientOrderId, ex.Message);
        }
    }
}
