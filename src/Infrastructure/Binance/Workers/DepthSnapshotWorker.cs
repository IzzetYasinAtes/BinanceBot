using System.Text.Json;
using BinanceBot.Application.Abstractions;
using BinanceBot.Application.Abstractions.Binance;
using BinanceBot.Domain.MarketData;
using BinanceBot.Domain.ValueObjects;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BinanceBot.Infrastructure.Binance.Workers;

/// <summary>
/// Depth snapshot + diff resync (U/u validation).
/// Snapshot is taken from REST; subsequent diffs validated via WsDepthDiffPayload (PreviousFinalUpdateId).
/// If gap detected, resync by re-fetching snapshot.
/// </summary>
public sealed class DepthSnapshotWorker : BackgroundService
{
    private readonly IBinanceMarketStream _stream;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<BinanceOptions> _options;
    private readonly ILogger<DepthSnapshotWorker> _logger;
    private readonly Dictionary<string, long> _lastUpdateIdBySymbol = [];

    public DepthSnapshotWorker(
        IBinanceMarketStream stream,
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<BinanceOptions> options,
        ILogger<DepthSnapshotWorker> logger)
    {
        _stream = stream;
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Loop 23 blocker fix (BLOCKER-2): dedicated subscriber channel.
        var reader = _stream.SubscribeDepth();
        await foreach (var diff in reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await HandleAsync(diff, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Depth diff handler error for {Symbol}", diff.Symbol);
            }
        }
    }

    private async Task HandleAsync(WsDepthDiffPayload diff, CancellationToken ct)
    {
        if (!_lastUpdateIdBySymbol.TryGetValue(diff.Symbol, out var last))
        {
            await CaptureSnapshotAsync(diff.Symbol, ct);
            return;
        }

        if (diff.FirstUpdateId > last + 1)
        {
            _logger.LogWarning("Depth gap detected for {Symbol} (last={Last}, first={First}); resyncing",
                diff.Symbol, last, diff.FirstUpdateId);
            await CaptureSnapshotAsync(diff.Symbol, ct);
            return;
        }

        _lastUpdateIdBySymbol[diff.Symbol] = diff.FinalUpdateId;
    }

    private async Task CaptureSnapshotAsync(string symbol, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var market = scope.ServiceProvider.GetRequiredService<IBinanceMarketData>();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var limit = _options.CurrentValue.DepthSnapshotLimit;
        var snap = await market.GetOrderBookSnapshotAsync(symbol, limit, ct);

        var symbolVo = Symbol.From(symbol);
        var entity = OrderBookSnapshot.Capture(
            symbolVo,
            snap.LastUpdateId,
            JsonSerializer.Serialize(snap.Bids),
            JsonSerializer.Serialize(snap.Asks),
            DateTimeOffset.UtcNow);

        db.OrderBookSnapshots.Add(entity);
        await db.SaveChangesAsync(ct);

        _lastUpdateIdBySymbol[symbol] = snap.LastUpdateId;
        _logger.LogInformation("Depth snapshot captured for {Symbol} (lastUpdateId={Id})",
            symbol, snap.LastUpdateId);
    }
}
