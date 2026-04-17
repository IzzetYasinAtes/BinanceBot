using BinanceBot.Domain.BacktestRuns;
using BinanceBot.Domain.Instruments;
using BinanceBot.Domain.MarketData;
using BinanceBot.Domain.Orders;
using BinanceBot.Domain.Positions;
using BinanceBot.Domain.RiskProfiles;
using BinanceBot.Domain.Strategies;
using BinanceBot.Domain.SystemEvents;
using Microsoft.EntityFrameworkCore;

namespace BinanceBot.Application.Abstractions;

public interface IApplicationDbContext
{
    DbSet<Instrument> Instruments { get; }
    DbSet<Kline> Klines { get; }
    DbSet<BookTicker> BookTickers { get; }
    DbSet<OrderBookSnapshot> OrderBookSnapshots { get; }
    DbSet<Order> Orders { get; }
    DbSet<OrderFill> OrderFills { get; }
    DbSet<Position> Positions { get; }
    DbSet<Strategy> Strategies { get; }
    DbSet<StrategySignal> StrategySignals { get; }
    DbSet<RiskProfile> RiskProfiles { get; }
    DbSet<BacktestRun> BacktestRuns { get; }
    DbSet<BacktestTrade> BacktestTrades { get; }
    DbSet<SystemEvent> SystemEvents { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
