using BinanceBot.Application.Abstractions;
using BinanceBot.Application.Abstractions.Trading;
using BinanceBot.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace BinanceBot.Infrastructure.Trading;

/// <summary>
/// VirtualBalance-backed equity provider (ADR-0011 §11.3 + decision-sizing.md Commit 5).
///
/// Loop 12 reform: split into two reads.
///   - <see cref="GetEquityAsync"/> returns mark-to-market equity (Equity
///     column, falls back to CurrentBalance when unset). Used by sizing.
///   - <see cref="GetRealizedEquityAsync"/> returns CurrentBalance only —
///     the realized cash after closed fills, ignoring any open-position
///     unrealized PnL. Used by <c>EquityPeakTrackerService</c> to stop
///     intraday spikes from inflating PeakEquity (Loop 6/7/9/10/11 trace).
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

    public async Task<decimal> GetRealizedEquityAsync(TradingMode mode, CancellationToken ct)
    {
        // ADR-0006 mainnet guard mirror — same rationale as GetEquityAsync.
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

        // CurrentBalance is mutated only by VirtualBalance.ApplyFill (realized
        // delta). ApplyUnrealized writes to Equity, never CurrentBalance —
        // exactly the semantic Loop 12 needs for peak tracking.
        return balance.CurrentBalance;
    }
}
