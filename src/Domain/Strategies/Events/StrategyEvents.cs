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
    DateTimeOffset BarOpenTime,
    decimal? SuggestedStopPrice = null,
    // Loop 10 take-profit fix — ADR-0011 §11.4 sapma #4 pattern (default null,
    // backward compatible with older callers that don't set it).
    decimal? SuggestedTakeProfit = null) : DomainEventBase;
