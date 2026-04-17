using BinanceBot.Domain.MarketData;
using BinanceBot.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BinanceBot.Infrastructure.Persistence.Configurations;

public sealed class KlineConfiguration : IEntityTypeConfiguration<Kline>
{
    public void Configure(EntityTypeBuilder<Kline> builder)
    {
        builder.ToTable("Klines");
        builder.HasKey(k => k.Id);
        builder.Property(k => k.Id).ValueGeneratedOnAdd();

        builder.Property(k => k.Symbol)
            .HasConversion(s => s.Value, v => Symbol.From(v))
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(k => k.Interval).HasConversion<int>();
        builder.Property(k => k.OpenTime).IsRequired();
        builder.Property(k => k.CloseTime).IsRequired();

        builder.Property(k => k.OpenPrice).HasPrecision(28, 10);
        builder.Property(k => k.HighPrice).HasPrecision(28, 10);
        builder.Property(k => k.LowPrice).HasPrecision(28, 10);
        builder.Property(k => k.ClosePrice).HasPrecision(28, 10);
        builder.Property(k => k.Volume).HasPrecision(28, 10);
        builder.Property(k => k.QuoteVolume).HasPrecision(28, 10);
        builder.Property(k => k.TakerBuyBaseVolume).HasPrecision(28, 10);
        builder.Property(k => k.TakerBuyQuoteVolume).HasPrecision(28, 10);

        builder.HasIndex(k => new { k.Symbol, k.Interval, k.OpenTime })
            .IsUnique()
            .HasDatabaseName("UX_Klines_Symbol_Interval_OpenTime");

        builder.HasIndex(k => new { k.Symbol, k.Interval, k.IsClosed, k.OpenTime })
            .HasDatabaseName("IX_Klines_ReadPath");

        builder.Ignore(k => k.DomainEvents);
    }
}
