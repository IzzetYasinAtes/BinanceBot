using BinanceBot.Application.Abstractions;
using BinanceBot.Application.Abstractions.Trading;
using BinanceBot.Domain.Common;
using BinanceBot.Domain.Positions;
using Microsoft.EntityFrameworkCore;

namespace BinanceBot.Infrastructure.Trading;

/// <summary>
/// VirtualBalance-backed equity provider (ADR-0011 §11.3 + decision-sizing.md Commit 5).
///
/// Loop 12 reform: split into two reads.
///   - <see cref="GetEquityAsync"/> returns mark-to-market equity (Equity
///     column, falls back to CurrentBalance when unset). Used by sizing.
///   - <see cref="GetRealizedEquityAsync"/> returns realized notional equity:
///     cash + cost-basis of currently open positions, ignoring any
///     open-position unrealized PnL. Used by <c>EquityPeakTrackerService</c>
///     to stop intraday spikes from inflating PeakEquity (Loop 6/7/9/10/11 trace).
///
/// Loop 14/15 fix: <see cref="GetRealizedEquityAsync"/> previously returned
/// only <c>VirtualBalance.CurrentBalance</c> (cash). When an order fills, cash
/// drops by the cost basis (it is "locked" inside the open position) but no
/// realized loss has occurred — the position can still close at break-even.
/// Cash-only therefore manufactured a fake drawdown the moment any position
/// opened (Loop 14 t30: $100 cash -> BUY $39.97 -> $60.03 cash -> 40% DD ->
/// CB Tripped sahte). The correct realized notional equity adds the
/// cost-basis of every still-open position back to cash. Unrealized PnL is
/// still excluded (Loop 12 invariant): cost-basis uses
/// <see cref="Position.AverageEntryPrice"/>, never <c>MarkPrice</c>.
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

        // Loop 14/15: cash + cost-basis of open positions. Cash is mutated
        // only by VirtualBalance.ApplyFill (realized delta + locks cost basis
        // when a position opens). Adding cost-basis back reconstructs the
        // notional capital available before the lock — i.e. realized equity.
        // AverageEntryPrice (NOT MarkPrice) keeps unrealized PnL out, which
        // is the Loop 12 invariant the peak tracker depends on.
        var openPositionsCostBasis = await _db.Positions
            .AsNoTracking()
            .Where(p => p.Mode == mode && p.Status == PositionStatus.Open)
            .SumAsync(p => p.AverageEntryPrice * p.Quantity, ct);

        return balance.CurrentBalance + openPositionsCostBasis;
    }
}
