using BinanceBot.Domain.Common;
using BinanceBot.Domain.ValueObjects;

namespace BinanceBot.Domain.MarketData;

public sealed class OrderBookSnapshot : Entity<long>
{
    public Symbol Symbol { get; private set; } = default!;
    public long LastUpdateId { get; private set; }
    public string BidsJson { get; private set; } = "[]";
    public string AsksJson { get; private set; } = "[]";
    public DateTimeOffset CapturedAt { get; private set; }

    private OrderBookSnapshot() { }

    public static OrderBookSnapshot Capture(
        Symbol symbol,
        long lastUpdateId,
        string bidsJson,
        string asksJson,
        DateTimeOffset capturedAt)
    {
        if (lastUpdateId <= 0)
        {
            throw new DomainException("LastUpdateId must be positive.");
        }

        if (string.IsNullOrWhiteSpace(bidsJson) || string.IsNullOrWhiteSpace(asksJson))
        {
            throw new DomainException("Bids/Asks JSON cannot be empty.");
        }

        return new OrderBookSnapshot
        {
            Symbol = symbol,
            LastUpdateId = lastUpdateId,
            BidsJson = bidsJson,
            AsksJson = asksJson,
            CapturedAt = capturedAt,
        };
    }
}
