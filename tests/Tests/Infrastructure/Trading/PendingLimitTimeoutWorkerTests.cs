using Ardalis.Result;
using BinanceBot.Application.Abstractions;
using BinanceBot.Application.Abstractions.Binance;
using BinanceBot.Application.Abstractions.Exchange;
using BinanceBot.Domain.Common;
using BinanceBot.Domain.MarketData;
using BinanceBot.Domain.Orders;
using BinanceBot.Domain.ValueObjects;
using BinanceBot.Infrastructure.Trading;
using BinanceBot.Tests.Infrastructure.Strategies;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace BinanceBot.Tests.Infrastructure.Trading;

/// <summary>
/// Loop 107 / ADR-0026 §A — PendingLimitTimeoutWorker testleri. Tek worker
/// timeout expire + paper fill simulation iki path'i de yapar.
/// </summary>
public class PendingLimitTimeoutWorkerTests
{
    private static readonly Symbol Btc = Symbol.From("BTCUSDT");

    private static (PendingLimitTimeoutWorker Worker, StubDbContext Db, Mock<IExchangeClient> Exchange, TestClock Clock)
        BuildHarness(DateTimeOffset now)
    {
        var sc = new ServiceCollection();
        var dbName = Guid.NewGuid().ToString();
        sc.AddDbContext<StubDbContext>(o => o.UseInMemoryDatabase(dbName));
        sc.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<StubDbContext>());

        var clock = new TestClock(now);
        sc.AddSingleton<IClock>(clock);

        var exchange = new Mock<IExchangeClient>(MockBehavior.Loose);
        sc.AddSingleton(exchange.Object);

        var sp = sc.BuildServiceProvider();
        var db = sp.GetRequiredService<StubDbContext>();

