using BinanceBot.Domain.Positions;

namespace BinanceBot.Application.Positions.Queries;

internal static class PositionMapper
{
    public static PositionDto ToDto(Position p) => new(
        p.Id,
        p.Symbol.Value,
        p.Side.ToString(),
        p.Status.ToString(),
        p.Quantity,
        p.AverageEntryPrice,
        p.ExitPrice,
        p.MarkPrice,
        p.UnrealizedPnl,
        p.RealizedPnl,
        p.StrategyId,
        p.OpenedAt,
        p.ClosedAt,
        p.UpdatedAt);
}
