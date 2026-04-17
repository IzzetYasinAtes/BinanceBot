using BinanceBot.Domain.RiskProfiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BinanceBot.Infrastructure.Persistence.Configurations;

public sealed class RiskProfileConfiguration : IEntityTypeConfiguration<RiskProfile>
{
    public void Configure(EntityTypeBuilder<RiskProfile> builder)
    {
        builder.ToTable("RiskProfiles");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.RiskPerTradePct).HasPrecision(10, 4);
        builder.Property(r => r.MaxPositionSizePct).HasPrecision(10, 4);
        builder.Property(r => r.MaxDrawdown24hPct).HasPrecision(10, 4);
        builder.Property(r => r.MaxDrawdownAllTimePct).HasPrecision(10, 4);
        builder.Property(r => r.MaxConsecutiveLosses);

        builder.Property(r => r.RiskPerTradeCap).HasPrecision(10, 4);
        builder.Property(r => r.MaxPositionCap).HasPrecision(10, 4);
        builder.Property(r => r.CapsAdminNote).HasMaxLength(500);

        builder.Property(r => r.CircuitBreakerStatus).HasConversion<int>();
        builder.Property(r => r.CircuitBreakerReason).HasMaxLength(500);

        builder.Property(r => r.RealizedPnl24h).HasPrecision(28, 10);
        builder.Property(r => r.RealizedPnlAllTime).HasPrecision(28, 10);
        builder.Property(r => r.PeakEquity).HasPrecision(28, 10);
        builder.Property(r => r.CurrentDrawdownPct).HasPrecision(10, 4);

        builder.Ignore(r => r.DomainEvents);

        var seedTimestamp = new DateTimeOffset(2026, 4, 17, 0, 0, 0, TimeSpan.Zero);
        builder.HasData(
            SeedRow(BinanceBot.Domain.Common.TradingMode.Paper, seedTimestamp),
            SeedRow(BinanceBot.Domain.Common.TradingMode.LiveTestnet, seedTimestamp),
            SeedRow(BinanceBot.Domain.Common.TradingMode.LiveMainnet, seedTimestamp));
    }

    private static object SeedRow(
        BinanceBot.Domain.Common.TradingMode mode,
        DateTimeOffset now) => new
        {
            Id = RiskProfile.IdFor(mode),
            RiskPerTradePct = 0.01m,
            MaxPositionSizePct = 0.10m,
            MaxDrawdown24hPct = 0.05m,
            MaxDrawdownAllTimePct = 0.25m,
            MaxConsecutiveLosses = 3,
            CircuitBreakerStatus = CircuitBreakerStatus.Healthy,
            ConsecutiveLosses = 0,
            RealizedPnl24h = 0m,
            RealizedPnlAllTime = 0m,
            PeakEquity = 0m,
            CurrentDrawdownPct = 0m,
            UpdatedAt = now,
        };
}
