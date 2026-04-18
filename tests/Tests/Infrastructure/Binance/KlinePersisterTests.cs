using BinanceBot.Application.Abstractions;
using BinanceBot.Application.Abstractions.Binance;
using BinanceBot.Domain.MarketData;
using BinanceBot.Domain.MarketData.Events;
using BinanceBot.Infrastructure.Binance.Workers;
using BinanceBot.Infrastructure.Persistence;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace BinanceBot.Tests.Infrastructure.Binance;

/// <summary>
/// ADR-0010 contract: <see cref="KlinePersister"/> must publish accumulated
/// domain events through the <see cref="IPublisher"/> for live WS bars
/// (default), and must NOT publish them when the REST backfill caller passes
/// <c>emitDomainEvents: false</c>. The persistence path itself (DB row state)
/// stays identical in both modes — only the publish fan-out differs.
/// </summary>
public sealed class KlinePersisterTests
{
    private static readonly DateTimeOffset BaseOpen =
        new(2026, 4, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task PersistAsync_DefaultEmit_PublishesClosedEventOnce()
    {
        await using var harness = TestHarness.Build();

        var payload = BuildPayload(BaseOpen, isClosed: true);

        await harness.Persister.PersistAsync(payload, CancellationToken.None);

        (await harness.Db.Klines.CountAsync()).Should().Be(1);
        CountPublishedEvents<KlineClosedEvent>(harness.Publisher).Should().Be(1);
        CountPublishedEvents<KlineIngestedEvent>(harness.Publisher).Should().Be(1);
    }

    [Fact]
    public async Task PersistAsync_SuppressNewInsert_PersistsButPublishesNothing()
    {
        await using var harness = TestHarness.Build();

        var payload = BuildPayload(BaseOpen, isClosed: true);

        await harness.Persister.PersistAsync(
            payload, CancellationToken.None, emitDomainEvents: false);

        (await harness.Db.Klines.CountAsync()).Should().Be(1);
        CountPublishedEvents<KlineClosedEvent>(harness.Publisher).Should().Be(0);
        CountPublishedEvents<KlineIngestedEvent>(harness.Publisher).Should().Be(0);
    }

    [Fact]
    public async Task PersistAsync_SuppressUpsert_PersistsButPublishesNothing()
    {
        await using var harness = TestHarness.Build();

        // Seed: live WS bar is already in the DB and its events have been
        // drained by ApplicationDbContext.SaveChangesAsync (real fan-out).
        var initial = BuildPayload(BaseOpen, isClosed: false);
        await harness.Persister.PersistAsync(initial, CancellationToken.None);
        harness.Publisher.Invocations.Clear();

        // Backfill subsequently re-touches the same key — must stay silent.
        var upsert = BuildPayload(BaseOpen, isClosed: true);
        await harness.Persister.PersistAsync(
            upsert, CancellationToken.None, emitDomainEvents: false);

        (await harness.Db.Klines.CountAsync()).Should().Be(1);
        var stored = await harness.Db.Klines.SingleAsync();
        stored.IsClosed.Should().BeTrue();

        CountPublishedEvents<KlineClosedEvent>(harness.Publisher).Should().Be(0);
        CountPublishedEvents<KlineIngestedEvent>(harness.Publisher).Should().Be(0);
    }

    [Fact]
    public async Task PersistAsync_MultiBarEmit_PublishesClosedEventPerBar()
    {
        await using var harness = TestHarness.Build();

        const int barCount = 5;
        for (var i = 0; i < barCount; i++)
        {
            var payload = BuildPayload(BaseOpen.AddMinutes(i), isClosed: true);
            await harness.Persister.PersistAsync(
                payload, CancellationToken.None, emitDomainEvents: true);
        }

        (await harness.Db.Klines.CountAsync()).Should().Be(barCount);
        CountPublishedEvents<KlineClosedEvent>(harness.Publisher).Should().Be(barCount);
        CountPublishedEvents<KlineIngestedEvent>(harness.Publisher).Should().Be(barCount);
    }

    /// <summary>
    /// ApplicationDbContext stores events as <c>List&lt;IDomainEvent&gt;</c>
    /// and dispatches via <c>IPublisher.Publish(IDomainEvent, ct)</c>, so the
    /// generic resolves to <c>Publish&lt;IDomainEvent&gt;</c> at the call site.
    /// Concrete-type matchers therefore never match — we count by inspecting
    /// the runtime type of the captured argument instead.
    /// </summary>
    private static int CountPublishedEvents<TEvent>(Mock<IPublisher> publisher)
        where TEvent : class
    {
        return publisher.Invocations
            .Where(i => i.Method.Name == nameof(IPublisher.Publish))
            .Count(i => i.Arguments.Count > 0 && i.Arguments[0] is TEvent);
    }

    private static WsKlinePayload BuildPayload(DateTimeOffset openTime, bool isClosed)
    {
        var closeTime = openTime.AddMinutes(1);
        return new WsKlinePayload(
            Symbol: "BTCUSDT",
            Interval: KlineInterval.OneMinute,
            OpenTime: openTime,
            CloseTime: closeTime,
            Open: 30000m,
            High: 30010m,
            Low: 29990m,
            Close: 30005m,
            Volume: 1m,
            QuoteVolume: 30000m,
            TradeCount: 10,
            TakerBuyBase: 0.5m,
            TakerBuyQuote: 15000m,
            IsClosed: isClosed);
    }

    private sealed class TestHarness : IAsyncDisposable
    {
        public required ServiceProvider Provider { get; init; }
        public required ApplicationDbContext Db { get; init; }
        public required IKlinePersister Persister { get; init; }
        public required Mock<IPublisher> Publisher { get; init; }

        public static TestHarness Build()
        {
            var services = new ServiceCollection();
            services.AddLogging();

            var publisher = new Mock<IPublisher>();
            publisher
                .Setup(p => p.Publish(It.IsAny<object>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            publisher
                .Setup(p => p.Publish(It.IsAny<INotification>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            services.AddSingleton(publisher.Object);

            var dbName = $"persister-tests-{Guid.NewGuid():N}";
            services.AddDbContext<ApplicationDbContext>(opt =>
                opt.UseInMemoryDatabase(dbName));
            services.AddScoped<IApplicationDbContext>(sp =>
                sp.GetRequiredService<ApplicationDbContext>());
            services.AddScoped<IKlinePersister, KlinePersister>();

            var provider = services.BuildServiceProvider();

            return new TestHarness
            {
                Provider = provider,
                Db = provider.GetRequiredService<ApplicationDbContext>(),
                Persister = provider.GetRequiredService<IKlinePersister>(),
                Publisher = publisher,
            };
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await Provider.DisposeAsync();
        }
    }
}
