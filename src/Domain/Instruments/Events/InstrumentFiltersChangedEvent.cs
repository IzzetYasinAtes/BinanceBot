using BinanceBot.Domain.Common;

namespace BinanceBot.Domain.Instruments.Events;

public sealed record InstrumentFiltersChangedEvent(string Symbol) : DomainEventBase;
