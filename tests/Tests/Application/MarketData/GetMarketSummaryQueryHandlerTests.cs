using BinanceBot.Application.Abstractions;
using BinanceBot.Application.Abstractions.Binance;
using BinanceBot.Application.MarketData.Queries.GetMarketSummary;
using BinanceBot.Domain.MarketData;
using BinanceBot.Domain.ValueObjects;
using BinanceBot.Tests.Infrastructure.Strategies;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace BinanceBot.Tests.Application.MarketData;

/// <summary>
/// ADR-0012 §12.1 / Commit 2 — handler must source 24h fields from
/// <see cref="IBinanceMarketData.GetTicker24hAsync"/> (REST), not from local 1m klines, and
/// blend mark price from the local <c>BookTickers</c> table.
/// </summary>
public class GetMarketSummaryQueryHandlerTests
{
    private static StubDbContext NewDb()
    {
        var opts = new DbContextOptionsBuilder<StubDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new StubDbContext(opts);
    }

    [Fact]
    public async Task Handle_ReturnsRestPriceChangePct_NotZero()
    {
        using var db = NewDb();
        // Add a stale book ticker so the mark-price branch is exercised.
        db.BookTickers.Add(BookTicker.Create(
            Symbol.From("BTCUSDT"),
            bidPrice: 49990m, bidQty: 1m,
            askPrice: 50010m, askQty: 1m,
            updateId: 1, updatedAt: DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();

        var binance = new Mock<IBinanceMarketData>();
        binance.Setup(b => b.GetTicker24hAsync(
                It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new Ticker24hDto(
                    Symbol: "BTCUSDT",
                    LastPrice: 50000m,
                    PriceChangePct: 2.34m,
                    HighPrice: 51000m,
                    LowPrice: 49000m,
                    QuoteVolume: 1234567m,
                    CloseTime: DateTimeOffset.UtcNow),
            });

        var sut = new GetMarketSummaryQueryHandler(db, binance.Object);
        var result = await sut.Handle(new GetMarketSummaryQuery(new[] { "BTCUSDT" }), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value.Single();
        dto.Symbol.Should().Be("BTCUSDT");
        dto.LastPrice.Should().Be(50000m);
        dto.Change24hPct.Should().Be(2.34m);
        dto.Volume24hQuote.Should().Be(1234567m);
        // mark = (49990 + 50010) / 2
        dto.MarkPrice.Should().Be(50000m);
    }

    [Fact]
    public async Task Handle_FallsBackToLastPrice_WhenBookTickerMissing()
    {
        using var db = NewDb();
        // No book ticker rows seeded.

        var binance = new Mock<IBinanceMarketData>();
        binance.Setup(b => b.GetTicker24hAsync(
                It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new Ticker24hDto("BTCUSDT", 49500m, -1.5m, 50500m, 49400m, 100m, DateTimeOffset.UtcNow),
            });

        var sut = new GetMarketSummaryQueryHandler(db, binance.Object);
        var result = await sut.Handle(new GetMarketSummaryQuery(new[] { "BTCUSDT" }), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value.Single();
        // Without a book ticker, mark price falls back to REST lastPrice — never 0.
        dto.MarkPrice.Should().Be(49500m);
        dto.Change24hPct.Should().Be(-1.5m);
    }

    [Fact]
    public async Task Handle_EmptyTickerResponse_ReturnsEmptyResult()
    {
        using var db = NewDb();

        var binance = new Mock<IBinanceMarketData>();
        binance.Setup(b => b.GetTicker24hAsync(
                It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Ticker24hDto>());

        var sut = new GetMarketSummaryQueryHandler(db, binance.Object);
        var result = await sut.Handle(
            new GetMarketSummaryQuery(new[] { "BTCUSDT", "BNBUSDT" }), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }
}
