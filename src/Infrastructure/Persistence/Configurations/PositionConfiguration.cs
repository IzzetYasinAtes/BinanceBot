using BinanceBot.Domain.Positions;
using BinanceBot.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BinanceBot.Infrastructure.Persistence.Configurations;

public sealed class PositionConfiguration : IEntityTypeConfiguration<Position>
{
    public void Configure(EntityTypeBuilder<Position> builder)
    {
        builder.ToTable("Positions");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedOnAdd();

        builder.Property(p => p.Symbol)
            .HasConversion(s => s.Value, v => Symbol.From(v))
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(p => p.Side).HasConversion<int>();
        builder.Property(p => p.Status).HasConversion<int>();
        builder.Property(p => p.Mode).HasConversion<int>().IsRequired();

        builder.Property(p => p.Quantity).HasPrecision(28, 10);
        builder.Property(p => p.AverageEntryPrice).HasPrecision(28, 10);
        builder.Property(p => p.ExitPrice).HasPrecision(28, 10);
        builder.Property(p => p.MarkPrice).HasPrecision(28, 10);
        // ADR-0012 §12.4: Position.StopPrice — nullable, decimal(18,8) per spec.
        builder.Property(p => p.StopPrice).HasColumnType("decimal(18,8)");
        // Loop 10 take-profit fix — symmetric to StopPrice (decimal(18,8) NULL).
        builder.Property(p => p.TakeProfit).HasColumnType("decimal(18,8)");
        // ADR-0014 §14.5: pattern-based time stop. TimeSpan is not a native EF type;
        // store seconds (long?). null = no time stop. Conversion is symmetric so the
        // domain stays in TimeSpan and the database stays in bigint.
        builder.Property(p => p.MaxHoldDuration)
            .HasColumnName("MaxHoldDurationSeconds")
            .HasConversion(
                v => v.HasValue ? (long?)(long)v.Value.TotalSeconds : null,
                v => v.HasValue ? (TimeSpan?)TimeSpan.FromSeconds(v.Value) : null);
        builder.Property(p => p.UnrealizedPnl).HasPrecision(28, 10);
        builder.Property(p => p.RealizedPnl).HasPrecision(28, 10);

        builder.HasIndex(p => new { p.Symbol, p.Mode })
            .IsUnique()
            .HasFilter("[Status] = 1")
            .HasDatabaseName("UX_Positions_Symbol_Mode_Open");
        builder.HasIndex(p => new { p.Status, p.UpdatedAt })
            .HasDatabaseName("IX_Positions_Status_Updated");
        builder.HasIndex(p => new { p.Mode, p.Status, p.UpdatedAt })
            .HasDatabaseName("IX_Positions_Mode_Status");
        builder.HasIndex(p => p.StrategyId)
            .HasDatabaseName("IX_Positions_StrategyId");

        builder.Ignore(p => p.DomainEvents);
    }
}
