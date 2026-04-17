using BinanceBot.Domain.Orders;

namespace BinanceBot.Application.Orders.Queries;

internal static class OrderMapper
{
    public static OrderDto ToDto(Order order) => new(
        order.ClientOrderId,
        order.ExchangeOrderId,
        order.Symbol.Value,
        order.Side.ToString(),
        order.Type.ToString(),
        order.TimeInForce.ToString(),
        order.Quantity,
        order.Price,
        order.StopPrice,
        order.ExecutedQuantity,
        order.CumulativeQuoteQty,
        order.Status.ToString(),
        order.StrategyId,
        order.Mode.ToString(),
        order.CreatedAt,
        order.UpdatedAt);
}
