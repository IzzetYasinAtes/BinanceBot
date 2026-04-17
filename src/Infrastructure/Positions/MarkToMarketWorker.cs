using BinanceBot.Application.Abstractions;
using BinanceBot.Domain.MarketData;
using BinanceBot.Domain.Positions;
using BinanceBot.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BinanceBot.Infrastructure.Positions;

public sealed class MarkToMarketWorker : BackgroundService
{
    private static readonly TimeSpan Cycle = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MarkToMarketWorker> _logger;

    public MarkToMarketWorker(IServiceScopeFactory scopeFactory, ILogger<MarkToMarketWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MarkToMarketWorker tick failed");
            }

            try
            {
                await Task.Delay(Cycle, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var openPositions = await db.Positions
            .Where(p => p.Status == PositionStatus.Open)
            .ToListAsync(ct);

        if (openPositions.Count == 0) return;

        var symbols = openPositions.Select(p => p.Symbol).Distinct().ToList();
        var tickers = await db.BookTickers
            .AsNoTracking()
            .Where(b => symbols.Contains(b.Symbol))
            .ToListAsync(ct);

        var dirty = 0;
        foreach (var position in openPositions)
        {
            var ticker = tickers.FirstOrDefault(t => t.Symbol == position.Symbol);
            if (ticker is null) continue;

            var mid = (ticker.BidPrice + ticker.AskPrice) / 2m;
            if (mid <= 0m) continue;

            position.MarkToMarket(mid, clock.UtcNow);
            dirty++;
        }

        if (dirty > 0)
        {
            await db.SaveChangesAsync(ct);
            _logger.LogDebug("MarkToMarket cycle: updated {Count} positions", dirty);
        }
    }
}
