using BinanceBot.Application.Abstractions.Binance;
using BinanceBot.Domain.MarketData;
using BinanceBot.Infrastructure.Binance.Streams;
using FluentAssertions;

namespace BinanceBot.Tests.Infrastructure.Binance;

/// <summary>
/// Loop 23 blocker fix (BLOCKER-2). Verifies the fan-out broadcast contract:
/// every subscriber channel must receive every published payload. The previous
/// single-channel design silently raced — one consumer won each envelope.
/// </summary>
public sealed class BinanceStreamBusTests
{
    private static WsKlinePayload MakeKline(string symbol, DateTimeOffset openTime) =>
        new(
            Symbol: symbol,
            Interval: KlineInterval.ThirtySeconds,
            OpenTime: openTime,
            CloseTime: openTime.AddSeconds(30),
            Open: 100m,
            High: 101m,
            Low: 99m,
            Close: 100.5m,
            Volume: 10m,
            QuoteVolume: 1000m,
            TradeCount: 5,
            TakerBuyBase: 5m,
            TakerBuyQuote: 500m,
            IsClosed: true);

    [Fact]
    public async Task PublishKline_TwoSubscribers_BothReceiveEveryPayload()
    {
        // Arrange
        var bus = new BinanceStreamBus();
        var readerA = bus.SubscribeKlines();
        var readerB = bus.SubscribeKlines();

        var baseTime = new DateTimeOffset(2026, 4, 19, 12, 0, 0, TimeSpan.Zero);
        var bar1 = MakeKline("BTCUSDT", baseTime);
        var bar2 = MakeKline("ETHUSDT", baseTime.AddSeconds(30));

        // Act
        bus.PublishKline(bar1).Should().BeTrue();
        bus.PublishKline(bar2).Should().BeTrue();

        // Assert — each subscriber drains both envelopes independently.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var a1 = await readerA.ReadAsync(cts.Token);
        var a2 = await readerA.ReadAsync(cts.Token);
        var b1 = await readerB.ReadAsync(cts.Token);
        var b2 = await readerB.ReadAsync(cts.Token);

        a1.Should().Be(bar1);
        a2.Should().Be(bar2);
        b1.Should().Be(bar1);
        b2.Should().Be(bar2);
    }

    [Fact]
    public void PublishKline_NoSubscribers_ReturnsFalse()
    {
        var bus = new BinanceStreamBus();
        var bar = MakeKline("BTCUSDT", DateTimeOffset.UtcNow);

        bus.PublishKline(bar).Should().BeFalse();
    }

    [Fact]
    public async Task PublishBookTicker_FanOutAcrossSubscribers()
    {
        var bus = new BinanceStreamBus();
        var r1 = bus.SubscribeBookTickers();
        var r2 = bus.SubscribeBookTickers();

        var now = DateTimeOffset.UtcNow;
        var payload = new WsBookTickerPayload(
            Symbol: "BTCUSDT",
            UpdateId: 42,
            BidPrice: 100m,
            BidQty: 1m,
            AskPrice: 101m,
            AskQty: 1m,
            ReceivedAt: now);

        bus.PublishBookTicker(payload).Should().BeTrue();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        (await r1.ReadAsync(cts.Token)).Should().Be(payload);
        (await r2.ReadAsync(cts.Token)).Should().Be(payload);
    }

    [Fact]
    public async Task PublishDepth_FanOutAcrossSubscribers()
    {
        var bus = new BinanceStreamBus();
        var r1 = bus.SubscribeDepth();
        var r2 = bus.SubscribeDepth();

        var now = DateTimeOffset.UtcNow;
        var payload = new WsDepthDiffPayload(
            Symbol: "BTCUSDT",
            FirstUpdateId: 1,
            FinalUpdateId: 2,
            PreviousFinalUpdateId: null,
            BidUpdates: Array.Empty<OrderBookLevelDto>(),
            AskUpdates: Array.Empty<OrderBookLevelDto>(),
            ReceivedAt: now);

        bus.PublishDepth(payload).Should().BeTrue();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        (await r1.ReadAsync(cts.Token)).Should().Be(payload);
        (await r2.ReadAsync(cts.Token)).Should().Be(payload);
    }
}
