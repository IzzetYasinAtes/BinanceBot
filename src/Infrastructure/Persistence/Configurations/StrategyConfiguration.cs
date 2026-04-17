using BinanceBot.Domain.Strategies;
using BinanceBot.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BinanceBot.Infrastructure.Persistence.Configurations;

public sealed class StrategyConfiguration : IEntityTypeConfiguration<Strategy>
{
    public void Configure(EntityTypeBuilder<Strategy> builder)
    {
        builder.ToTable("Strategies");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedOnAdd();

        builder.Property(s => s.Name).HasMaxLength(80).IsRequired();
        builder.HasIndex(s => s.Name).IsUnique().HasDatabaseName("UX_Strategies_Name");

        builder.Property(s => s.Type).HasConversion<int>();
        builder.Property(s => s.Status).HasConversion<int>();
        builder.Property(s => s.SymbolsCsv).HasMaxLength(200).IsRequired();
        builder.Property(s => s.ParametersJson).HasColumnType("nvarchar(max)").IsRequired();

        builder.HasMany(s => s.Signals)
            .WithOne()
            .HasForeignKey(sig => sig.StrategyId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(Strategy.Signals))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(s => s.Status).HasDatabaseName("IX_Strategies_Status");

        builder.Ignore(s => s.DomainEvents);
    }
}

public sealed class StrategySignalConfiguration : IEntityTypeConfiguration<StrategySignal>
{
    public void Configure(EntityTypeBuilder<StrategySignal> builder)
    {
        builder.ToTable("StrategySignals");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedOnAdd();

        builder.Property(s => s.Symbol)
            .HasConversion(s => s.Value, v => Symbol.From(v))
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(s => s.Direction).HasConversion<int>();
        builder.Property(s => s.SuggestedQuantity).HasPrecision(28, 10);
        builder.Property(s => s.SuggestedPrice).HasPrecision(28, 10);
        builder.Property(s => s.SuggestedStopPrice).HasPrecision(28, 10);
        builder.Property(s => s.ContextJson).HasColumnType("nvarchar(max)").IsRequired();

        builder.HasIndex(s => new { s.StrategyId, s.BarOpenTime, s.Symbol })
            .IsUnique()
            .HasDatabaseName("UX_StrategySignals_Bar");
        builder.HasIndex(s => new { s.StrategyId, s.EmittedAt })
            .HasDatabaseName("IX_StrategySignals_Strategy_Emitted");
    }
}
