using BinanceBot.Application.Abstractions.Binance;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BinanceBot.Infrastructure.Strategies;

/// <summary>
/// Loop 67 KMS — BackgroundService that subscribes to the BookTicker fan-out
/// (<see cref="IBinanceMarketStream.SubscribeBookTickers"/>) and updates the
/// shared <see cref="BookTickerCache"/>. Separate channel from
/// <c>BookTickerIngestionWorker</c> (DB persister) so reader and persister do
/// not race for the same envelope (Loop 23 BLOCKER-2 fix pattern — every
/// consumer gets its own channel).
///
/// Lifecycle: started by host as IHostedService; subscribes once and drains
/// indefinitely. On shutdown the cancellation token triggers the
/// <c>await foreach</c> to exit cleanly. Errors are logged and swallowed —
/// stream supervisor reconnects underneath.
/// </summary>
public sealed class BookTickerCacheWorker : BackgroundService
{
    private readonly IBinanceMarketStream _stream;
    private readonly BookTickerCache _cache;
    private readonly ILogger<BookTickerCacheWorker> _logger;

    public BookTickerCacheWorker(
        IBinanceMarketStream stream,
        BookTickerCache cache,
        ILogger<BookTickerCacheWorker> logger)
    {
        _stream = stream;
        _cache = cache;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var reader = _stream.SubscribeBookTickers();

        try
        {
            await foreach (var payload in reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    _cache.Apply(payload);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "BookTickerCache apply failed symbol={Symbol}", payload.Symbol);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown — clean exit.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BookTickerCacheWorker terminated unexpectedly");
        }
    }
}
