using System.Text.Json;
using BinanceBot.Application.SystemEvents;
using BinanceBot.Domain.Orders.Events;
using BinanceBot.Domain.SystemEvents;
using MediatR;

namespace BinanceBot.Infrastructure.SystemEvents.Handlers;

/// <summary>ADR-0016 §16.9.5 — emir gönderildi.</summary>
public sealed class OrderPlacedSystemEventHandler : INotificationHandler<OrderPlacedEvent>
{
    private readonly ISystemEventPublisher _publisher;
    public OrderPlacedSystemEventHandler(ISystemEventPublisher publisher) => _publisher = publisher;

    public Task Handle(OrderPlacedEvent notification, CancellationToken cancellationToken)
    {
        var details = JsonSerializer.Serialize(new
        {
            clientOrderId = notification.ClientOrderId,
            symbol = notification.Symbol,
            side = notification.Side.ToString(),
            type = notification.Type.ToString(),
            quantity = notification.Quantity,
            price = notification.Price,
            mode = notification.Mode.ToString(),
        });
        return _publisher.PublishAsync(
            SystemEventType.OrderPlaced,
            $"Emir gönderildi: {notification.Symbol} {notification.Side} {notification.Quantity}",
            details,
            cancellationToken);
    }
}

/// <summary>ADR-0016 §16.9.5 — emir gerçekleşti.</summary>
public sealed class OrderFilledSystemEventHandler : INotificationHandler<OrderFilledEvent>
{
    private readonly ISystemEventPublisher _publisher;
    public OrderFilledSystemEventHandler(ISystemEventPublisher publisher) => _publisher = publisher;

    public Task Handle(OrderFilledEvent notification, CancellationToken cancellationToken)
    {
        var avgPrice = notification.ExecutedQuantity > 0m
            ? notification.CumulativeQuoteQty / notification.ExecutedQuantity
            : 0m;
        var details = JsonSerializer.Serialize(new
        {
            clientOrderId = notification.ClientOrderId,
            symbol = notification.Symbol,
            executedQuantity = notification.ExecutedQuantity,
            cumulativeQuoteQty = notification.CumulativeQuoteQty,
            avgPrice,
            mode = notification.Mode.ToString(),
        });
        return _publisher.PublishAsync(
            SystemEventType.OrderFilled,
            $"Emir gerçekleşti: {notification.Symbol} {notification.ExecutedQuantity} @ {avgPrice:F2}",
            details,
            cancellationToken);
    }
}

/// <summary>ADR-0016 §16.9.5 — emir iptal.</summary>
public sealed class OrderCancelledSystemEventHandler : INotificationHandler<OrderCancelledEvent>
{
    private readonly ISystemEventPublisher _publisher;
    public OrderCancelledSystemEventHandler(ISystemEventPublisher publisher) => _publisher = publisher;

    public Task Handle(OrderCancelledEvent notification, CancellationToken cancellationToken)
    {
        var details = JsonSerializer.Serialize(new
        {
            clientOrderId = notification.ClientOrderId,
            symbol = notification.Symbol,
            reason = notification.Reason,
        });
        return _publisher.PublishAsync(
            SystemEventType.OrderCanceled,
            $"Emir iptal: {notification.Symbol} ({notification.Reason})",
            details,
            cancellationToken);
    }
}
