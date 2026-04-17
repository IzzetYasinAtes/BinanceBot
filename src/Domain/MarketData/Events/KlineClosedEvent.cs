using BinanceBot.Domain.Common;

namespace BinanceBot.Domain.MarketData.Events;

public sealed record KlineClosedEvent(
    string Symbol,
    KlineInterval Interval,
    DateTimeOffset OpenTime,
    DateTimeOffset CloseTime,
    decimal ClosePrice) : DomainEventBase;
