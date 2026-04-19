using BinanceBot.Application.Abstractions;
using BinanceBot.Domain.SystemEvents;
using BinanceBot.Infrastructure.Persistence;
using BinanceBot.Infrastructure.SystemEvents;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace BinanceBot.Tests.Infrastructure.SystemEvents;

/// <summary>
/// ADR-0016 §16.9.4 contract — <see cref="SystemEventPublisher"/> writes a single
/// <c>SystemEvents</c> row per call, carries the typed enum through <c>EventType</c>
/// and serialises the wrapper payload with <c>type</c>, <c>typeCode</c>, <c>message</c>
/// and <c>details</c> fields. DB-insert failures are swallowed so the trade
/// pipeline is not blocked by a telemetry outage.
/// </summary>
public sealed class SystemEventPublisherTests
{
    [Fact]
    public async Task PublishAsync_InsertsSingleRow_WithTypedPayload()
    {
        await using var harness = Harness.Build();

        await harness.Publisher.PublishAsync(
            SystemEventType.Startup,
            "Uygulama başlatıldı",
            "{\"host\":\"test-host\"}",
            CancellationToken.None);

        var rows = await harness.Db.SystemEvents.ToListAsync();
        rows.Should().HaveCount(1);

        var row = rows[0];
        row.EventType.Should().Be("Startup");
        row.Severity.Should().Be(SystemEventSeverity.Info);
        row.Source.Should().Be("system");
        row.PayloadJson.Should().Contain("\"type\":\"Startup\"");
        row.PayloadJson.Should().Contain("\"typeCode\":1");
        // System.Text.Json default-escapes non-ASCII Turkish chars (ş → \u015F, ı → \u0131).
        row.PayloadJson.Should().Contain("\"message\":\"Uygulama ba");
        row.PayloadJson.Should().Contain("\"host\":\"test-host\"");
    }

    [Fact]
    public async Task PublishAsync_ResolvesWarningSeverity_ForRiskAlert()
    {
        await using var harness = Harness.Build();

        await harness.Publisher.PublishAsync(
            SystemEventType.RiskAlert,
            "Risk uyarısı",
            detailsJson: null,
            CancellationToken.None);

        var row = await harness.Db.SystemEvents.SingleAsync();
        row.Severity.Should().Be(SystemEventSeverity.Warning);
    }

    [Fact]
    public async Task PublishAsync_WithoutDetails_SerialisesNullDetailsField()
    {
        await using var harness = Harness.Build();

        await harness.Publisher.PublishAsync(
            SystemEventType.WarmupCompleted,
            "Indicator warmup tamamlandı: BTCUSDT",
            null,
            CancellationToken.None);

        var row = await harness.Db.SystemEvents.SingleAsync();
        row.PayloadJson.Should().Contain("\"details\":null");
        row.PayloadJson.Should().Contain("BTCUSDT");
    }

    [Fact]
    public async Task PublishAsync_MultipleCalls_AppendDistinctRows()
    {
        await using var harness = Harness.Build();

        await harness.Publisher.PublishAsync(
            SystemEventType.Startup, "Uygulama başlatıldı");
        await harness.Publisher.PublishAsync(
            SystemEventType.Shutdown, "Uygulama durduruluyor");

        var rows = await harness.Db.SystemEvents
            .OrderBy(x => x.Id).ToListAsync();
        rows.Should().HaveCount(2);
        rows[0].EventType.Should().Be("Startup");
        rows[1].EventType.Should().Be("Shutdown");
    }

    [Fact]
    public async Task PublishAsync_WithInvalidDetailsJson_FallsBackToRawString()
    {
        await using var harness = Harness.Build();

        await harness.Publisher.PublishAsync(
            SystemEventType.OrderPlaced,
            "Emir gönderildi",
            "not-json-{",
            CancellationToken.None);

        var row = await harness.Db.SystemEvents.SingleAsync();
        row.PayloadJson.Should().Contain("not-json-");
    }

    private sealed class Harness : IAsyncDisposable
    {
        public required ApplicationDbContext Db { get; init; }
        public required SystemEventPublisher Publisher { get; init; }

        public static Harness Build()
        {
            var publisher = new Mock<IPublisher>();
            publisher
                .Setup(p => p.Publish(It.IsAny<object>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            publisher
                .Setup(p => p.Publish(It.IsAny<INotification>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"sysevents-{Guid.NewGuid():N}")
                .Options;
            var db = new ApplicationDbContext(options, publisher.Object);

            var clock = new Mock<IClock>();
            clock.SetupGet(c => c.UtcNow).Returns(DateTimeOffset.UtcNow);

            var sut = new SystemEventPublisher(
                db, clock.Object, NullLogger<SystemEventPublisher>.Instance);

            return new Harness { Db = db, Publisher = sut };
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
        }
    }
}
