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

    /// <summary>
    /// Fetch 24h rolling-window ticker for a batch of symbols (ADR-0012 §12.1).
    /// Single REST call against <c>/api/v3/ticker/24hr?symbols=[...]</c>; weight=2 per symbol,
    /// well below the 6000/min cap for our 3-symbol UI poll loop.
    /// </summary>
    Task<IReadOnlyList<Ticker24hDto>> GetTicker24hAsync(
        IReadOnlyCollection<string> symbols,
        CancellationToken cancellationToken);
}
