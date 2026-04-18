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

        // NOTE: Binance Spot API ClientOrderId max is 36 chars; we widen our DB column to 64
        // to fit the internal correlation prefix `sig-{StrategyId}-{barUnix}-x-{modeSuffix}`
        // even when StrategyId reaches long.MaxValue (worst case ~37–40 chars). The Mainnet
        // path is currently blocked by ADR-0006; if/when we send a 36+ char cid to Binance
        // we must truncate at the gateway. (ADR-0011 review round 2 — blocker fix.)
        builder.Property(o => o.ClientOrderId).HasMaxLength(64).IsRequired();

        builder.Property(o => o.Mode)
            .HasConversion<int>()
            .IsRequired();

        builder.HasIndex(o => new { o.ClientOrderId, o.Mode })
            .IsUnique()
            .HasDatabaseName("UX_Orders_ClientOrderId_Mode");

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
        // Loop 10 take-profit fix — pure metadata, never sent to Binance for MARKET orders.
        builder.Property(o => o.TakeProfit).HasColumnType("decimal(18,8)");
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
