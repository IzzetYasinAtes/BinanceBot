using BinanceBot.Application.Abstractions;
using BinanceBot.Domain.BacktestRuns;
using BinanceBot.Domain.Balances;
using BinanceBot.Domain.Common;
using BinanceBot.Domain.Instruments;
using BinanceBot.Domain.MarketData;
using BinanceBot.Domain.Orders;
using BinanceBot.Domain.Positions;
using BinanceBot.Domain.RiskProfiles;
using BinanceBot.Domain.Strategies;
using BinanceBot.Domain.SystemEvents;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BinanceBot.Infrastructure.Persistence;

public sealed class ApplicationDbContext : DbContext, IApplicationDbContext
{
    private readonly IPublisher _publisher;

    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, IPublisher publisher)
        : base(options)
    {
        _publisher = publisher;
    }

    public DbSet<Instrument> Instruments => Set<Instrument>();
    public DbSet<Kline> Klines => Set<Kline>();
    public DbSet<BookTicker> BookTickers => Set<BookTicker>();
    public DbSet<OrderBookSnapshot> OrderBookSnapshots => Set<OrderBookSnapshot>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderFill> OrderFills => Set<OrderFill>();
    public DbSet<Position> Positions => Set<Position>();
    public DbSet<Strategy> Strategies => Set<Strategy>();
    public DbSet<StrategySignal> StrategySignals => Set<StrategySignal>();
    public DbSet<RiskProfile> RiskProfiles => Set<RiskProfile>();
    public DbSet<BacktestRun> BacktestRuns => Set<BacktestRun>();
    public DbSet<BacktestTrade> BacktestTrades => Set<BacktestTrade>();
    public DbSet<SystemEvent> SystemEvents => Set<SystemEvent>();
    public DbSet<VirtualBalance> VirtualBalances => Set<VirtualBalance>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var aggregates = ChangeTracker
            .Entries()
            .Where(e => e.Entity is Instrument
                     || e.Entity is Kline
                     || e.Entity is Order
                     || e.Entity is Position
                     || e.Entity is Strategy
                     || e.Entity is RiskProfile
                     || e.Entity is BacktestRun
                     || e.Entity is VirtualBalance)
            .Select(e => e.Entity)
            .ToList();

        var events = new List<IDomainEvent>();
        foreach (var agg in aggregates)
        {
            switch (agg)
            {
                case Instrument i: events.AddRange(i.DomainEvents); i.ClearDomainEvents(); break;
                case Kline k: events.AddRange(k.DomainEvents); k.ClearDomainEvents(); break;
                case Order o: events.AddRange(o.DomainEvents); o.ClearDomainEvents(); break;
                case Position p: events.AddRange(p.DomainEvents); p.ClearDomainEvents(); break;
                case Strategy s: events.AddRange(s.DomainEvents); s.ClearDomainEvents(); break;
                case RiskProfile r: events.AddRange(r.DomainEvents); r.ClearDomainEvents(); break;
                case BacktestRun b: events.AddRange(b.DomainEvents); b.ClearDomainEvents(); break;
                case VirtualBalance vb: events.AddRange(vb.DomainEvents); vb.ClearDomainEvents(); break;
            }
        }

        var affected = await base.SaveChangesAsync(cancellationToken);

        foreach (var evt in events)
        {
            await _publisher.Publish(evt, cancellationToken);
        }

        return affected;
    }
}
