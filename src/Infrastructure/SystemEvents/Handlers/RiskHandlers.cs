using System.Text.Json;
using BinanceBot.Application.SystemEvents;
using BinanceBot.Domain.RiskProfiles.Events;
using BinanceBot.Domain.SystemEvents;
using MediatR;

namespace BinanceBot.Infrastructure.SystemEvents.Handlers;

/// <summary>ADR-0016 §16.9.5 — risk uyarısı (circuit breaker trip).</summary>
public sealed class CircuitBreakerTrippedSystemEventHandler
    : INotificationHandler<CircuitBreakerTrippedEvent>
{
    private readonly ISystemEventPublisher _publisher;
    public CircuitBreakerTrippedSystemEventHandler(ISystemEventPublisher publisher) =>
        _publisher = publisher;

    public Task Handle(CircuitBreakerTrippedEvent notification, CancellationToken cancellationToken)
    {
        var details = JsonSerializer.Serialize(new
        {
            reason = notification.Reason,
            observedDrawdownPct = notification.ObservedDrawdownPct,
            trippedAt = notification.TrippedAt,
        });
        return _publisher.PublishAsync(
            SystemEventType.RiskAlert,
            $"Risk uyarısı: circuit_breaker ({notification.Reason})",
            details,
            cancellationToken);
    }
}
