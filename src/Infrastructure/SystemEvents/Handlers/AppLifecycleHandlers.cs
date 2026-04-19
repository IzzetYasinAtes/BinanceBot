using System.Text.Json;
using BinanceBot.Application.SystemEvents;
using BinanceBot.Domain.SystemEvents;
using BinanceBot.Domain.SystemEvents.Events;
using MediatR;

namespace BinanceBot.Infrastructure.SystemEvents.Handlers;

/// <summary>ADR-0016 §16.9.5 — boot olayını <c>SystemEvents</c> tablosuna yazar.</summary>
public sealed class AppStartedSystemEventHandler : INotificationHandler<AppStartedEvent>
{
    private readonly ISystemEventPublisher _publisher;
    public AppStartedSystemEventHandler(ISystemEventPublisher publisher) => _publisher = publisher;

    public Task Handle(AppStartedEvent notification, CancellationToken cancellationToken)
    {
        var details = JsonSerializer.Serialize(new
        {
            host = notification.HostName,
            environment = notification.Environment,
        });
        return _publisher.PublishAsync(
            SystemEventType.Startup,
            "Uygulama başlatıldı",
            details,
            cancellationToken);
    }
}

/// <summary>ADR-0016 §16.9.5 — shutdown olayını yazar.</summary>
public sealed class AppStoppingSystemEventHandler : INotificationHandler<AppStoppingEvent>
{
    private readonly ISystemEventPublisher _publisher;
    public AppStoppingSystemEventHandler(ISystemEventPublisher publisher) => _publisher = publisher;

    public Task Handle(AppStoppingEvent notification, CancellationToken cancellationToken)
    {
        var details = JsonSerializer.Serialize(new { host = notification.HostName });
        return _publisher.PublishAsync(
            SystemEventType.Shutdown,
            "Uygulama durduruluyor",
            details,
            cancellationToken);
    }
}

/// <summary>ADR-0016 §16.9.5 — indicator warmup tamamlandı.</summary>
public sealed class IndicatorWarmupCompletedSystemEventHandler
    : INotificationHandler<IndicatorWarmupCompletedEvent>
{
    private readonly ISystemEventPublisher _publisher;
    public IndicatorWarmupCompletedSystemEventHandler(ISystemEventPublisher publisher) =>
        _publisher = publisher;

    public Task Handle(IndicatorWarmupCompletedEvent notification, CancellationToken cancellationToken)
    {
        var details = JsonSerializer.Serialize(new
        {
            symbol = notification.Symbol,
            oneMinuteBars = notification.OneMinuteBarCount,
            oneHourBars = notification.OneHourBarCount,
        });
        return _publisher.PublishAsync(
            SystemEventType.WarmupCompleted,
            $"Indicator warmup tamamlandı: {notification.Symbol}",
            details,
            cancellationToken);
    }
}

/// <summary>ADR-0016 §16.9.5 — WS durum geçişini yazar.</summary>
public sealed class WsConnectionStateChangedSystemEventHandler
    : INotificationHandler<WsConnectionStateChangedEvent>
{
    private readonly ISystemEventPublisher _publisher;
    public WsConnectionStateChangedSystemEventHandler(ISystemEventPublisher publisher) =>
        _publisher = publisher;

    public Task Handle(WsConnectionStateChangedEvent notification, CancellationToken cancellationToken)
    {
        var details = JsonSerializer.Serialize(new
        {
            from = notification.FromState,
            to = notification.ToState,
        });
        return _publisher.PublishAsync(
            SystemEventType.WsStateChanged,
            $"WS durumu: {notification.FromState} → {notification.ToState}",
            details,
            cancellationToken);
    }
}
