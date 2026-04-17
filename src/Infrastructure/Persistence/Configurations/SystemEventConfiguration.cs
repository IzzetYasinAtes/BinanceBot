using BinanceBot.Domain.SystemEvents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BinanceBot.Infrastructure.Persistence.Configurations;

public sealed class SystemEventConfiguration : IEntityTypeConfiguration<SystemEvent>
{
    public void Configure(EntityTypeBuilder<SystemEvent> builder)
    {
        builder.ToTable("SystemEvents");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        builder.Property(e => e.EventType).HasMaxLength(100).IsRequired();
        builder.Property(e => e.Severity).HasConversion<int>();
        builder.Property(e => e.PayloadJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(e => e.Source).HasMaxLength(80).IsRequired();
        builder.Property(e => e.CorrelationId);
        builder.Property(e => e.OccurredAt).IsRequired();

        builder.HasIndex(e => e.OccurredAt).HasDatabaseName("IX_SystemEvents_OccurredAt");
        builder.HasIndex(e => new { e.EventType, e.OccurredAt })
            .HasDatabaseName("IX_SystemEvents_EventType_OccurredAt");
    }
}
