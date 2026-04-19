using System.Text.Json;
using BinanceBot.Application.SystemEvents;
using BinanceBot.Domain.Positions.Events;
using BinanceBot.Domain.SystemEvents;
using MediatR;

namespace BinanceBot.Infrastructure.SystemEvents.Handlers;

/// <summary>ADR-0016 §16.9.5 — pozisyon açıldı.</summary>
public sealed class PositionOpenedSystemEventHandler : INotificationHandler<PositionOpenedEvent>
{
    private readonly ISystemEventPublisher _publisher;
    public PositionOpenedSystemEventHandler(ISystemEventPublisher publisher) => _publisher = publisher;

    public Task Handle(PositionOpenedEvent notification, CancellationToken cancellationToken)
    {
        var details = JsonSerializer.Serialize(new
        {
            positionId = notification.PositionId,
            symbol = notification.Symbol,
            side = notification.Side.ToString(),
            entryPrice = notification.EntryPrice,
            quantity = notification.Quantity,
            mode = notification.Mode.ToString(),
        });
        return _publisher.PublishAsync(
            SystemEventType.PositionOpened,
            $"Pozisyon açıldı: {notification.Symbol} {notification.Side} {notification.Quantity}",
            details,
            cancellationToken);
    }
}

/// <summary>ADR-0016 §16.9.5 — pozisyon kapandı.</summary>
public sealed class PositionClosedSystemEventHandler : INotificationHandler<PositionClosedEvent>
{
    private readonly ISystemEventPublisher _publisher;
    public PositionClosedSystemEventHandler(ISystemEventPublisher publisher) => _publisher = publisher;

    public Task Handle(PositionClosedEvent notification, CancellationToken cancellationToken)
    {
        var details = JsonSerializer.Serialize(new
        {
            positionId = notification.PositionId,
            symbol = notification.Symbol,
            realizedPnl = notification.RealizedPnl,
            reason = notification.Reason,
            mode = notification.Mode.ToString(),
        });
        return _publisher.PublishAsync(
            SystemEventType.PositionClosed,
            $"Pozisyon kapandı: {notification.Symbol} PnL=${notification.RealizedPnl:F2}",
            details,
            cancellationToken);
    }
}
