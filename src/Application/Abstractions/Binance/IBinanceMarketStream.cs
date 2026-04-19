using System.Threading.Channels;

namespace BinanceBot.Application.Abstractions.Binance;

/// <summary>
/// Binance WS fan-out bus. Loop 23 blocker fix (BLOCKER-2): each consumer must
/// get every payload, so <c>Subscribe*</c> returns a fresh <see cref="ChannelReader{T}"/>
/// backed by the consumer's own bounded channel. The legacy <c>*Updates</c> helpers
/// are thin <c>ReadAllAsync</c> wrappers around the same subscribe path.
/// </summary>
public interface IBinanceMarketStream
{
    /// <summary>
    /// Allocates a new kline subscriber channel and returns its reader.
    /// Supervisor-side publishes fan-out to every subscriber.
    /// </summary>
    ChannelReader<WsKlinePayload> SubscribeKlines();

    ChannelReader<WsBookTickerPayload> SubscribeBookTickers();

    ChannelReader<WsDepthDiffPayload> SubscribeDepth();

    IAsyncEnumerable<WsKlinePayload> KlineUpdates(CancellationToken cancellationToken);
    IAsyncEnumerable<WsBookTickerPayload> BookTickerUpdates(CancellationToken cancellationToken);
    IAsyncEnumerable<WsDepthDiffPayload> DepthUpdates(CancellationToken cancellationToken);
}
