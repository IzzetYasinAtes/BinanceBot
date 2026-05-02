using BinanceBot.Application.Abstractions;
using BinanceBot.Domain.Common;
using BinanceBot.Domain.Positions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BinanceBot.Infrastructure.Risk;

/// <summary>
/// Loop 81 — boot-time integrity probe for the Paper VirtualBalance / Positions split.
///
/// The Loop 81 reset bug surfaced because <c>VirtualBalances.LastResetAt</c> moved on
/// <c>POST /api/papertrade/reset</c> but the <c>Positions</c> table still held trades from
/// the previous iteration. <see cref="ResetPaperBalanceCommandHandler"/> now wipes both
/// in a single transaction, but databases that were reset before the fix shipped will
/// still carry the inconsistency. This checker emits a single warning at startup so
/// operators can decide whether to issue a fresh reset; it never mutates state.
///
/// Detection rule: Paper has a non-null <see cref="Domain.Balances.VirtualBalance.LastResetAt"/>
/// AND there exists a Paper Position with <c>OpenedAt &lt; LastResetAt</c>. The check is
/// scoped to <see cref="TradingMode.Paper"/> because that is the only mode that supports
/// reset (see <see cref="Domain.Balances.VirtualBalance.ResetForIteration"/>).
/// </summary>
public sealed class VirtualBalanceConsistencyChecker : IHostedService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<VirtualBalanceConsistencyChecker> _logger;

    public VirtualBalanceConsistencyChecker(
        IServiceProvider services,
        ILogger<VirtualBalanceConsistencyChecker> logger)
    {
        _services = services;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

            var paper = await db.VirtualBalances
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == (int)TradingMode.Paper, cancellationToken);

            if (paper?.LastResetAt is not DateTimeOffset lastReset)
            {
                // Fresh seed (LastResetAt == null) — nothing to check.
                return;
            }

            // Stale row detection: any pre-reset Position survived the reset path.
            // Use Min(OpenedAt) so the warning carries actionable diagnostics.
            var stalePositionStats = await db.Positions
                .AsNoTracking()
                .Where(p => p.Mode == TradingMode.Paper && p.OpenedAt < lastReset)
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    Count = g.Count(),
                    OldestOpenedAt = g.Min(p => p.OpenedAt),
                    OpenCount = g.Count(p => p.Status == PositionStatus.Open),
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (stalePositionStats is null || stalePositionStats.Count == 0)
            {
                return;
            }

            _logger.LogWarning(
                "VirtualBalance/Positions inconsistency detected: paper LastResetAt={LastReset} "
                + "but {StaleCount} Position(s) predate the reset (oldest OpenedAt={Oldest}, openStill={OpenCount}). "
                + "UI 'Toplam Net K/Z' will mix iterations. Operator action: POST /api/papertrade/reset to wipe.",
                lastReset, stalePositionStats.Count, stalePositionStats.OldestOpenedAt, stalePositionStats.OpenCount);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Host shutting down during startup — no warning, no action.
        }
        catch (Exception ex)
        {
            // Probe-only — never block boot. Connection/transient failures get a single warning.
            _logger.LogWarning(ex, "VirtualBalanceConsistencyChecker probe failed");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
