namespace BinanceBot.Application.Abstractions.Binance;

public interface IBinanceMarketStream
{
    IAsyncEnumerable<WsKlinePayload> KlineUpdates(CancellationToken cancellationToken);
    IAsyncEnumerable<WsBookTickerPayload> BookTickerUpdates(CancellationToken cancellationToken);
    IAsyncEnumerable<WsDepthDiffPayload> DepthUpdates(CancellationToken cancellationToken);
}
