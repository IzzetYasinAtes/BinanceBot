using BinanceBot.Application.Abstractions;
using BinanceBot.Domain.BacktestRuns;
using BinanceBot.Domain.Balances;
using BinanceBot.Domain.Instruments;
using BinanceBot.Domain.MarketData;
using BinanceBot.Domain.Orders;
using BinanceBot.Domain.Positions;
using BinanceBot.Domain.RiskProfiles;
using BinanceBot.Domain.Strategies;
using BinanceBot.Domain.SystemEvents;
using BinanceBot.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace BinanceBot.Tests.Infrastructure.Strategies;

/// <summary>
/// Lightweight EF Core InMemory implementation of <see cref="IApplicationDbContext"/> used
/// by handler tests. Keeps the surface minimal — no domain-event publishing, no value
/// converters; tests stage state directly via <c>db.Set.Add(...)</c>.
/// </summary>
internal sealed class StubDbContext : DbContext, IApplicationDbContext
{
    public StubDbContext(DbContextOptions<StubDbContext> options) : base(options) { }

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
        modelBuilder.Entity<Instrument>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Symbol).HasConversion(s => s.Value, v => Symbol.From(v));
            b.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<BookTicker>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Symbol).HasConversion(s => s.Value, v => Symbol.From(v));
        });

        modelBuilder.Entity<RiskProfile>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.CircuitBreakerStatus).HasConversion<int>();
            b.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<Position>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Symbol).HasConversion(s => s.Value, v => Symbol.From(v));
            b.Property(x => x.Side).HasConversion<int>();
            b.Property(x => x.Status).HasConversion<int>();
            b.Property(x => x.Mode).HasConversion<int>();
            b.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<Order>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Symbol).HasConversion(s => s.Value, v => Symbol.From(v));
            b.Property(x => x.Side).HasConversion<int>();
            b.Property(x => x.Type).HasConversion<int>();
            b.Property(x => x.TimeInForce).HasConversion<int>();
            b.Property(x => x.Status).HasConversion<int>();
            b.Property(x => x.Mode).HasConversion<int>();
            b.Ignore(x => x.Fills);
            b.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<VirtualBalance>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Mode).HasConversion<int>();
            b.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Ignore<Kline>();
        modelBuilder.Ignore<OrderBookSnapshot>();
        modelBuilder.Ignore<OrderFill>();
        modelBuilder.Ignore<Strategy>();
        modelBuilder.Ignore<StrategySignal>();
        modelBuilder.Ignore<BacktestRun>();
        modelBuilder.Ignore<BacktestTrade>();
        modelBuilder.Ignore<SystemEvent>();

        base.OnModelCreating(modelBuilder);
    }

    Task<int> IApplicationDbContext.SaveChangesAsync(CancellationToken cancellationToken) =>
        base.SaveChangesAsync(cancellationToken);
}
