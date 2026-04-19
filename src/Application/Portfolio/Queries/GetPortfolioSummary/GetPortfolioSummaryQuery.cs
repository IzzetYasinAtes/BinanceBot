using Ardalis.Result;
using BinanceBot.Application.Abstractions;
using BinanceBot.Domain.Common;
using BinanceBot.Domain.Positions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BinanceBot.Application.Portfolio.Queries.GetPortfolioSummary;

/// <summary>
/// Loop 19 — single-shot portfolio snapshot for the dashboard. Replaces a stack of
/// per-metric queries on the UI side and reconciles two values that drifted apart
/// in earlier loops:
///   - <c>CurrentCash</c>     : VirtualBalance.CurrentBalance (settled cash).
///   - <c>TrueEquity</c>      : CurrentCash + sum(open MTM unrealized PnL) + open cost basis sign-aware.
///
/// Previous /api/balances handler returned both columns equal because
/// VirtualBalance.Equity is a pre-aggregated MTM that races partial fills (Loop
/// 17/18 trace). This query keeps the two views explicit so the UI can display
/// "cash" vs "equity" without conflating them.
/// </summary>
public sealed record GetPortfolioSummaryQuery(TradingMode Mode = TradingMode.Paper)
    : IRequest<Result<PortfolioSummaryDto>>;

public sealed record PortfolioSummaryDto(
    TradingMode Mode,
    string ModeName,
    decimal StartingBalance,
    decimal CurrentCash,
    decimal OpenPositionsValue,
    decimal TrueEquity,
    decimal RealizedPnl24h,
    decimal RealizedPnlAllTime,
    decimal UnrealizedPnlTotal,
    decimal NetPnl,
    decimal NetPnlPct,
    decimal TotalCommissionPaid,
    decimal NetProfitAfterFees,
    int WinningTrades,
    int LosingTrades,
    decimal WinRate,
    int OpenPositionCount,
    int ClosedTradeCount,
    DateTimeOffset AsOfUtc);

public sealed class GetPortfolioSummaryQueryHandler
    : IRequestHandler<GetPortfolioSummaryQuery, Result<PortfolioSummaryDto>>
{
    /// <summary>
    /// Paper fee assumption mirrors PaperFillSimulator (0.10% taker). Live modes
    /// will eventually surface the real OrderFill.Commission column once account
    /// sync ships; today we still derive a uniform proxy from CumulativeQuoteQty
    /// to keep the dashboard consistent across modes.
    /// </summary>
    private const decimal CommissionRate = 0.001m;

    private readonly IApplicationDbContext _db;
    private readonly IClock _clock;

    public GetPortfolioSummaryQueryHandler(IApplicationDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Result<PortfolioSummaryDto>> Handle(
        GetPortfolioSummaryQuery request, CancellationToken ct)
    {
        var mode = request.Mode;
        var now = _clock.UtcNow;

        var balance = await _db.VirtualBalances
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == (int)mode, ct);

        if (balance is null)
        {
            return Result<PortfolioSummaryDto>.NotFound($"VirtualBalance for mode {mode} not seeded.");
        }

        // Open positions — unrealized PnL + cost basis. We compute the MTM value
        // as cost-basis + unrealized so the UI reads a "what's parked in open
        // positions" figure that ties out against Position.MarkPrice.
        var openAgg = await _db.Positions
            .AsNoTracking()
            .Where(p => p.Mode == mode && p.Status == PositionStatus.Open)
            .GroupBy(p => 1)
            .Select(g => new
            {
                Count = g.Count(),
                CostBasis = g.Sum(p => p.AverageEntryPrice * p.Quantity),
                Unrealized = g.Sum(p => p.UnrealizedPnl),
            })
            .FirstOrDefaultAsync(ct);

        var openCount = openAgg?.Count ?? 0;
        var openCostBasis = openAgg?.CostBasis ?? 0m;
        var unrealizedTotal = openAgg?.Unrealized ?? 0m;
        var openPositionsValue = openCostBasis + unrealizedTotal;

        var trueEquity = balance.CurrentBalance + openPositionsValue;

        // Closed positions — realized PnL aggregates and win/loss bucketing.
        var todayStart = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);

        var closedAgg = await _db.Positions
            .AsNoTracking()
            .Where(p => p.Mode == mode && p.Status == PositionStatus.Closed)
            .GroupBy(p => 1)
            .Select(g => new
            {
                Count = g.Count(),
                RealizedAllTime = g.Sum(p => p.RealizedPnl),
                Winning = g.Count(p => p.RealizedPnl > 0m),
                Losing = g.Count(p => p.RealizedPnl < 0m),
            })
            .FirstOrDefaultAsync(ct);

        var realizedAllTime = closedAgg?.RealizedAllTime ?? 0m;
        var winningTrades = closedAgg?.Winning ?? 0;
        var losingTrades = closedAgg?.Losing ?? 0;
        var closedCount = closedAgg?.Count ?? 0;

        var realizedToday = await _db.Positions
            .AsNoTracking()
            .Where(p => p.Mode == mode
                     && p.Status == PositionStatus.Closed
                     && p.ClosedAt != null
                     && p.ClosedAt >= todayStart)
            .SumAsync(p => (decimal?)p.RealizedPnl, ct) ?? 0m;

        // Commission proxy — uniform 0.10% on every executed quote-quantity for
        // the mode. Paper fills already include this in RealizedPnl, so we
        // surface it as a transparency metric, not a second deduction.
        var executedQuote = await _db.Orders
            .AsNoTracking()
            .Where(o => o.Mode == mode && o.CumulativeQuoteQty > 0m)
            .SumAsync(o => (decimal?)o.CumulativeQuoteQty, ct) ?? 0m;
        var totalCommission = executedQuote * CommissionRate;

        var netPnl = realizedAllTime + unrealizedTotal;
        var netPct = balance.StartingBalance > 0m
            ? netPnl / balance.StartingBalance
            : 0m;

        // Commissions are already netted into RealizedPnl/UnrealizedPnl by the
        // paper simulator, so NetProfitAfterFees == NetPnl. The field is kept
        // explicit to make the intent obvious to UI consumers.
        var netAfterFees = netPnl;

        var decided = winningTrades + losingTrades;
        var winRate = decided > 0
            ? (decimal)winningTrades / decided
            : 0m;

        var dto = new PortfolioSummaryDto(
            Mode: mode,
            ModeName: mode.ToString(),
            StartingBalance: balance.StartingBalance,
            CurrentCash: balance.CurrentBalance,
            OpenPositionsValue: openPositionsValue,
            TrueEquity: trueEquity,
            RealizedPnl24h: realizedToday,
            RealizedPnlAllTime: realizedAllTime,
            UnrealizedPnlTotal: unrealizedTotal,
            NetPnl: netPnl,
            NetPnlPct: netPct,
            TotalCommissionPaid: totalCommission,
            NetProfitAfterFees: netAfterFees,
            WinningTrades: winningTrades,
            LosingTrades: losingTrades,
            WinRate: winRate,
            OpenPositionCount: openCount,
            ClosedTradeCount: closedCount,
            AsOfUtc: now);

        return Result.Success(dto);
    }
}
