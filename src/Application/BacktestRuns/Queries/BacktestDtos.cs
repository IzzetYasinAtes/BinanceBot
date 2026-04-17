namespace BinanceBot.Application.BacktestRuns.Queries;

public sealed record BacktestRunSummaryDto(
    long Id,
    long StrategyId,
    string Status,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    decimal InitialBalance,
    decimal FinalBalance,
    decimal Sharpe,
    decimal MaxDrawdownPct,
    decimal WinRate,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt);

public sealed record BacktestResultDto(
    long Id,
    long StrategyId,
    string Status,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    decimal InitialBalance,
    decimal FinalBalance,
    decimal Sharpe,
    decimal MaxDrawdownPct,
    decimal WinRate,
    int TradeCount,
    string? FailureReason,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt);
