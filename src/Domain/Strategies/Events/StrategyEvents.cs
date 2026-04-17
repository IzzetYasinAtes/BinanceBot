using BinanceBot.Domain.Common;

namespace BinanceBot.Domain.Strategies.Events;

public sealed record StrategyCreatedEvent(long StrategyId, string Name, StrategyType Type) : DomainEventBase;
public sealed record StrategyActivatedEvent(long StrategyId) : DomainEventBase;
public sealed record StrategyDeactivatedEvent(long StrategyId, string Reason) : DomainEventBase;
public sealed record StrategyParametersUpdatedEvent(long StrategyId) : DomainEventBase;
public sealed record StrategySignalEmittedEvent(
    long StrategyId,
    string Symbol,
    StrategySignalDirection Direction,
    DateTimeOffset BarOpenTime) : DomainEventBase;
