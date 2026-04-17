namespace BinanceBot.Application.Strategies.Queries;

public sealed record StrategyDto(
    long Id,
    string Name,
    string Type,
    string Status,
    string[] Symbols,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ActivatedAt);

public sealed record StrategyDetailDto(
    long Id,
    string Name,
    string Type,
    string Status,
    string[] Symbols,
    string ParametersJson,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ActivatedAt,
    int SignalCountLast24h);

public sealed record StrategySignalDto(
    long Id,
    long StrategyId,
    string Symbol,
    DateTimeOffset BarOpenTime,
    string Direction,
    decimal SuggestedQuantity,
    decimal? SuggestedPrice,
    decimal? SuggestedStopPrice,
    DateTimeOffset EmittedAt);
