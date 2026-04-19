using System.Collections.Concurrent;
using System.Text.Json;
using BinanceBot.Application.SystemEvents;
using BinanceBot.Domain.Strategies.Events;
using BinanceBot.Domain.SystemEvents;
using BinanceBot.Domain.SystemEvents.Events;
using MediatR;

namespace BinanceBot.Infrastructure.SystemEvents.Handlers;

/// <summary>ADR-0016 §16.9.5 — strateji aktif edildi.</summary>
public sealed class StrategyActivatedSystemEventHandler : INotificationHandler<StrategyActivatedEvent>
{
    private readonly ISystemEventPublisher _publisher;
    public StrategyActivatedSystemEventHandler(ISystemEventPublisher publisher) => _publisher = publisher;

    public Task Handle(StrategyActivatedEvent notification, CancellationToken cancellationToken)
    {
        var details = JsonSerializer.Serialize(new { strategyId = notification.StrategyId });
        return _publisher.PublishAsync(
            SystemEventType.StrategyActivated,
            $"Strateji aktif edildi: {notification.StrategyId}",
            details,
            cancellationToken);
    }
}

/// <summary>ADR-0016 §16.9.5 — strateji deaktif edildi.</summary>
public sealed class StrategyDeactivatedSystemEventHandler : INotificationHandler<StrategyDeactivatedEvent>
{
    private readonly ISystemEventPublisher _publisher;
    public StrategyDeactivatedSystemEventHandler(ISystemEventPublisher publisher) => _publisher = publisher;

    public Task Handle(StrategyDeactivatedEvent notification, CancellationToken cancellationToken)
    {
        var details = JsonSerializer.Serialize(new
        {
            strategyId = notification.StrategyId,
            reason = notification.Reason,
        });
        return _publisher.PublishAsync(
            SystemEventType.StrategyDeactivated,
            $"Strateji deaktif edildi: {notification.StrategyId} ({notification.Reason})",
            details,
            cancellationToken);
    }
}

/// <summary>ADR-0016 §16.9.5 — sinyal üretildi.</summary>
public sealed class StrategySignalEmittedSystemEventHandler
    : INotificationHandler<StrategySignalEmittedEvent>
{
    private readonly ISystemEventPublisher _publisher;
    public StrategySignalEmittedSystemEventHandler(ISystemEventPublisher publisher) =>
        _publisher = publisher;

    public Task Handle(StrategySignalEmittedEvent notification, CancellationToken cancellationToken)
    {
        var details = JsonSerializer.Serialize(new
        {
            strategyId = notification.StrategyId,
            symbol = notification.Symbol,
            direction = notification.Direction.ToString(),
            barOpenTime = notification.BarOpenTime,
            suggestedStop = notification.SuggestedStopPrice,
            suggestedTakeProfit = notification.SuggestedTakeProfit,
        });
        return _publisher.PublishAsync(
            SystemEventType.SignalEmitted,
            $"Sinyal üretildi: {notification.Symbol} {notification.Direction}",
            details,
            cancellationToken);
    }
}

/// <summary>
/// ADR-0016 §16.9.5 — sinyal atlandı (gate-false / cooldown / throttle).
/// Hot-path'te evaluator her bar skip ederse loglar patlar — handler
/// per-(strategyId, minute) pencereyle max 1 event yazar. Pencereler
/// <see cref="DateTimeOffset.UtcNow"/> dakikasından türetilir; concurrency için
/// <see cref="ConcurrentDictionary{TKey,TValue}"/> yeterli.
/// </summary>
public sealed class StrategySignalSkippedSystemEventHandler
    : INotificationHandler<StrategySignalSkippedEvent>
{
    // Key = "{strategyId}:{minuteBucket}"; value ignored (we only care about
    // presence). Stale keys are pruned lazily when the dictionary hits the
    // prune threshold.
    private static readonly ConcurrentDictionary<string, byte> ThrottleKeys = new();
    private const int PruneThreshold = 4096;

    private readonly ISystemEventPublisher _publisher;
    public StrategySignalSkippedSystemEventHandler(ISystemEventPublisher publisher) =>
        _publisher = publisher;

    public Task Handle(StrategySignalSkippedEvent notification, CancellationToken cancellationToken)
    {
        var minuteBucket = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60L;
        var key = $"{notification.StrategyId}:{minuteBucket}";

        // TryAdd returns false if an entry already exists — this minute's
        // first skip has already been logged, squelch the rest.
        if (!ThrottleKeys.TryAdd(key, 0))
        {
            return Task.CompletedTask;
        }

        if (ThrottleKeys.Count > PruneThreshold)
        {
            PruneStale(minuteBucket);
        }

        var details = JsonSerializer.Serialize(new
        {
            strategyId = notification.StrategyId,
            symbol = notification.Symbol,
            reason = notification.Reason,
            barOpenTime = notification.BarOpenTime,
        });
        return _publisher.PublishAsync(
            SystemEventType.SignalSkipped,
            $"Sinyal atlandı: {notification.Symbol} ({notification.Reason})",
            details,
            cancellationToken);
    }

    private static void PruneStale(long currentMinuteBucket)
    {
        foreach (var entry in ThrottleKeys)
        {
            var parts = entry.Key.Split(':');
            if (parts.Length == 2
                && long.TryParse(parts[1], out var bucket)
                && currentMinuteBucket - bucket > 5)
            {
                ThrottleKeys.TryRemove(entry.Key, out _);
            }
        }
    }

    /// <summary>Test hook — stabil throttle davranışı için dictionary temizler.</summary>
    internal static void ResetThrottleForTests() => ThrottleKeys.Clear();
}
