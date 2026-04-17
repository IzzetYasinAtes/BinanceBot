using BinanceBot.Domain.Common;
using BinanceBot.Domain.Orders;

namespace BinanceBot.Domain.BacktestRuns;

public sealed class BacktestTrade : Entity<long>
{
    public long BacktestRunId { get; private set; }
    public int SequenceNo { get; private set; }
    public OrderSide Side { get; private set; }
    public decimal Price { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal Pnl { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }

    private BacktestTrade() { }

    internal static BacktestTrade Record(
        int sequenceNo,
        OrderSide side,
        decimal price,
        decimal quantity,
        decimal pnl,
        DateTimeOffset occurredAt)
    {
        if (sequenceNo < 0)
        {
            throw new DomainException("SequenceNo must be non-negative.");
        }
        if (price <= 0m || quantity <= 0m)
        {
            throw new DomainException("Price and quantity must be positive.");
        }

        return new BacktestTrade
        {
            SequenceNo = sequenceNo,
            Side = side,
            Price = price,
            Quantity = quantity,
            Pnl = pnl,
            OccurredAt = occurredAt,
        };
    }
}
