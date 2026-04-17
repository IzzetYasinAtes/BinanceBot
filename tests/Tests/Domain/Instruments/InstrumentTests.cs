using BinanceBot.Domain.Common;
using BinanceBot.Domain.Instruments;
using BinanceBot.Domain.Instruments.Events;
using BinanceBot.Domain.ValueObjects;
using FluentAssertions;

namespace BinanceBot.Tests.Domain.Instruments;

public class InstrumentTests
{
    [Fact]
    public void Create_WithValidInputs_RaisesRegisteredEvent()
    {
        var symbol = Symbol.From("BTCUSDT");

        var instrument = Instrument.Create(
            symbol, "BTC", "USDT", InstrumentStatus.Trading,
            tickSize: 0.01m, stepSize: 0.00001m,
            minNotional: 10m, minQty: 0.00001m, maxQty: 9000m,
            syncedAt: DateTimeOffset.UtcNow);

        instrument.Symbol.Should().Be(symbol);
        instrument.Status.Should().Be(InstrumentStatus.Trading);
        instrument.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<InstrumentRegisteredEvent>();
    }

    [Fact]
    public void Create_WithZeroTickSize_Throws()
    {
        var act = () => Instrument.Create(
            Symbol.From("BTCUSDT"), "BTC", "USDT", InstrumentStatus.Trading,
            tickSize: 0m, stepSize: 0.001m,
            minNotional: 10m, minQty: 0.001m, maxQty: 100m,
            syncedAt: DateTimeOffset.UtcNow);

        act.Should().Throw<DomainException>()
            .WithMessage("TickSize*");
    }

    [Fact]
    public void UpdateFilters_WhenChanged_RaisesFilterChangedEvent()
    {
        var instrument = Instrument.Create(
            Symbol.From("BTCUSDT"), "BTC", "USDT", InstrumentStatus.Trading,
            tickSize: 0.01m, stepSize: 0.001m,
            minNotional: 10m, minQty: 0.001m, maxQty: 100m,
            syncedAt: DateTimeOffset.UtcNow);
        instrument.ClearDomainEvents();

        instrument.UpdateFilters(
            tickSize: 0.02m, stepSize: 0.001m,
            minNotional: 10m, minQty: 0.001m, maxQty: 100m,
            status: InstrumentStatus.Trading,
            syncedAt: DateTimeOffset.UtcNow);

        instrument.TickSize.Should().Be(0.02m);
        instrument.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<InstrumentFiltersChangedEvent>();
    }

    [Fact]
    public void UpdateFilters_WhenNothingChanged_DoesNotRaiseEvent()
    {
        var instrument = Instrument.Create(
            Symbol.From("BTCUSDT"), "BTC", "USDT", InstrumentStatus.Trading,
            tickSize: 0.01m, stepSize: 0.001m,
            minNotional: 10m, minQty: 0.001m, maxQty: 100m,
            syncedAt: DateTimeOffset.UtcNow);
        instrument.ClearDomainEvents();

        instrument.UpdateFilters(
            tickSize: 0.01m, stepSize: 0.001m,
            minNotional: 10m, minQty: 0.001m, maxQty: 100m,
            status: InstrumentStatus.Trading,
            syncedAt: DateTimeOffset.UtcNow.AddMinutes(5));

        instrument.DomainEvents.Should().BeEmpty();
    }
}