        var worker = new PendingLimitTimeoutWorker(
            sp.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<PendingLimitTimeoutWorker>.Instance);
        return (worker, db, exchange, clock);
    }

    [Fact]
    public async Task Cycle_ExpiresPendingLimit_WhenExpiresAtPassed_PaperMode()
    {
        var t0 = new DateTimeOffset(2026, 5, 5, 10, 0, 0, TimeSpan.Zero);
        var (worker, db, exchange, _) = BuildHarness(now: t0.AddMinutes(6));

        var order = Order.PlaceLimit(
            "cid-expire-1", Btc, OrderSide.Buy, 0.01m,
            limitPrice: 29970m, stopPrice: null, strategyId: 1,
            mode: TradingMode.Paper, now: t0, expiresAt: t0.AddMinutes(5));
        db.Orders.Add(order);
        // BookTicker far above limit so fill cannot trigger; only timeout path runs.
        db.BookTickers.Add(BookTicker.Create(
            Btc, bidPrice: 30050m, bidQty: 1m, askPrice: 30060m, askQty: 1m,
            updateId: 1L, updatedAt: t0));
        await db.SaveChangesAsync();

        await worker.ProcessCycleAsync(CancellationToken.None);

        // Re-query via fresh DbContext on shared InMemory store to bypass tracker state.
        var allOrders = await db.Orders.AsNoTracking().ToListAsync();
        var reloaded = allOrders.First(o => o.ClientOrderId == "cid-expire-1");
        reloaded.Status.Should().Be(OrderStatus.Expired);
        // Paper mode: CancelLiveOrderAsync called değil.
        exchange.Verify(e => e.CancelLiveOrderAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Cycle_LiveTestnet_ExpiresPendingLimit_AlsoCancelsOnExchange()
    {
        var t0 = new DateTimeOffset(2026, 5, 5, 10, 0, 0, TimeSpan.Zero);
        var (worker, db, exchange, _) = BuildHarness(now: t0.AddMinutes(6));

        exchange.Setup(e => e.CancelLiveOrderAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new CancelOrderResponse(
                Accepted: true, ErrorCode: null, ErrorMessage: null)));

        var order = Order.PlaceLimit(
            "cid-expire-lt", Btc, OrderSide.Buy, 0.01m,
            limitPrice: 29970m, stopPrice: null, strategyId: 1,
            mode: TradingMode.LiveTestnet, now: t0, expiresAt: t0.AddMinutes(5));
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        await worker.ProcessCycleAsync(CancellationToken.None);

        var reloaded = await db.Orders.AsNoTracking().FirstAsync(o => o.ClientOrderId == "cid-expire-lt");
        reloaded.Status.Should().Be(OrderStatus.Expired);
        exchange.Verify(e => e.CancelLiveOrderAsync("BTCUSDT", "cid-expire-lt", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Cycle_PaperLong_FillsAtLimitPrice_WhenAskCrossesBelow()
    {
        var t0 = new DateTimeOffset(2026, 5, 5, 10, 0, 0, TimeSpan.Zero);
        // now still within timeout so fill path triggers, not timeout.
        var (worker, db, _, _) = BuildHarness(now: t0.AddMinutes(2));

        var order = Order.PlaceLimit(
            "cid-fill-long", Btc, OrderSide.Buy, 0.01m,
            limitPrice: 29970m, stopPrice: null, strategyId: 1,
            mode: TradingMode.Paper, now: t0, expiresAt: t0.AddMinutes(5));
        db.Orders.Add(order);
        // Ask 29969 ≤ limit 29970 → cross → fill.
        db.BookTickers.Add(BookTicker.Create(
            Btc, bidPrice: 29965m, bidQty: 1m, askPrice: 29969m, askQty: 1m,
            updateId: 1L, updatedAt: t0.AddMinutes(2)));
        await db.SaveChangesAsync();

        await worker.ProcessCycleAsync(CancellationToken.None);

        var reloaded = await db.Orders.AsNoTracking().FirstAsync(o => o.ClientOrderId == "cid-fill-long");
        reloaded.Status.Should().Be(OrderStatus.Filled);
        reloaded.ExecutedQuantity.Should().Be(0.01m);
        // Fill price = limit price (limit guarantee, no slippage).
        reloaded.CumulativeQuoteQty.Should().Be(299.70m);
    }

    [Fact]
    public async Task Cycle_PaperShort_FillsAtLimitPrice_WhenBidCrossesAbove()
    {
        var t0 = new DateTimeOffset(2026, 5, 5, 10, 0, 0, TimeSpan.Zero);
        var (worker, db, _, _) = BuildHarness(now: t0.AddMinutes(2));

        var order = Order.PlaceLimit(
            "cid-fill-short", Btc, OrderSide.Sell, 0.01m,
            limitPrice: 30030m, stopPrice: null, strategyId: 1,
            mode: TradingMode.Paper, now: t0, expiresAt: t0.AddMinutes(5));
        db.Orders.Add(order);
        // Bid 30035 ≥ limit 30030 → cross → fill.
        db.BookTickers.Add(BookTicker.Create(
            Btc, bidPrice: 30035m, bidQty: 1m, askPrice: 30040m, askQty: 1m,
            updateId: 1L, updatedAt: t0.AddMinutes(2)));
        await db.SaveChangesAsync();

        await worker.ProcessCycleAsync(CancellationToken.None);

        var reloaded = await db.Orders.AsNoTracking().FirstAsync(o => o.ClientOrderId == "cid-fill-short");
        reloaded.Status.Should().Be(OrderStatus.Filled);
        reloaded.CumulativeQuoteQty.Should().Be(300.30m);
    }

    [Fact]
    public async Task Cycle_PaperLong_KeepsPending_WhenAskAboveLimit()
    {
        var t0 = new DateTimeOffset(2026, 5, 5, 10, 0, 0, TimeSpan.Zero);
        var (worker, db, _, _) = BuildHarness(now: t0.AddMinutes(1));

        var order = Order.PlaceLimit(
            "cid-pending", Btc, OrderSide.Buy, 0.01m,
            limitPrice: 29970m, stopPrice: null, strategyId: 1,
            mode: TradingMode.Paper, now: t0, expiresAt: t0.AddMinutes(5));
        db.Orders.Add(order);
        // Ask 29980 > limit 29970 — pullback noktasına henüz ulaşmadı.
        db.BookTickers.Add(BookTicker.Create(
            Btc, bidPrice: 29975m, bidQty: 1m, askPrice: 29980m, askQty: 1m,
            updateId: 1L, updatedAt: t0.AddMinutes(1)));
        await db.SaveChangesAsync();

        await worker.ProcessCycleAsync(CancellationToken.None);

        var reloaded = await db.Orders.AsNoTracking().FirstAsync(o => o.ClientOrderId == "cid-pending");
        reloaded.Status.Should().Be(OrderStatus.New);
        reloaded.ExecutedQuantity.Should().Be(0m);
    }
}
