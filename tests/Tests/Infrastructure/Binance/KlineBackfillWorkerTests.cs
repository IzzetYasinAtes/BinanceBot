using BinanceBot.Application.Abstractions;
using BinanceBot.Application.Abstractions.Binance;
using BinanceBot.Domain.MarketData;
using BinanceBot.Domain.ValueObjects;
using BinanceBot.Infrastructure.Binance;
using BinanceBot.Infrastructure.Binance.Workers;
using BinanceBot.Infrastructure.Persistence;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace BinanceBot.Tests.Infrastructure.Binance;

public sealed class KlineBackfillWorkerTests
{
    // Compressed timings so the timeout test does not block CI for 10s.
    private static readonly TimeSpan FastBudget = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan FastPoll = TimeSpan.FromMilliseconds(15);

    private static readonly DateTimeOffset NowUtc =
        new(2026, 4, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task BackfillDisabled_ShortCircuits_NoMarketDataCall()
    {
        var marketData = new Mock<IBinanceMarketData>(MockBehavior.Strict);
        var probe = new StubProbe(ready: true);

        await using var harness = TestHarness.Build(
            options: BuildOptions(enabled: false, symbols: new[] { "BTCUSDT" }),
            marketData: marketData.Object,
            probe: probe);

        await harness.Worker.RunOnceAsync(CancellationToken.None);

        marketData.VerifyNoOtherCalls();
        (await harness.Db.Klines.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task AllSuccess_PersistsEveryBar_ForEverySymbolIntervalPair()
    {
        var marketData = new Mock<IBinanceMarketData>(MockBehavior.Strict);
        marketData
            .Setup(m => m.GetKlinesAsync(
                It.IsAny<string>(), KlineInterval.OneMinute, 1000,
                null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string sym, KlineInterval _, int __, DateTimeOffset? ___,
                           DateTimeOffset? ____, CancellationToken _____) => BuildBars(sym, count: 3));

        var probe = new StubProbe(ready: true);

        await using var harness = TestHarness.Build(
            options: BuildOptions(enabled: true, symbols: new[] { "BTCUSDT", "ETHUSDT" }),
            marketData: marketData.Object,
            probe: probe);

        await harness.Worker.RunOnceAsync(CancellationToken.None);

        marketData.Verify(m => m.GetKlinesAsync(
            "BTCUSDT", KlineInterval.OneMinute, 1000, null, null, It.IsAny<CancellationToken>()),
            Times.Once);
        marketData.Verify(m => m.GetKlinesAsync(
            "ETHUSDT", KlineInterval.OneMinute, 1000, null, null, It.IsAny<CancellationToken>()),
            Times.Once);

        (await harness.Db.Klines.CountAsync()).Should().Be(6);
    }

    [Fact]
    public async Task PartialFail_ContinuesToNextSymbol_AndDoesNotThrow()
    {
        var marketData = new Mock<IBinanceMarketData>(MockBehavior.Strict);
        marketData
            .Setup(m => m.GetKlinesAsync(
                "BTCUSDT", KlineInterval.OneMinute, 1000,
                null, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("simulated REST failure"));
        marketData
            .Setup(m => m.GetKlinesAsync(
                "ETHUSDT", KlineInterval.OneMinute, 1000,
                null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildBars("ETHUSDT", count: 2));

        var probe = new StubProbe(ready: true);

        await using var harness = TestHarness.Build(
            options: BuildOptions(enabled: true, symbols: new[] { "BTCUSDT", "ETHUSDT" }),
            marketData: marketData.Object,
            probe: probe);

        var act = async () => await harness.Worker.RunOnceAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
        marketData.Verify(m => m.GetKlinesAsync(
            "ETHUSDT", KlineInterval.OneMinute, 1000, null, null, It.IsAny<CancellationToken>()),
            Times.Once);
        (await harness.Db.Klines.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task WsNeverReady_ProceedsAfterTimeout_StillCallsMarketData()
    {
        var marketData = new Mock<IBinanceMarketData>(MockBehavior.Strict);
        marketData
            .Setup(m => m.GetKlinesAsync(
                "BTCUSDT", KlineInterval.OneMinute, 1000,
                null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildBars("BTCUSDT", count: 1));

        var probe = new StubProbe(ready: false);

        await using var harness = TestHarness.Build(
            options: BuildOptions(enabled: true, symbols: new[] { "BTCUSDT" }),
            marketData: marketData.Object,
            probe: probe);

        var startedAt = DateTimeOffset.UtcNow;
        await harness.Worker.RunOnceAsync(CancellationToken.None);
        var elapsed = DateTimeOffset.UtcNow - startedAt;

        elapsed.Should().BeGreaterThanOrEqualTo(FastBudget - TimeSpan.FromMilliseconds(50));
        marketData.Verify(m => m.GetKlinesAsync(
            "BTCUSDT", KlineInterval.OneMinute, 1000, null, null, It.IsAny<CancellationToken>()),
            Times.Once);
        (await harness.Db.Klines.CountAsync()).Should().Be(1);
    }

    private static BinanceOptions BuildOptions(bool enabled, string[] symbols) => new()
    {
        RestBaseUrl = "https://testnet.binance.vision",
        WsBaseUrl = "wss://stream.testnet.binance.vision",
        Symbols = symbols,
        KlineIntervals = new[] { "1m" },
        BackfillEnabled = enabled,
        BackfillLimit = 1000,
        BackfillIntervals = new[] { "1m" },
    };

    private static IReadOnlyList<RestKlineDto> BuildBars(string symbol, int count)
    {
        var bars = new List<RestKlineDto>(count);
        var price = symbol.StartsWith("BTC", StringComparison.OrdinalIgnoreCase) ? 30000m : 2000m;
        for (var i = 0; i < count; i++)
        {
            var openTime = NowUtc.AddMinutes(-count + i);
            var closeTime = openTime.AddMinutes(1);
            bars.Add(new RestKlineDto(
                OpenTime: openTime,
                CloseTime: closeTime,
                Open: price,
                High: price + 10m,
                Low: price - 10m,
                Close: price + 5m,
                Volume: 1m,
                QuoteVolume: price,
                TradeCount: 10,
                TakerBuyBase: 0.5m,
                TakerBuyQuote: price / 2m));
        }
        return bars;
    }

    private sealed class StubProbe : IWsReadinessProbe
    {
        public StubProbe(bool ready) { IsReady = ready; }
        public bool IsReady { get; set; }
    }

    private sealed class StubClock : IClock
    {
        public DateTimeOffset UtcNow => NowUtc.AddMinutes(1);
        public long BinanceServerTimeMs => UtcNow.ToUnixTimeMilliseconds();
        public long DriftMs => 0;
    }

    private sealed class TestHarness : IAsyncDisposable
    {
        public required ServiceProvider Provider { get; init; }
        public required ApplicationDbContext Db { get; init; }
        public required KlineBackfillWorker Worker { get; init; }

        public static TestHarness Build(
            BinanceOptions options,
            IBinanceMarketData marketData,
            IWsReadinessProbe probe)
        {
            var services = new ServiceCollection();

            services.AddLogging();

            // MediatR publisher needed by ApplicationDbContext ctor — no-op in tests.
            var publisher = new Mock<IPublisher>();
            publisher
                .Setup(p => p.Publish(It.IsAny<object>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            publisher
                .Setup(p => p.Publish(It.IsAny<INotification>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            services.AddSingleton(publisher.Object);

            var dbName = $"backfill-tests-{Guid.NewGuid():N}";
            services.AddDbContext<ApplicationDbContext>(opt =>
                opt.UseInMemoryDatabase(dbName));
            services.AddScoped<IApplicationDbContext>(sp =>
                sp.GetRequiredService<ApplicationDbContext>());

            services.AddSingleton(marketData);
            // Per-scope persister mirrors the production registration.
            services.AddScoped<IKlinePersister, KlinePersister>();

            services.AddSingleton(probe);
            services.AddSingleton<IOptionsMonitor<BinanceOptions>>(
                new StaticOptionsMonitor<BinanceOptions>(options));

            var provider = services.BuildServiceProvider();

            var worker = new KlineBackfillWorker(
                provider.GetRequiredService<IServiceScopeFactory>(),
                provider.GetRequiredService<IOptionsMonitor<BinanceOptions>>(),
                probe,
                new StubClock(),
                TimeProvider.System,
                NullLogger<KlineBackfillWorker>.Instance,
                FastBudget,
                FastPoll);

            return new TestHarness
            {
                Provider = provider,
                Db = provider.GetRequiredService<ApplicationDbContext>(),
                Worker = worker,
            };
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await Provider.DisposeAsync();
        }
    }

    private sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
    {
        public StaticOptionsMonitor(T value) { CurrentValue = value; }
        public T CurrentValue { get; }
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
