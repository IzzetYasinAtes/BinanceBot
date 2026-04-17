using BinanceBot.Domain.Common;

namespace BinanceBot.Domain.RiskProfiles.Events;

public sealed record RiskProfileUpdatedEvent(
    decimal RiskPerTradePct,
    decimal MaxPositionSizePct,
    decimal MaxDrawdown24hPct,
    decimal MaxDrawdownAllTimePct,
    int MaxConsecutiveLosses) : DomainEventBase;

public sealed record RiskCapsOverriddenEvent(
    decimal RiskPerTradeCap,
    decimal MaxPositionCap,
    string AdminNote) : DomainEventBase;

public sealed record CircuitBreakerTrippedEvent(
    string Reason,
    decimal ObservedDrawdownPct,
    DateTimeOffset TrippedAt) : DomainEventBase;

public sealed record CircuitBreakerResetEvent(
    string AdminNote,
    DateTimeOffset ResetAt) : DomainEventBase;

public sealed record TradeOutcomeRecordedEvent(
    decimal RealizedPnl,
    int ConsecutiveLosses) : DomainEventBase;
