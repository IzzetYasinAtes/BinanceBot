using BinanceBot.Application.Abstractions;
using BinanceBot.Application.Abstractions.Exchange;
using BinanceBot.Domain.Common;
using BinanceBot.Domain.MarketData;
using BinanceBot.Domain.Orders;
using BinanceBot.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BinanceBot.Infrastructure.Trading;

/// <summary>
/// Loop 107 / ADR-0026 §A — Pending Limit Order timeout + paper fill worker.
/// Tek background service iki görev yapar:
/// <list type="number">
///   <item><b>Fill (Paper):</b> Status=New + Type=Limit + ExpiresAt > now + Mode=Paper.
///         Latest BookTicker ask &lt;= LimitPrice (Long) veya bid &gt;= LimitPrice (Short)
///         ise simulate fill @ limit price (Order.RegisterFill virtual tradeId).</item>
///   <item><b>Timeout:</b> Status=New + Type=Limit + ExpiresAt &lt;= now.
///         Order.Expire çağrılır (OrderExpiredEvent raise). LiveTestnet için
///         IExchangeClient.CancelLiveOrderAsync ile borsada da cancel; paper için
///         DB-only.</item>
/// </list>
///
/// Cycle: 30sn polling. AsNoTracking read önce (CLAUDE.md src §4), tracking
/// fetch sonra mutation için. EF tarafı SaveChangesAsync per-cycle batch.
///
/// Race guard: Order.Status guard her behavior'da (Filled/Cancelled idempotent).
/// </summary>
public sealed class PendingLimitTimeoutWorker : BackgroundService
{
    private static readonly TimeSpan CycleInterval = TimeSpan.FromSeconds(30);
    private static long _virtualFillCounter;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PendingLimitTimeoutWorker> _logger;

