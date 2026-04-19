using BinanceBot.Application.Abstractions.Binance;
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
        // Loop 23 blocker fix (BLOCKER-2): dedicated subscriber channel so this
        // worker and MarketIndicatorService both receive every kline envelope.
        var reader = _stream.SubscribeKlines();
        await foreach (var payload in reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var persister = scope.ServiceProvider.GetRequiredService<IKlinePersister>();
                await persister.PersistAsync(payload, stoppingToken);
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
}
