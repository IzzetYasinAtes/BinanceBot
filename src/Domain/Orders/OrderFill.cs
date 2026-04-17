using BinanceBot.Domain.Common;

namespace BinanceBot.Domain.Orders;

public sealed class OrderFill : Entity<long>
{
    public long OrderId { get; private set; }
    public long ExchangeTradeId { get; private set; }
    public decimal Price { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal Commission { get; private set; }
    public string CommissionAsset { get; private set; } = "USDT";
    public DateTimeOffset FilledAt { get; private set; }

    private OrderFill() { }

    internal static OrderFill Create(
        long exchangeTradeId,
        decimal price,
        decimal quantity,
        decimal commission,
        string commissionAsset,
        DateTimeOffset filledAt)
    {
        if (exchangeTradeId <= 0)
        {
            throw new DomainException("ExchangeTradeId must be positive.");
        }
        if (price <= 0m)
        {
            throw new DomainException("Fill price must be positive.");
        }
        if (quantity <= 0m)
        {
            throw new DomainException("Fill quantity must be positive.");
        }

        return new OrderFill
        {
            ExchangeTradeId = exchangeTradeId,
            Price = price,
            Quantity = quantity,
            Commission = commission,
            CommissionAsset = string.IsNullOrWhiteSpace(commissionAsset)
                ? "USDT"
                : commissionAsset.ToUpperInvariant(),
            FilledAt = filledAt,
        };
    }
}
