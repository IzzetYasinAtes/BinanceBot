using BinanceBot.Application.Abstractions;
using BinanceBot.Application.Abstractions.Trading;
using BinanceBot.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace BinanceBot.Infrastructure.Trading;

/// <summary>
/// VirtualBalance-backed equity provider (ADR-0011 §11.3 + decision-sizing.md Commit 5).
/// </summary>
public sealed class EquitySnapshotProvider : IEquitySnapshotProvider
{
    private readonly IApplicationDbContext _db;

    public EquitySnapshotProvider(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<decimal> GetEquityAsync(TradingMode mode, CancellationToken ct)
    {
        // ADR-0006 mainnet guard — never compute sizing for real-money orders here.
        if (mode == TradingMode.LiveMainnet)
        {
            return 0m;
        }

        var balance = await _db.VirtualBalances
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == (int)mode, ct);
        if (balance is null)
        {
            return 0m;
        }

        // Paper       : Equity is updated by VirtualBalance.ApplyFill / ApplyUnrealized (ADR-0008 §8.4).
        // LiveTestnet : VirtualBalance row may be 0 until account-sync ships (Loop 4+).
        return balance.Equity > 0m ? balance.Equity : balance.CurrentBalance;
    }
}
