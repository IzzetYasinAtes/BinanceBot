using System.Text.Json;
using BinanceBot.Application.Abstractions;
using BinanceBot.Application.SystemEvents;
using BinanceBot.Domain.SystemEvents;
using BinanceBot.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace BinanceBot.Infrastructure.SystemEvents;

/// <summary>
/// ADR-0016 §16.9.4 — <see cref="ISystemEventPublisher"/> implementasyonu.
/// <c>SystemEvents</c> tablosuna bir satır ekler; hatalar yutulur (telemetry
/// birincil trade yolunu bloke etmemelidir). Serilog zaten birincil kayıt,
/// bu servis UI'ya görünür "Sistem Olayları" akışı için ikincil pipe.
/// </summary>
public sealed class SystemEventPublisher : ISystemEventPublisher
{
    private static readonly JsonSerializerOptions PayloadOptions = new()
    {
        WriteIndented = false,
    };

    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly ILogger<SystemEventPublisher> _logger;

    public SystemEventPublisher(
        ApplicationDbContext db,
        IClock clock,
        ILogger<SystemEventPublisher> logger)
    {
        _db = db;
        _clock = clock;
        _logger = logger;
    }

    public async Task PublishAsync(
        SystemEventType type,
        string message,
        string? detailsJson = null,
        CancellationToken ct = default)
    {
        try
        {
            var payload = BuildPayload(type, message, detailsJson);
            var severity = ResolveSeverity(type);
            var entity = SystemEvent.Record(
                eventType: type.ToString(),
                severity: severity,
                payloadJson: payload,
                source: "system",
                correlationId: null,
                occurredAt: _clock.UtcNow);

            _db.SystemEvents.Add(entity);
            await _db.SaveChangesAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Caller shutting down — do not log as a warning.
        }
        catch (Exception ex)
        {
            // ADR-0016 §16.9.4 swallow rule: telemetry failures must not tear the
            // trade pipeline. Log the miss so operators can chase persistent errors.
            _logger.LogWarning(ex,
                "SystemEvent publish failed type={Type} message={Message}",
                type, message);
        }
    }

    private static string BuildPayload(SystemEventType type, string message, string? detailsJson)
    {
        object? detailsNode = null;
        if (!string.IsNullOrWhiteSpace(detailsJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(detailsJson);
                detailsNode = JsonSerializer.Deserialize<object>(doc.RootElement.GetRawText());
            }
            catch (JsonException)
            {
                // Fall back to raw string so the payload still serialises cleanly.
                detailsNode = detailsJson;
            }
        }

        var wrapper = new
        {
            type = type.ToString(),
            typeCode = (int)type,
            message = message ?? string.Empty,
            details = detailsNode,
        };
        return JsonSerializer.Serialize(wrapper, PayloadOptions);
    }

    private static SystemEventSeverity ResolveSeverity(SystemEventType type) =>
        type switch
        {
            SystemEventType.RiskAlert => SystemEventSeverity.Warning,
            SystemEventType.Shutdown => SystemEventSeverity.Warning,
            _ => SystemEventSeverity.Info,
        };
}
