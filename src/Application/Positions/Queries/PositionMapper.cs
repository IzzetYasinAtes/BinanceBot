using BinanceBot.Domain.Positions;

namespace BinanceBot.Application.Positions.Queries;

internal static class PositionMapper
{
    // Loop 106 — KOMİSYON kolonu fix: Domain'deki EntryCommission + ExitCommission
    // alanları DTO'ya geçirilmiyordu; frontend `commissionPaid ?? commission ?? 0`
    // arıyordu, ikisi de yoktu, $0.0000 görünüyordu. Aynı güncellemede StopPrice
    // ve TakeProfit alanları da DTO'ya eklendi (positions tablosunda TP/SL kolonları
    // hep "—" çıkıyordu).
    public static PositionDto ToDto(Position p) => new(
        p.Id,
        p.Symbol.Value,
        p.Direction.ToString(),
        p.Status.ToString(),
        p.Quantity,
        p.AverageEntryPrice,
        p.ExitPrice,
        p.MarkPrice,
        p.StopPrice,
        p.TakeProfit,
        p.UnrealizedPnl,
        p.RealizedPnl,
        p.EntryCommission,
        p.ExitCommission,
        p.EntryCommission + p.ExitCommission,
        p.StrategyId,
        p.OpenedAt,
        p.ClosedAt,
        p.UpdatedAt);
}
