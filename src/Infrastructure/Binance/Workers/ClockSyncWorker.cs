using BinanceBot.Application.Abstractions.Binance;
using BinanceBot.Infrastructure.Time;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BinanceBot.Infrastructure.Binance.Workers;

public sealed class ClockSyncWorker : BackgroundService
{
    private static readonly TimeSpan SyncInterval = TimeSpan.FromMinutes(10);
    private const long DriftWarnThresholdMs = 500;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly BinanceClock _clock;
    private readonly ILogger<ClockSyncWorker> _logger;

    public ClockSyncWorker(
        IServiceScopeFactory scopeFactory,
        BinanceClock clock,
        ILogger<ClockSyncWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var market = scope.ServiceProvider.GetRequiredService<IBinanceMarketData>();
                var dto = await market.GetServerTimeAsync(stoppingToken);
                _clock.SetFromServer(dto.ServerTimeMs);

                if (Math.Abs(_clock.DriftMs) > DriftWarnThresholdMs)
                {
                    _logger.LogWarning("Clock drift {DriftMs}ms exceeds threshold {ThresholdMs}ms",
                        _clock.DriftMs, DriftWarnThresholdMs);
                }
                else
                {
                    _logger.LogDebug("Clock synced, drift {DriftMs}ms", _clock.DriftMs);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ClockSyncWorker cycle failed");
            }

            try
            {
                await Task.Delay(SyncInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
