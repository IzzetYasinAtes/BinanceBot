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

        // Commission total — sum from OrderFills.Commission joined with Orders by
        // mode. PaperFeeSimulator writes the real per-fill commission (BNB
        // discount 0.075% or standard 0.10%) so SUM surfaces the actual cost.
        var totalCommission = await (
            from f in _db.OrderFills.AsNoTracking()
            join o in _db.Orders.AsNoTracking() on f.OrderId equals o.Id
            where o.Mode == mode
            select (decimal?)f.Commission
        ).SumAsync(ct) ?? 0m;

        // Cash-grounded net: trueEquity - starting. Component metrikleri
        // (RealizedPnlAllTime, UnrealizedPnlTotal) UI'a ayrı alanlar olarak gider
        // ama hero "Net K/Z" bu tek tutarlılık sağlanan değerle gösterilir.
        // PaperFillSimulator fee asimetrisi (ADR-0020 tracklenen) nedeniyle gross
        // component toplamı trueEquity'den ~fee_quote kadar sapabilir; tek kaynak cash.
        var netPnl = trueEquity - balance.StartingBalance;
        var netPct = balance.StartingBalance > 0m
            ? netPnl / balance.StartingBalance
            : 0m;

        // ADR-0020 pending: PaperFillSimulator BUY fee base asset cinsinden
        // OrderFill.Commission alanına yazılıyor ama cash'ten düşülmüyor; SELL
        // fee ise quote cinsinden cash'ten düşülüyor. Dolayısıyla Position.
        // RealizedPnl gross kalıyor. NetProfitAfterFees şimdilik netPnl
        // (cash-grounded) ile aynı döner — ADR-0020 ile Position'a
        // EntryCommission/ExitCommission eklenince bu alan RealizedPnl_net
        // toplamına taşınacak.
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
