using BinanceBot.Domain.MarketData;

namespace BinanceBot.Application.Abstractions.Binance;

public interface IBinanceMarketData
{
    Task<IReadOnlyList<ExchangeInfoSymbolDto>> GetExchangeInfoAsync(
        IReadOnlyCollection<string> symbols,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<RestKlineDto>> GetKlinesAsync(
        string symbol,
        KlineInterval interval,
        int limit,
        DateTimeOffset? startTime,
        DateTimeOffset? endTime,
        CancellationToken cancellationToken);

    Task<OrderBookSnapshotDto> GetOrderBookSnapshotAsync(
        string symbol,
        int limit,
        CancellationToken cancellationToken);

    Task<BinanceServerTimeDto> GetServerTimeAsync(CancellationToken cancellationToken);

    Task PingAsync(CancellationToken cancellationToken);
}
