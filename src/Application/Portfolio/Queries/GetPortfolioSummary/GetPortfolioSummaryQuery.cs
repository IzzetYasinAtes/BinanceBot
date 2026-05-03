using Ardalis.Result;
using BinanceBot.Application.Abstractions;
using BinanceBot.Domain.Common;
using BinanceBot.Domain.Positions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BinanceBot.Application.Portfolio.Queries.GetPortfolioSummary;

/// <summary>
/// Loop 19 → Loop 93 — single-shot portfolio snapshot for the dashboard.
///
/// Loop 93 §2 — Futures cash semantics (ADR-0025). Spot-era ledger formula
/// (<c>start + realized − open notional − open commission</c>) silently
/// double-counted notional once the simulator switched to Futures: notional
/// is held as MARGIN (not deducted from cash), only commission and realized
/// PnL move the wallet. The handler now reads <c>VirtualBalance.WalletBalance</c>
/// directly as the authoritative cash figure (paper sim already maintains it via
/// <see cref="VirtualBalance.ApplyFill"/> / <see cref="VirtualBalance.ReturnMarginAndApplyPnl"/>):
///   - <c>CurrentCash</c>         : <c>WalletBalance</c> — single source of truth.
///   - <c>OpenPositionsValue</c>  : Σ <c>MarkPrice × Quantity</c> (notional view, sign-agnostic).
///   - <c>TrueEquity</c>          : <c>WalletBalance + Σ UnrealizedPnl</c>.
///   - <c>NetPnl</c>              : <c>TrueEquity − StartingBalance</c>.
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

        // Loop 93 §2 — open positions aggregate. <c>NotionalValue</c> reads
        // <c>MarkPrice × Quantity</c> so the UI shows current market exposure
        // (sign-agnostic — Long and Short both lock the same magnitude of
        // margin). <c>OpenCommission</c> stays in the projection only for the
        // <c>TotalCommissionPaid</c> hero metric; it is NOT deducted from cash
        // any more (Spot-era artefact removed — Futures keeps notional in
        // <see cref="VirtualBalance.AllocatedMargin"/>, not in cash).
        var openAgg = await _db.Positions
            .AsNoTracking()
            .Where(p => p.Mode == mode && p.Status == PositionStatus.Open)
            .GroupBy(p => 1)
            .Select(g => new
            {
                Count = g.Count(),
                NotionalValue = g.Sum(p => p.MarkPrice * p.Quantity),
                Unrealized = g.Sum(p => p.UnrealizedPnl),
                OpenCommission = g.Sum(p => p.EntryCommission),
            })
            .FirstOrDefaultAsync(ct);

        var openCount = openAgg?.Count ?? 0;
        var openPositionsValue = openAgg?.NotionalValue ?? 0m;
        var unrealizedTotal = openAgg?.Unrealized ?? 0m;
        var openCommission = openAgg?.OpenCommission ?? 0m;

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

        // ---- Loop 93 §2 — Futures cash semantics (ADR-0025) -----------------
        // Single source of truth: <c>VirtualBalance.WalletBalance</c>. Paper
        // sim's <see cref="FuturesPaperFillSimulator"/> already routes the
        // realized cash delta through <see cref="VirtualBalance.ApplyFill"/>
        // (commission − notional convention is wallet-correct for Futures
        // because the simulator's <c>realizedCash</c> is the net wallet effect
        // of the leg). Open notional lives in <see cref="VirtualBalance.AllocatedMargin"/>,
        // NOT in cash, so the Spot-era subtraction (cash − notional − commission)
        // produced a $80 ghost reading while the actual wallet held $399.99 in
        // Loop 92 t30 — exactly the snapshot drift this fix eliminates.
        //
        //   currentCash       = WalletBalance
        //   trueEquity        = WalletBalance + Σ open.UnrealizedPnl
        //   openPositionsValue = Σ open.MarkPrice × Quantity   (UI display only)
        //   netPnl            = trueEquity − StartingBalance
        var walletCash = balance.WalletBalance;
        var trueEquity = walletCash + unrealizedTotal;

        var netPnl = trueEquity - balance.StartingBalance;
        var netPct = balance.StartingBalance > 0m
            ? netPnl / balance.StartingBalance
            : 0m;

        // ADR-0020 §20.8 — Position.Close artık RealizedPnl'i fee-net yazıyor.
        // NetProfitAfterFees realizedAllTime + unrealizedTotal − openCommission:
        // kapalı işlemlerin fee-net realized + açık pozisyonların unrealized −
        // open entry fees (henüz Close çağrılmadığından RealizedPnl içine
        // girmedi). Futures cash formülünde netPnl = realized + unrealized −
        // openCommission invariant'ı korunur (commission tek defa wallet'tan
        // düşer, RealizedPnl close'da fee-net yazılır).
        var netAfterFees = realizedAllTime + unrealizedTotal - openCommission;

        var decided = winningTrades + losingTrades;
        var winRate = decided > 0
            ? (decimal)winningTrades / decided
            : 0m;

        var dto = new PortfolioSummaryDto(
            Mode: mode,
            ModeName: mode.ToString(),
            StartingBalance: balance.StartingBalance,
            CurrentCash: walletCash,
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
