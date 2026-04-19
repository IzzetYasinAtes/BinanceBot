using BinanceBot.Application.Abstractions;
using BinanceBot.Application.Abstractions.Binance;
using BinanceBot.Domain.MarketData;
using BinanceBot.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BinanceBot.Infrastructure.Binance.Workers;

public sealed class BookTickerIngestionWorker : BackgroundService
{
    private readonly IBinanceMarketStream _stream;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BookTickerIngestionWorker> _logger;

    public BookTickerIngestionWorker(
        IBinanceMarketStream stream,
        IServiceScopeFactory scopeFactory,
        ILogger<BookTickerIngestionWorker> logger)
    {
        _stream = stream;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Loop 23 blocker fix (BLOCKER-2): dedicated subscriber channel.
        var reader = _stream.SubscribeBookTickers();
        await foreach (var payload in reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await PersistAsync(payload, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BookTicker persist error for {Symbol}", payload.Symbol);
            }
        }
    }

    private async Task PersistAsync(WsBookTickerPayload p, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var symbolVo = Symbol.From(p.Symbol);
        var existing = await ((DbSet<BookTicker>)db.BookTickers)
            .FirstOrDefaultAsync(b => b.Symbol == symbolVo, ct);

        if (existing is null)
        {
            var created = BookTicker.Create(
                symbolVo, p.BidPrice, p.BidQty, p.AskPrice, p.AskQty, p.UpdateId, p.ReceivedAt);
            db.BookTickers.Add(created);
        }
        else
        {
            existing.Apply(p.BidPrice, p.BidQty, p.AskPrice, p.AskQty, p.UpdateId, p.ReceivedAt);
        }

        await db.SaveChangesAsync(ct);
    }
}
