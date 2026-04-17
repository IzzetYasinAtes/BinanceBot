using BinanceBot.Domain.MarketData;
using BinanceBot.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BinanceBot.Infrastructure.Persistence.Configurations;

public sealed class BookTickerConfiguration : IEntityTypeConfiguration<BookTicker>
{
    public void Configure(EntityTypeBuilder<BookTicker> builder)
    {
        builder.ToTable("BookTickers");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).ValueGeneratedOnAdd();

        builder.Property(b => b.Symbol)
            .HasConversion(s => s.Value, v => Symbol.From(v))
            .HasMaxLength(20)
            .IsRequired();

        builder.HasIndex(b => b.Symbol).IsUnique().HasDatabaseName("UX_BookTickers_Symbol");

        builder.Property(b => b.BidPrice).HasPrecision(28, 10);
        builder.Property(b => b.BidQuantity).HasPrecision(28, 10);
        builder.Property(b => b.AskPrice).HasPrecision(28, 10);
        builder.Property(b => b.AskQuantity).HasPrecision(28, 10);
        builder.Property(b => b.UpdateId).IsConcurrencyToken();
        builder.Property(b => b.UpdatedAt).IsRequired();
    }
}