    public PendingLimitTimeoutWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<PendingLimitTimeoutWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "PendingLimitTimeoutWorker started (cycle={Cycle}s)",
            CycleInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PendingLimitTimeoutWorker cycle error");
            }

            try
            {
                await Task.Delay(CycleInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("PendingLimitTimeoutWorker stopping");
    }

    /// <summary>Internal cycle hook for unit tests — invokes the full timeout+fill pass.</summary>
    internal async Task ProcessCycleAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<IApplicationDbContext>();
        var clock = sp.GetRequiredService<IClock>();
        var exchange = sp.GetRequiredService<IExchangeClient>();
        var now = clock.UtcNow;

        // Tracked fetch: tüm pending limit emirleri (Status=New AND Type=Limit AND
        // ExpiresAt IS NOT NULL). Filtered index IX_Orders_Pending_Limit_Filtered
        // (Status=1 AND Type=2) zaten bu predicate'i optimize ediyor.
        var pending = await db.Orders
            .Where(o => o.Status == OrderStatus.New
                && o.Type == OrderType.Limit
                && o.ExpiresAt != null)
            .ToListAsync(ct);

        if (pending.Count == 0)
        {
            return;
        }

        // Sembol başına latest BookTicker (paper fill check için)
        // Loop 111: Symbol VO HasConversion + .Contains(VO) bazı providerlarda
        // (SQL Server) translate edilemez. StopLossMonitor pattern: string list +
        // Symbol.Value karşılaştırması. Materialize sonra in-memory filter.
        var symbolStrings = pending.Select(o => o.Symbol.Value).Distinct().ToList();
        var allTickers = await db.BookTickers
            .AsNoTracking()
            .ToListAsync(ct);
        var tickers = allTickers.Where(b => symbolStrings.Contains(b.Symbol.Value)).ToList();
        var tickerBySymbol = tickers.ToDictionary(t => t.Symbol);

        var filledCount = 0;
        var expiredCount = 0;

        foreach (var order in pending)
        {
            if (ct.IsCancellationRequested) break;

            // Step 1: Timeout? Önce timeout kontrol et — geç kalan emir fill'e şans
            // bulamadan iptal edilir (mainnet semantik: GTC + cancel after timeout).
            if (order.ExpiresAt <= now)
            {
                await ExpirePendingOrderAsync(order, exchange, now, ct);
                expiredCount++;
                continue;
            }

            // Step 2: Paper fill check — mark cross ettiyse limit @ price ile fill et.
            // Live (testnet/mainnet) için Binance kendi engine'i fill yapar; biz
            // sadece audit + status sync (Loop 92 user data stream zaten ORDER_TRADE_UPDATE
            // dinliyor). Bu yüzden sadece Paper için manuel fill simulasyonu.
            if (order.Mode != TradingMode.Paper)
            {
                continue;
            }

            if (!tickerBySymbol.TryGetValue(order.Symbol, out var ticker))
            {
                continue;
            }

            var crosses = order.Side == OrderSide.Buy
                ? ticker.AskPrice > 0m && ticker.AskPrice <= order.LimitPrice
                : ticker.BidPrice > 0m && ticker.BidPrice >= order.LimitPrice;
            if (!crosses)
            {
                continue;
            }

            FillPendingPaperOrder(order, now);
            filledCount++;
        }

        if (filledCount > 0 || expiredCount > 0)
        {
            await db.SaveChangesAsync(ct);
            _logger.LogInformation(
                "PendingLimitTimeoutWorker cycle filled={Filled} expired={Expired} pending={Pending}",
                filledCount, expiredCount, pending.Count - filledCount - expiredCount);
        }
    }

    /// <summary>
    /// Domain Order.Expire (idempotent terminal status guard) + LiveTestnet
    /// IExchangeClient.CancelLiveOrderAsync (mainnet ADR-0006 ile zaten Reject).
    /// </summary>
    private async Task ExpirePendingOrderAsync(
        Order order,
        IExchangeClient exchange,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (order.Mode == TradingMode.LiveTestnet)
        {
            try
            {
                var cancelResult = await exchange.CancelLiveOrderAsync(
                    order.Symbol.Value, order.ClientOrderId, ct);
                if (!cancelResult.IsSuccess)
                {
                    _logger.LogWarning(
                        "Pending limit cancel failed (live) cid={Cid} symbol={Symbol} errors={Errors}",
                        order.ClientOrderId, order.Symbol.Value, string.Join(";", cancelResult.Errors));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Pending limit cancel exception cid={Cid} symbol={Symbol}",
                    order.ClientOrderId, order.Symbol.Value);
            }
        }

        order.Expire(now);
        _logger.LogInformation(
            "Pending limit expired cid={Cid} symbol={Symbol} side={Side} limit={Limit} expiresAt={Exp} mode={Mode}",
            order.ClientOrderId, order.Symbol.Value, order.Side,
            order.LimitPrice, order.ExpiresAt, order.Mode);
    }

    /// <summary>
    /// Paper fill simulation — limit price'ta fill (slippage 0, limit guarantee).
    /// Commission FuturesPaperFillSimulator semantiği ile uyumlu (taker %0.05 USDT).
    /// </summary>
    private void FillPendingPaperOrder(Order order, DateTimeOffset now)
    {
        var fillPrice = order.LimitPrice ?? order.Price ?? 0m;
        if (fillPrice <= 0m)
        {
            _logger.LogWarning(
                "Pending limit paper fill skip (no price) cid={Cid}", order.ClientOrderId);
            return;
        }

        // Futures taker fee 0.05% (Loop 92 / FuturesPaperFillSimulator). USDT-quote.
        var notional = fillPrice * order.Quantity;
        var commission = notional * 0.0005m;
        var tradeId = Interlocked.Increment(ref _virtualFillCounter);

        order.RegisterFill(tradeId, fillPrice, order.Quantity, commission, "USDT", now);

        _logger.LogInformation(
            "Pending limit filled (paper) cid={Cid} symbol={Symbol} side={Side} limit={Limit} qty={Qty} fee={Fee}",
            order.ClientOrderId, order.Symbol.Value, order.Side,
            fillPrice, order.Quantity, commission);
    }
}
