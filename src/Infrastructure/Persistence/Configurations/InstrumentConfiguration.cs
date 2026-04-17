using BinanceBot.Domain.Instruments;
using BinanceBot.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BinanceBot.Infrastructure.Persistence.Configurations;

public sealed class InstrumentConfiguration : IEntityTypeConfiguration<Instrument>
{
    public void Configure(EntityTypeBuilder<Instrument> builder)
    {
        builder.ToTable("Instruments");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedOnAdd();

        builder.Property(i => i.Symbol)
            .HasConversion(s => s.Value, v => Symbol.From(v))
            .HasMaxLength(20)
            .IsRequired();

        builder.HasIndex(i => i.Symbol).IsUnique().HasDatabaseName("UX_Instruments_Symbol");

        builder.Property(i => i.BaseAsset).HasMaxLength(16).IsRequired();
        builder.Property(i => i.QuoteAsset).HasMaxLength(16).IsRequired();
        builder.Property(i => i.Status).HasConversion<int>();
        builder.Property(i => i.TickSize).HasPrecision(28, 10);
        builder.Property(i => i.StepSize).HasPrecision(28, 10);
        builder.Property(i => i.MinNotional).HasPrecision(28, 10);
        builder.Property(i => i.MinQty).HasPrecision(28, 10);
        builder.Property(i => i.MaxQty).HasPrecision(28, 10);
        builder.Property(i => i.LastSyncedAt).IsRequired();

        builder.Ignore(i => i.DomainEvents);
    }
}
