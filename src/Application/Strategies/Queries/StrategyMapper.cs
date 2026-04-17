using BinanceBot.Domain.Strategies;

namespace BinanceBot.Application.Strategies.Queries;

internal static class StrategyMapper
{
    public static StrategyDto ToDto(Strategy s) => new(
        s.Id,
        s.Name,
        s.Type.ToString(),
        s.Status.ToString(),
        s.SymbolsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries),
        s.CreatedAt,
        s.UpdatedAt,
        s.ActivatedAt);

    public static StrategyDetailDto ToDetailDto(Strategy s, int signalCountLast24h) => new(
        s.Id,
        s.Name,
        s.Type.ToString(),
        s.Status.ToString(),
        s.SymbolsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries),
        s.ParametersJson,
        s.CreatedAt,
        s.UpdatedAt,
        s.ActivatedAt,
        signalCountLast24h);

    public static StrategySignalDto ToSignalDto(StrategySignal s) => new(
        s.Id,
        s.StrategyId,
        s.Symbol.Value,
        s.BarOpenTime,
        s.Direction.ToString(),
        s.SuggestedQuantity,
        s.SuggestedPrice,
        s.SuggestedStopPrice,
        s.EmittedAt);
}
