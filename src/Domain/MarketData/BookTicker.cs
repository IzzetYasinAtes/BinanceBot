using BinanceBot.Domain.Common;
using BinanceBot.Domain.ValueObjects;

namespace BinanceBot.Domain.MarketData;

public sealed class BookTicker : Entity<int>
{
    public Symbol Symbol { get; private set; } = default!;
    public decimal BidPrice { get; private set; }
    public decimal BidQuantity { get; private set; }
    public decimal AskPrice { get; private set; }
    public decimal AskQuantity { get; private set; }
    public long UpdateId { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private BookTicker() { }

    public static BookTicker Create(
        Symbol symbol,
        decimal bidPrice,
        decimal bidQty,
        decimal askPrice,
        decimal askQty,
        long updateId,
        DateTimeOffset updatedAt) =>
        new()
        {
            Symbol = symbol,
            BidPrice = bidPrice,
            BidQuantity = bidQty,
            AskPrice = askPrice,
            AskQuantity = askQty,
            UpdateId = updateId,
            UpdatedAt = updatedAt,
        };

    public void Apply(
        decimal bidPrice,
        decimal bidQty,
        decimal askPrice,
        decimal askQty,
        long updateId,
        DateTimeOffset updatedAt)
    {
        if (updateId < UpdateId)
        {
            return;
        }

        BidPrice = bidPrice;
        BidQuantity = bidQty;
        AskPrice = askPrice;
        AskQuantity = askQty;
        UpdateId = updateId;
        UpdatedAt = updatedAt;
    }
}
