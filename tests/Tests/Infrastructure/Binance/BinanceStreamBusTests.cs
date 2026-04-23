using System.Text;
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

    /// <summary>
    /// Loop 24 runtime bug: Binance SPOT WS does not support @kline_30s. Even so,
    /// our local parser must still parse a raw 30s kline payload correctly — the
    /// stream-side dispatcher must not silently drop it if the server ever routes
    /// one through (e.g. a futures endpoint swap, test fixture, or mocked frame).
    /// </summary>
    [Fact]
    public async Task PublishKline_30sPayload_ParsedAndBroadcastToAllSubscribers()
    {
        // Combined-stream envelope raw JSON matching Binance doc layout.
        var rawJson =
            "{\"stream\":\"btcusdt@kline_30s\",\"data\":{\"e\":\"kline\",\"E\":1745000000000," +
            "\"s\":\"BTCUSDT\",\"k\":{\"t\":1744999980000,\"T\":1745000009999,\"s\":\"BTCUSDT\"," +
            "\"i\":\"30s\",\"f\":1,\"L\":10,\"o\":\"100.0\",\"c\":\"100.5\",\"h\":\"101.0\"," +
            "\"l\":\"99.5\",\"v\":\"10.0\",\"n\":5,\"x\":true,\"q\":\"1000.0\",\"V\":\"5.0\"," +
            "\"Q\":\"500.0\",\"B\":\"0\"}}}";
        var rawBytes = Encoding.UTF8.GetBytes(rawJson);

        BinanceStreamParser.TryParseCombinedEnvelope(rawBytes, out var streamName, out var data)
            .Should().BeTrue();
        streamName.Should().Be("btcusdt@kline_30s");

        BinanceStreamParser.TryParseKline(data, DateTimeOffset.UtcNow, out var kline)
            .Should().BeTrue();
        kline.Interval.Should().Be(KlineInterval.ThirtySeconds);
        kline.IsClosed.Should().BeTrue();

        // Fan-out broadcast — two subscribers both get the 30s kline envelope.
        var bus = new BinanceStreamBus();
        var r1 = bus.SubscribeKlines();
        var r2 = bus.SubscribeKlines();
        bus.PublishKline(kline).Should().BeTrue();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        (await r1.ReadAsync(cts.Token)).Interval.Should().Be(KlineInterval.ThirtySeconds);
        (await r2.ReadAsync(cts.Token)).Interval.Should().Be(KlineInterval.ThirtySeconds);
    }

    /// <summary>
    /// Loop 24 — unsupported interval codes must not throw through the dispatcher.
    /// Previously <see cref="KlineIntervalExtensions.FromBinanceCode"/> threw
    /// ArgumentOutOfRangeException which bubbled into a swallowed WS frame
    /// dispatch error. Parser now returns <c>false</c> and the supervisor logs it.
    /// </summary>
    [Fact]
    public void TryParseKline_UnknownIntervalCode_ReturnsFalseWithoutThrowing()
    {
        var rawJson =
            "{\"stream\":\"btcusdt@kline_foo\",\"data\":{\"e\":\"kline\",\"E\":1,\"s\":\"BTCUSDT\"," +
            "\"k\":{\"t\":1,\"T\":2,\"s\":\"BTCUSDT\",\"i\":\"foo\",\"f\":1,\"L\":2,\"o\":\"1\"," +
            "\"c\":\"1\",\"h\":\"1\",\"l\":\"1\",\"v\":\"1\",\"n\":1,\"x\":true,\"q\":\"1\"," +
            "\"V\":\"1\",\"Q\":\"1\",\"B\":\"0\"}}}";
        var raw = Encoding.UTF8.GetBytes(rawJson);

        BinanceStreamParser.TryParseCombinedEnvelope(raw, out _, out var data).Should().BeTrue();

        var parsed = BinanceStreamParser.TryParseKline(data, DateTimeOffset.UtcNow, out var kline);
        parsed.Should().BeFalse();
        kline.Should().BeNull();
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
