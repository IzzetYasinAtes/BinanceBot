using BinanceBot.Domain.MarketData;
using BinanceBot.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BinanceBot.Infrastructure.Persistence.Configurations;

public sealed class OrderBookSnapshotConfiguration : IEntityTypeConfiguration<OrderBookSnapshot>
{
    public void Configure(EntityTypeBuilder<OrderBookSnapshot> builder)
    {
        builder.ToTable("OrderBookSnapshots");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedOnAdd();

        builder.Property(s => s.Symbol)
            .HasConversion(s => s.Value, v => Symbol.From(v))
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(s => s.LastUpdateId).IsRequired();
        builder.Property(s => s.BidsJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(s => s.AsksJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(s => s.CapturedAt).IsRequired();

        builder.HasIndex(s => new { s.Symbol, s.CapturedAt })
            .HasDatabaseName("IX_OrderBookSnapshots_Symbol_CapturedAt");
    }
}
