using System.Threading.Channels;
using BinanceBot.Application.Abstractions.Binance;

namespace BinanceBot.Infrastructure.Binance.Streams;

/// <summary>
/// Loop 23 blocker fix (BLOCKER-2): fan-out broadcast pattern so that every
/// consumer of a stream receives every payload. The previous single-channel
/// design (SingleReader=false) race'di — iki consumer'dan yalnızca biri
/// her kline/book-ticker/depth'i okuyordu ve diğerine ulaşmıyordu.
///
/// Yazım tarafı (<see cref="BinanceWsSupervisor"/>) <c>Publish*</c> çağırır;
/// her publish, aktif subscriber listesinin snapshot'ı alınarak her bir
/// <see cref="Channel{T}"/>'e <c>TryWrite</c> ile yazılır (DropOldest policy,
/// bounded 1024/2048/4096). Okuyucular <c>Subscribe*</c> ile kendi
/// <see cref="ChannelReader{T}"/>'larını alır. Her consumer izole.
///
/// Thread-safety: subscriber listesi değişiklikleri <c>lock</c> altında;
/// publish snapshot kopyası alır, lock dışında yazar (back-pressure yok —
/// bounded channel + DropOldest).
/// </summary>
public sealed class BinanceStreamBus : IBinanceMarketStream
{
    private readonly Broadcaster<WsKlinePayload> _klines = new(capacity: 1024);
    private readonly Broadcaster<WsBookTickerPayload> _bookTickers = new(capacity: 2048);
    private readonly Broadcaster<WsDepthDiffPayload> _depth = new(capacity: 4096);

    // -- Publish side (used by BinanceWsSupervisor) ---------------------------

    /// <summary>
    /// Supervisor publishes a kline envelope to every subscriber.
    /// Returns true if at least one subscriber existed.
    /// </summary>
    internal bool PublishKline(WsKlinePayload payload) => _klines.Publish(payload);

    internal bool PublishBookTicker(WsBookTickerPayload payload) => _bookTickers.Publish(payload);

    internal bool PublishDepth(WsDepthDiffPayload payload) => _depth.Publish(payload);

    // -- Subscribe side (used by ingestion workers + MarketIndicatorService) --

    /// <summary>
    /// Creates a new bounded (DropOldest) channel and registers it as a
    /// kline subscriber. Caller reads from the returned <see cref="ChannelReader{T}"/>.
    /// </summary>
    public ChannelReader<WsKlinePayload> SubscribeKlines() => _klines.Subscribe();

    public ChannelReader<WsBookTickerPayload> SubscribeBookTickers() => _bookTickers.Subscribe();

    public ChannelReader<WsDepthDiffPayload> SubscribeDepth() => _depth.Subscribe();

    // -- IBinanceMarketStream (backwards-compatible IAsyncEnumerable API) -----

    public IAsyncEnumerable<WsKlinePayload> KlineUpdates(CancellationToken cancellationToken) =>
        SubscribeKlines().ReadAllAsync(cancellationToken);

    public IAsyncEnumerable<WsBookTickerPayload> BookTickerUpdates(CancellationToken cancellationToken) =>
        SubscribeBookTickers().ReadAllAsync(cancellationToken);

    public IAsyncEnumerable<WsDepthDiffPayload> DepthUpdates(CancellationToken cancellationToken) =>
        SubscribeDepth().ReadAllAsync(cancellationToken);

    /// <summary>
    /// Minimal fan-out broadcaster. Each <c>Subscribe</c> yields a fresh
    /// bounded channel. <c>Publish</c> writes to every current subscriber.
    /// </summary>
    private sealed class Broadcaster<T>
    {
        private readonly int _capacity;
        private readonly List<Channel<T>> _subscribers = new();
        private readonly object _lock = new();

        public Broadcaster(int capacity)
        {
            _capacity = capacity;
        }

        public ChannelReader<T> Subscribe()
        {
            var channel = Channel.CreateBounded<T>(new BoundedChannelOptions(_capacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleWriter = true,
                SingleReader = true,
            });

            lock (_lock)
            {
                _subscribers.Add(channel);
            }

            return channel.Reader;
        }

        public bool Publish(T item)
        {
            // Snapshot under lock, write outside — publish must not block on a
            // slow consumer (DropOldest drops rather than blocks anyway).
            Channel<T>[] snapshot;
            lock (_lock)
            {
                if (_subscribers.Count == 0)
                {
                    return false;
                }
                snapshot = _subscribers.ToArray();
            }

            foreach (var channel in snapshot)
            {
                channel.Writer.TryWrite(item);
            }
            return true;
        }
    }
}
