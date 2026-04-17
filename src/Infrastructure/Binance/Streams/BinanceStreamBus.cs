using System.Threading.Channels;
using BinanceBot.Application.Abstractions.Binance;

namespace BinanceBot.Infrastructure.Binance.Streams;

public sealed class BinanceStreamBus : IBinanceMarketStream
{
    private readonly Channel<WsKlinePayload> _klines = Channel.CreateBounded<WsKlinePayload>(
        new BoundedChannelOptions(1024)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleWriter = true,
            SingleReader = false,
        });

    private readonly Channel<WsBookTickerPayload> _bookTickers = Channel.CreateBounded<WsBookTickerPayload>(
        new BoundedChannelOptions(2048)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleWriter = true,
            SingleReader = false,
        });

    private readonly Channel<WsDepthDiffPayload> _depth = Channel.CreateBounded<WsDepthDiffPayload>(
        new BoundedChannelOptions(4096)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleWriter = true,
            SingleReader = false,
        });

    public ChannelWriter<WsKlinePayload> KlineWriter => _klines.Writer;
    public ChannelWriter<WsBookTickerPayload> BookTickerWriter => _bookTickers.Writer;
    public ChannelWriter<WsDepthDiffPayload> DepthWriter => _depth.Writer;

    public IAsyncEnumerable<WsKlinePayload> KlineUpdates(CancellationToken cancellationToken) =>
        _klines.Reader.ReadAllAsync(cancellationToken);

    public IAsyncEnumerable<WsBookTickerPayload> BookTickerUpdates(CancellationToken cancellationToken) =>
        _bookTickers.Reader.ReadAllAsync(cancellationToken);

    public IAsyncEnumerable<WsDepthDiffPayload> DepthUpdates(CancellationToken cancellationToken) =>
        _depth.Reader.ReadAllAsync(cancellationToken);
}
