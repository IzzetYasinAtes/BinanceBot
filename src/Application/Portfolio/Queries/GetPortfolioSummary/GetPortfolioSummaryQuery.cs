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
    // ADR-0018 + Loop 23 review fix — Commission metriği artık `Orders.Commission`
    // sütunundan SUM ediliyor. PaperFeeSimulator BNB discount durumuna göre
    // (0.075% veya 0.10%) her fill'de gerçek commission yazıyor, o yüzden
    // hardcoded 0.10% proxy kaldırıldı. Reviewer BLOCKER: BNB discount aktifken
    // UI %33 yanlış gösteriyordu.
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
        // Loop 84 cash-bug fix: `OpenCommission` is summed separately so the
        // ledger-derived cash formula can deduct it explicitly (entry fee leaves
        // cash before exit fee shows up on Close).
        var openAgg = await _db.Positions
            .AsNoTracking()
            .Where(p => p.Mode == mode && p.Status == PositionStatus.Open)
            .GroupBy(p => 1)
            .Select(g => new
            {
                Count = g.Count(),
                CostBasis = g.Sum(p => p.AverageEntryPrice * p.Quantity),
                Unrealized = g.Sum(p => p.UnrealizedPnl),
                OpenCommission = g.Sum(p => p.EntryCommission),
            })
            .FirstOrDefaultAsync(ct);

        var openCount = openAgg?.Count ?? 0;
        var openCostBasis = openAgg?.CostBasis ?? 0m;
        var unrealizedTotal = openAgg?.Unrealized ?? 0m;
        var openCommission = openAgg?.OpenCommission ?? 0m;
        var openPositionsValue = openCostBasis + unrealizedTotal;

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
                ClosedCommission = g.Sum(p => p.EntryCommission + p.ExitCommission),
                Winning = g.Count(p => p.RealizedPnl > 0m),
                Losing = g.Count(p => p.RealizedPnl < 0m),
            })
            .FirstOrDefaultAsync(ct);

        var realizedAllTime = closedAgg?.RealizedAllTime ?? 0m;
        var closedCommission = closedAgg?.ClosedCommission ?? 0m;
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

        // ADR-0020 §20.8 — commission source is the Position aggregate's own
        // quote-denominated ledger (EntryCommission + ExitCommission). The previous
        // SUM over OrderFills.Commission mixed BUY fees (base asset) and SELL fees
        // (quote asset) into one numeric column — the result was not meaningful in
        // any single currency (see loop 32 diagnosis §4.4 for the exact breakage).
        // Open positions yet to close still contribute via EntryCommission so the
        // metric is comparable across iterations regardless of close timing.
        var totalCommission = closedCommission + openCommission;

        // ---- Loop 84 cash-bug fix (phantom +$157 on UI Toplam Net K/Z) ---------
        // Single source of truth: the position ledger. Even when
        // VirtualBalance.CurrentBalance drifts (paper iterations crossing a
        // PaperFillSimulator semantic change, or an in-flight reset interleaving
        // with fills), the ledger-derived cash + equity are exact:
        //
        //   ledgerCash = StartingBalance
        //              + Σ closed.RealizedPnl     // already net of entry+exit fees
        //              − Σ open.cost-basis         // settled for the open BUY leg
        //              − Σ open.EntryCommission    // entry fee for still-open positions
        //
        //   trueEquity = ledgerCash + Σ open.cost-basis + Σ open.UnrealizedPnl
        //              = StartingBalance
        //              + Σ closed.RealizedPnl
        //              + Σ open.UnrealizedPnl
        //              − Σ open.EntryCommission
        //
        // This formulation never reads CurrentBalance/Equity for the headline
        // numbers, so the UI heals automatically from a stale snapshot. The
        // VirtualBalance row is still maintained for backwards-compat callers
        // (sizer/peak-tracker read the realized formulation directly from
        // Positions; see EquitySnapshotProvider).
        var ledgerCash = balance.StartingBalance
                       + realizedAllTime
                       - openCostBasis
                       - openCommission;
        var trueEquity = ledgerCash + openPositionsValue;

        // Cash-grounded net: trueEquity - starting. Component metrikleri
        // (RealizedPnlAllTime, UnrealizedPnlTotal) UI'a ayrı alanlar olarak gider
        // ama hero "Net K/Z" bu tek tutarlılık sağlanan değerle gösterilir.
        var netPnl = trueEquity - balance.StartingBalance;
        var netPct = balance.StartingBalance > 0m
            ? netPnl / balance.StartingBalance
            : 0m;

        // ADR-0020 §20.8 — Position.Close artık RealizedPnl'i fee-net yazıyor.
        // NetProfitAfterFees realizedAllTime + unrealizedTotal − openCommission:
        // kapalı işlemlerin fee-net realized + açık pozisyonların unrealized −
        // open BUY-leg fees (henüz Close çağrılmadığından RealizedPnl içine
        // henüz girmedi). Cash-grounded netPnl ile birebir eşit olmalı.
        var netAfterFees = realizedAllTime + unrealizedTotal - openCommission;

        var decided = winningTrades + losingTrades;
        var winRate = decided > 0
            ? (decimal)winningTrades / decided
            : 0m;

        var dto = new PortfolioSummaryDto(
            Mode: mode,
            ModeName: mode.ToString(),
            StartingBalance: balance.StartingBalance,
            CurrentCash: ledgerCash,
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
