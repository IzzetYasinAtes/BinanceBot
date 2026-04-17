using BinanceBot.Domain.Common;
using BinanceBot.Domain.Instruments.Events;
using BinanceBot.Domain.ValueObjects;

namespace BinanceBot.Domain.Instruments;

public sealed class Instrument : AggregateRoot<int>
{
    public Symbol Symbol { get; private set; } = default!;
    public string BaseAsset { get; private set; } = default!;
    public string QuoteAsset { get; private set; } = default!;
    public InstrumentStatus Status { get; private set; }
    public decimal TickSize { get; private set; }
    public decimal StepSize { get; private set; }
    public decimal MinNotional { get; private set; }
    public decimal MinQty { get; private set; }
    public decimal MaxQty { get; private set; }
    public DateTimeOffset LastSyncedAt { get; private set; }

    private Instrument() { }

    public static Instrument Create(
        Symbol symbol,
        string baseAsset,
        string quoteAsset,
        InstrumentStatus status,
        decimal tickSize,
        decimal stepSize,
        decimal minNotional,
        decimal minQty,
        decimal maxQty,
        DateTimeOffset syncedAt)
    {
        if (string.IsNullOrWhiteSpace(baseAsset) || string.IsNullOrWhiteSpace(quoteAsset))
        {
            throw new DomainException("BaseAsset and QuoteAsset are required.");
        }

        if (tickSize <= 0m || stepSize <= 0m)
        {
            throw new DomainException("TickSize and StepSize must be positive.");
        }

        if (minQty <= 0m || maxQty < minQty)
        {
            throw new DomainException("MinQty must be positive and MaxQty >= MinQty.");
        }

        var instrument = new Instrument
        {
            Symbol = symbol,
            BaseAsset = baseAsset.ToUpperInvariant(),
            QuoteAsset = quoteAsset.ToUpperInvariant(),
            Status = status,
            TickSize = tickSize,
            StepSize = stepSize,
            MinNotional = minNotional,
            MinQty = minQty,
            MaxQty = maxQty,
            LastSyncedAt = syncedAt,
        };

        instrument.RaiseDomainEvent(new InstrumentRegisteredEvent(symbol.Value));
        return instrument;
    }

    public void UpdateFilters(
        decimal tickSize,
        decimal stepSize,
        decimal minNotional,
        decimal minQty,
        decimal maxQty,
        InstrumentStatus status,
        DateTimeOffset syncedAt)
    {
        var changed = TickSize != tickSize
            || StepSize != stepSize
            || MinNotional != minNotional
            || MinQty != minQty
            || MaxQty != maxQty
            || Status != status;

        TickSize = tickSize;
        StepSize = stepSize;
        MinNotional = minNotional;
        MinQty = minQty;
        MaxQty = maxQty;
        Status = status;
        LastSyncedAt = syncedAt;

        if (changed)
        {
            RaiseDomainEvent(new InstrumentFiltersChangedEvent(Symbol.Value));
        }
    }
}
