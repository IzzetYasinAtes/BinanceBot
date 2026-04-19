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
///   - <see cref="GetRealizedEquityAsync"/> returns realized PnL-based equity:
///     <c>StartingBalance + sum(closed position RealizedPnl)</c>. Used by
///     <c>EquityPeakTrackerService</c> so PeakEquity only ratchets up on
///     real, settled gains (Loop 6/7/9/10/11/14/15/16 trace).
///
/// Loop 17 reform: cash + open cost-basis (Loop 14/15) drifted upward across
/// loops because partial fills, fee accruals, and AddFill averaging let the
/// "cost basis" computation race the "cash" mutation. Loop 15 cash+cost
/// reached $178, Loop 16 hit $263 — both wildly above the $100 baseline,
/// inflating PeakEquity and arming a permanent fake drawdown. The PnL-based
/// formulation is timing-immune: <c>StartingBalance</c> is constant per
/// iteration, and <c>RealizedPnl</c> is only written on <c>Position.Close</c>
/// (single atomic write, no intermediate state). Open positions, mark price,
/// fees-in-flight, and partial fills all become irrelevant to the peak
/// tracker — exactly the property the circuit breaker needs.
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

        // Loop 17: PnL-based tracking. Baseline (StartingBalance, constant per
        // iteration) + cumulative realized PnL from closed positions.
        // Open-position mark/cost-basis fluctuations are intentionally
        // excluded — only fully closed trades move the equity proxy. This
        // is timing-immune: RealizedPnl is written exactly once on Close,
        // so no race with cash mutations or partial fills can inflate the
        // value. PeakEquity therefore can never exceed StartingBalance + true
        // realized gains, eliminating the Loop 14/15/16 inflation drift
        // ($100 -> $178 -> $263 baseline regression).
        var realizedSum = await _db.Positions
            .AsNoTracking()
            .Where(p => p.Mode == mode && p.Status == PositionStatus.Closed)
            .SumAsync(p => p.RealizedPnl, ct);

        return balance.StartingBalance + realizedSum;
    }

    /// <summary>
    /// Loop 19 — sizing equity uses the realized-only formulation. Symmetric to
    /// <see cref="GetRealizedEquityAsync"/> by design: the PeakEquity tracker
    /// (Loop 17) and the position sizer (Loop 19) both need the property that
    /// open-position mark fluctuations cannot expand the budget that justifies
    /// further entries. Loop 18 trace: VirtualBalance.Equity drifted from $100
    /// to $316 once two short positions were marked aggressively, which fed a
    /// $126 cap (0.40 * $316) and produced 123-XRP fan-outs on a $100 paper
    /// account. Anchoring sizing on StartingBalance + realized PnL bounds the
    /// cap at $40 (0.40 * $100) until trades actually settle.
    /// </summary>
    public Task<decimal> GetSizingEquityAsync(TradingMode mode, CancellationToken ct)
        => GetRealizedEquityAsync(mode, ct);
}
