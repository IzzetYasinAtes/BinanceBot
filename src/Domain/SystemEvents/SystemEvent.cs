using BinanceBot.Domain.Common;

namespace BinanceBot.Domain.SystemEvents;

public sealed class SystemEvent : Entity<long>
{
    public string EventType { get; private set; } = default!;
    public SystemEventSeverity Severity { get; private set; }
    public string PayloadJson { get; private set; } = "{}";
    public Guid? CorrelationId { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public string Source { get; private set; } = default!;

    private SystemEvent() { }

    public static SystemEvent Record(
        string eventType,
        SystemEventSeverity severity,
        string payloadJson,
        string source,
        Guid? correlationId,
        DateTimeOffset occurredAt)
    {
        if (string.IsNullOrWhiteSpace(eventType))
        {
            throw new DomainException("EventType required.");
        }

        return new SystemEvent
        {
            EventType = eventType,
            Severity = severity,
            PayloadJson = payloadJson ?? "{}",
            Source = string.IsNullOrWhiteSpace(source) ? "system" : source,
            CorrelationId = correlationId,
            OccurredAt = occurredAt,
        };
    }
}
