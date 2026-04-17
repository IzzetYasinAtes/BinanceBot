using BinanceBot.Domain.Common;

namespace BinanceBot.Domain.Balances.Events;

public sealed record VirtualBalanceResetEvent(
    TradingMode Mode,
    Guid IterationId,
    decimal StartingBalance,
    DateTimeOffset StartedAt) : DomainEventBase;

public sealed record VirtualBalanceUpdatedEvent(
    TradingMode Mode,
    decimal CurrentBalance,
    decimal Equity) : DomainEventBase;

public sealed record VirtualBalanceFillAppliedEvent(
    TradingMode Mode,
    decimal RealizedDelta,
    decimal CurrentBalance,
    DateTimeOffset AppliedAt) : DomainEventBase;
