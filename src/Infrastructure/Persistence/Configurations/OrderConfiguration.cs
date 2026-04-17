using BinanceBot.Domain.Orders;
using BinanceBot.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BinanceBot.Infrastructure.Persistence.Configurations;

public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Orders");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedOnAdd();

        builder.Property(o => o.ClientOrderId).HasMaxLength(36).IsRequired();
        builder.HasIndex(o => o.ClientOrderId)
            .IsUnique()
            .HasDatabaseName("UX_Orders_ClientOrderId");

        builder.Property(o => o.ExchangeOrderId);
        builder.HasIndex(o => o.ExchangeOrderId)
            .HasFilter("[ExchangeOrderId] IS NOT NULL")
            .HasDatabaseName("IX_Orders_ExchangeOrderId");

        builder.Property(o => o.Symbol)
            .HasConversion(s => s.Value, v => Symbol.From(v))
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(o => o.Side).HasConversion<int>();
        builder.Property(o => o.Type).HasConversion<int>();
        builder.Property(o => o.TimeInForce).HasConversion<int>();
        builder.Property(o => o.Status).HasConversion<int>();

        builder.Property(o => o.Quantity).HasPrecision(28, 10);
        builder.Property(o => o.Price).HasPrecision(28, 10);
        builder.Property(o => o.StopPrice).HasPrecision(28, 10);
        builder.Property(o => o.ExecutedQuantity).HasPrecision(28, 10);
        builder.Property(o => o.CumulativeQuoteQty).HasPrecision(28, 10);

        builder.HasMany(o => o.Fills)
            .WithOne()
            .HasForeignKey(f => f.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(Order.Fills))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(o => new { o.Symbol, o.Status, o.UpdatedAt })
            .HasDatabaseName("IX_Orders_Symbol_Status_Updated");
        builder.HasIndex(o => o.StrategyId)
            .HasDatabaseName("IX_Orders_StrategyId");

        builder.Ignore(o => o.DomainEvents);
    }
}

public sealed class OrderFillConfiguration : IEntityTypeConfiguration<OrderFill>
{
    public void Configure(EntityTypeBuilder<OrderFill> builder)
    {
        builder.ToTable("OrderFills");
        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).ValueGeneratedOnAdd();

        builder.Property(f => f.Price).HasPrecision(28, 10);
        builder.Property(f => f.Quantity).HasPrecision(28, 10);
        builder.Property(f => f.Commission).HasPrecision(28, 10);
        builder.Property(f => f.CommissionAsset).HasMaxLength(16).IsRequired();

        builder.HasIndex(f => new { f.OrderId, f.ExchangeTradeId })
            .IsUnique()
            .HasDatabaseName("UX_OrderFills_Order_ExchangeTrade");
    }
}
