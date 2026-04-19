using BinanceBot.Domain.Common;

namespace BinanceBot.Domain.SystemEvents.Events;

/// <summary>
/// ADR-0016 §16.9.6 — application boot event. Published once from the host
/// composition root after DB migrations have run and the service container is
/// ready. Drives the <c>SystemEventType.Startup</c> row.
/// </summary>
public sealed record AppStartedEvent(
    string HostName,
    string Environment) : DomainEventBase;

/// <summary>
/// ADR-0016 §16.9.6 — application shutdown event. Published from
/// <c>IHostApplicationLifetime.ApplicationStopping</c> so a final row lands in
/// <c>SystemEvents</c> before the host drains.
/// </summary>
public sealed record AppStoppingEvent(
    string HostName) : DomainEventBase;

/// <summary>
/// ADR-0016 §16.9.6 — emitted by <c>MarketIndicatorService</c> when both the 1m
/// and 1h rolling buffers have crossed their warmup budgets per symbol. Drives
/// the <c>SystemEventType.WarmupCompleted</c> row; one event per symbol.
/// </summary>
public sealed record IndicatorWarmupCompletedEvent(
    string Symbol,
    int OneMinuteBarCount,
    int OneHourBarCount) : DomainEventBase;

/// <summary>
/// ADR-0016 §16.9.6 — emitted when an evaluator skips a signal for a throttle /
/// cooldown / gate-false reason. Downstream <see cref="Events.IndicatorWarmupCompletedEvent"/>
/// peer handler throttles per-(strategy, minute) so the <c>SystemEvents</c>
/// stream does not flood on hot-path skip loops (per-bar max 1).
/// </summary>
public sealed record StrategySignalSkippedEvent(
    long StrategyId,
    string Symbol,
    string Reason,
    DateTimeOffset BarOpenTime) : DomainEventBase;

/// <summary>
/// ADR-0016 §16.9.6 — emitted by <c>BinanceWsSupervisor</c> whenever the WS
/// supervisor state machine transitions. Drives the <c>WsStateChanged</c> row.
/// </summary>
public sealed record WsConnectionStateChangedEvent(
    string FromState,
    string ToState) : DomainEventBase;
