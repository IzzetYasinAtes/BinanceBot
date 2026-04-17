using BinanceBot.Application.Abstractions;
using BinanceBot.Application.Abstractions.Binance;
using BinanceBot.Domain.MarketData;
using BinanceBot.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BinanceBot.Infrastructure.Binance.Workers;

public sealed class KlineIngestionWorker : BackgroundService
{
    private readonly IBinanceMarketStream _stream;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<KlineIngestionWorker> _logger;

    public KlineIngestionWorker(
        IBinanceMarketStream stream,
        IServiceScopeFactory scopeFactory,
        ILogger<KlineIngestionWorker> logger)
    {
        _stream = stream;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var payload in _stream.KlineUpdates(stoppingToken))
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
                _logger.LogError(ex, "Kline persist error for {Symbol} {OpenTime}",
                    payload.Symbol, payload.OpenTime);
            }
        }
    }

    private async Task PersistAsync(WsKlinePayload p, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var symbolVo = Symbol.From(p.Symbol);
        var existing = await ((DbSet<Kline>)db.Klines)
            .FirstOrDefaultAsync(
                k => k.Symbol == symbolVo && k.Interval == p.Interval && k.OpenTime == p.OpenTime,
                ct);

        if (existing is null)
        {
            var kline = Kline.Ingest(
                symbolVo, p.Interval, p.OpenTime, p.CloseTime,
                p.Open, p.High, p.Low, p.Close,
                p.Volume, p.QuoteVolume, p.TradeCount,
                p.TakerBuyBase, p.TakerBuyQuote, p.IsClosed);
            db.Klines.Add(kline);
        }
        else
        {
            existing.Upsert(
                p.Open, p.High, p.Low, p.Close,
                p.Volume, p.QuoteVolume, p.TradeCount,
                p.TakerBuyBase, p.TakerBuyQuote, p.IsClosed);
        }

        await db.SaveChangesAsync(ct);
    }
}
