using BinanceBot.Domain.Common;
using BinanceBot.Domain.ValueObjects;

namespace BinanceBot.Domain.Strategies;

public sealed class StrategySignal : Entity<long>
{
    public long StrategyId { get; private set; }
    public Symbol Symbol { get; private set; } = default!;
    public DateTimeOffset BarOpenTime { get; private set; }
    public StrategySignalDirection Direction { get; private set; }
    public decimal SuggestedQuantity { get; private set; }
    public decimal? SuggestedPrice { get; private set; }
    public decimal? SuggestedStopPrice { get; private set; }
    public string ContextJson { get; private set; } = "{}";
    public DateTimeOffset EmittedAt { get; private set; }

    private StrategySignal() { }

    internal static StrategySignal Emit(
        Symbol symbol,
        DateTimeOffset barOpenTime,
        StrategySignalDirection direction,
        decimal quantity,
        decimal? price,
        decimal? stopPrice,
        string contextJson,
        DateTimeOffset emittedAt)
    {
        if (quantity <= 0m)
        {
            throw new DomainException("Suggested quantity must be positive.");
        }

        return new StrategySignal
        {
            Symbol = symbol,
            BarOpenTime = barOpenTime,
            Direction = direction,
            SuggestedQuantity = quantity,
            SuggestedPrice = price,
            SuggestedStopPrice = stopPrice,
            ContextJson = string.IsNullOrWhiteSpace(contextJson) ? "{}" : contextJson,
            EmittedAt = emittedAt,
        };
    }
}
