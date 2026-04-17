using BinanceBot.Domain.Common;

namespace BinanceBot.Domain.BacktestRuns.Events;

public sealed record BacktestStartedEvent(long BacktestRunId, long StrategyId) : DomainEventBase;
public sealed record BacktestCompletedEvent(
    long BacktestRunId,
    decimal FinalBalance,
    decimal Sharpe,
    decimal MaxDrawdownPct,
    int TradeCount) : DomainEventBase;
public sealed record BacktestFailedEvent(long BacktestRunId, string Reason) : DomainEventBase;
