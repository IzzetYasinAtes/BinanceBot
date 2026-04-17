using BinanceBot.Domain.BacktestRuns;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BinanceBot.Infrastructure.Persistence.Configurations;

public sealed class BacktestRunConfiguration : IEntityTypeConfiguration<BacktestRun>
{
    public void Configure(EntityTypeBuilder<BacktestRun> builder)
    {
        builder.ToTable("BacktestRuns");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).ValueGeneratedOnAdd();

        builder.Property(b => b.Status).HasConversion<int>();
        builder.Property(b => b.InitialBalance).HasPrecision(28, 10);
        builder.Property(b => b.FinalBalance).HasPrecision(28, 10);
        builder.Property(b => b.Sharpe).HasPrecision(10, 4);
        builder.Property(b => b.MaxDrawdownPct).HasPrecision(10, 4);
        builder.Property(b => b.WinRate).HasPrecision(10, 4);
        builder.Property(b => b.FailureReason).HasMaxLength(500);

        builder.HasMany(b => b.Trades)
            .WithOne()
            .HasForeignKey(t => t.BacktestRunId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(BacktestRun.Trades))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(b => new { b.StrategyId, b.StartedAt })
            .HasDatabaseName("IX_BacktestRuns_Strategy_Started");
        builder.HasIndex(b => b.Status).HasDatabaseName("IX_BacktestRuns_Status");

        builder.Ignore(b => b.DomainEvents);
    }
}

public sealed class BacktestTradeConfiguration : IEntityTypeConfiguration<BacktestTrade>
{
    public void Configure(EntityTypeBuilder<BacktestTrade> builder)
    {
        builder.ToTable("BacktestTrades");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedOnAdd();

        builder.Property(t => t.Side).HasConversion<int>();
        builder.Property(t => t.Price).HasPrecision(28, 10);
        builder.Property(t => t.Quantity).HasPrecision(28, 10);
        builder.Property(t => t.Pnl).HasPrecision(28, 10);

        builder.HasIndex(t => new { t.BacktestRunId, t.SequenceNo })
            .IsUnique()
            .HasDatabaseName("UX_BacktestTrades_Run_Seq");
    }
}
