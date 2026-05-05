namespace BinanceBot.Application.Positions.Queries;

public sealed record PositionDto(
    long Id,
    string Symbol,
    string Direction,
    string Status,
    decimal Quantity,
    decimal AverageEntryPrice,
    decimal? ExitPrice,
    decimal MarkPrice,
    decimal? StopPrice,
    decimal? TakeProfit,
    decimal UnrealizedPnl,
    decimal RealizedPnl,
    decimal EntryCommission,
    decimal ExitCommission,
    decimal TotalCommission,
    long? StrategyId,
    DateTimeOffset OpenedAt,
    DateTimeOffset? ClosedAt,
    DateTimeOffset UpdatedAt);

public sealed record PositionPnlDto(
    string Symbol,
    decimal UnrealizedPnl,
    decimal RealizedPnl,
    decimal MarkPrice,
    decimal AverageEntryPrice);

public sealed record TodayPnlDto(
    decimal RealizedToday,
    decimal UnrealizedTotal,
    int OpenPositionCount);
