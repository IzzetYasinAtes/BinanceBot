using BinanceBot.Domain.Common;

namespace BinanceBot.Domain.MarketData.Events;

public sealed record KlineIngestedEvent(
    string Symbol,
    KlineInterval Interval,
    DateTimeOffset OpenTime,
    bool IsClosed) : DomainEventBase;
