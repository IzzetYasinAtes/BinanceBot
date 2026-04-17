using BinanceBot.Domain.Balances;
using BinanceBot.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BinanceBot.Infrastructure.Persistence.Configurations;

public sealed class VirtualBalanceConfiguration : IEntityTypeConfiguration<VirtualBalance>
{
    public void Configure(EntityTypeBuilder<VirtualBalance> builder)
    {
        builder.ToTable("VirtualBalances", t =>
        {
            t.HasCheckConstraint("CK_VirtualBalances_ModeIdParity", "[Id] = [Mode]");
        });

        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).ValueGeneratedNever();

        builder.Property(b => b.Mode).HasConversion<int>().IsRequired();

        builder.Property(b => b.StartingBalance).HasPrecision(28, 10);
        builder.Property(b => b.CurrentBalance).HasPrecision(28, 10);
        builder.Property(b => b.Equity).HasPrecision(28, 10);

        builder.Property(b => b.IterationId).IsRequired();
        builder.Property(b => b.StartedAt).IsRequired();
        builder.Property(b => b.LastResetAt);
        builder.Property(b => b.ResetCount).IsRequired();
        builder.Property(b => b.UpdatedAt).IsRequired();

        builder.Ignore(b => b.DomainEvents);

        var seedTimestamp = new DateTimeOffset(2026, 4, 17, 0, 0, 0, TimeSpan.Zero);
        builder.HasData(
            SeedRow(TradingMode.Paper, 100m, seedTimestamp),
            SeedRow(TradingMode.LiveTestnet, 0m, seedTimestamp),
            SeedRow(TradingMode.LiveMainnet, 0m, seedTimestamp));
    }

    private static object SeedRow(TradingMode mode, decimal startingBalance, DateTimeOffset now) => new
    {
        Id = (int)mode,
        Mode = mode,
        StartingBalance = startingBalance,
        CurrentBalance = startingBalance,
        Equity = startingBalance,
        IterationId = DeterministicIterationId(mode),
        StartedAt = now,
        LastResetAt = (DateTimeOffset?)null,
        ResetCount = 0,
        UpdatedAt = now,
    };

    // Seed Guid must be deterministic so HasData doesn't cause repeated migration churn.
    private static Guid DeterministicIterationId(TradingMode mode) => mode switch
    {
        TradingMode.Paper       => new Guid("11111111-1111-1111-1111-111111111111"),
        TradingMode.LiveTestnet => new Guid("22222222-2222-2222-2222-222222222222"),
        TradingMode.LiveMainnet => new Guid("33333333-3333-3333-3333-333333333333"),
        _ => throw new InvalidOperationException($"Unknown mode {mode}"),
    };
}
