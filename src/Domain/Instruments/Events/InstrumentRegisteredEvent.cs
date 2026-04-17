using BinanceBot.Domain.Common;

namespace BinanceBot.Domain.Instruments.Events;

public sealed record InstrumentRegisteredEvent(string Symbol) : DomainEventBase;
