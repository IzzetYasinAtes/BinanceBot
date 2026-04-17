using Ardalis.Result;
using MediatR;

namespace BinanceBot.Application.MarketData.Queries.GetKlines;

public sealed record GetKlinesQuery(
    string Symbol,
    string Interval,
    int Limit,
    DateTimeOffset? StartTime,
    DateTimeOffset? EndTime) : IRequest<Result<IReadOnlyList<KlineDto>>>;

public sealed record KlineDto(
    string Symbol,
    string Interval,
    DateTimeOffset OpenTime,
    DateTimeOffset CloseTime,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Volume,
    decimal QuoteVolume,
    int TradeCount,
    bool IsClosed);
