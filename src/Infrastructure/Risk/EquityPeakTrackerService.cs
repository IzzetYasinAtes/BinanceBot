using BinanceBot.Application.Abstractions;
using BinanceBot.Application.Abstractions.Trading;
using BinanceBot.Domain.Common;
using BinanceBot.Domain.RiskProfiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BinanceBot.Infrastructure.Risk;

/// <summary>
/// Loop 7 bug #17 fix — intraday <see cref="RiskProfile.PeakEquity"/> tracker.
///
/// Background: <c>PositionClosedRiskHandler</c> previously updated PeakEquity only on
/// trade close. In Loop 6 the Paper account spiked to $195.25 (large unrealized PnL)
/// at t30 and unwound to $56.10 at t90 — peak was never recorded because no trade
/// closed during the spike. The drawdown formula then computed
/// <c>(99.89 − 56.10)/99.89 = 43.84%</c> against the stale prior peak and tripped the
/// circuit breaker even though the true intraday drawdown vs the live peak was much
/// larger (~71%) and the CB should have fired earlier.
///
/// This service ticks every 30s (same cadence as <c>MarkToMarketWorker</c> /
/// <c>StopLossMonitorService</c>), pulls the current equity for every non-Mainnet
/// <see cref="TradingMode"/> via <see cref="IEquitySnapshotProvider"/>, and ratchets
/// <see cref="RiskProfile.PeakEquity"/> upward via the domain method
/// <see cref="RiskProfile.RecordPeakEquitySnapshot"/>. It does not raise events, alter
/// <see cref="RiskProfile.ConsecutiveLosses"/>, or trip the circuit breaker — those
/// remain on the close path through <see cref="RecordTradeOutcomeCommand"/>.
///
/// LiveMainnet is skipped because <see cref="EquitySnapshotProvider"/> always returns
/// 0 there (ADR-0006 mainnet guard), and <c>RecordPeakEquitySnapshot</c> short-circuits
/// on <c>currentEquity ≤ 0</c> anyway.
///
/// Loop 8 bug #19 update: <see cref="RiskProfile.RecordPeakEquitySnapshot"/> now also
/// trips the drawdown circuit-breaker when the configured ceiling is breached. The
/// tracker still does not pull that lever directly — it just persists, and the
/// <see cref="ApplicationDbContext"/> publisher fans the
/// <c>CircuitBreakerTrippedEvent</c> out to <c>CircuitBreakerTrippedHandler</c>
/// (kill-switch deactivates active strategies).
///
/// Loop 12 reform: switched from <c>GetEquityAsync</c> (mark-to-market) to
/// <c>GetRealizedEquityAsync</c> (cash balance after closed fills). Loops 6/7/9/10/11
/// each tripped the CB on a transient unrealized spike that never realized; e.g. Loop 11
/// Paper $100 → unrealized peak $164 → equity unwound to $99 → DD 39% → false trip.
/// PeakEquity now ratchets only on realized cash growth, so intraday open-position
/// volatility cannot inflate the peak.
/// </summary>
public sealed class EquityPeakTrackerService : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(30);

    private static readonly TradingMode[] TrackedModes =
    {
        TradingMode.Paper,
        TradingMode.LiveTestnet,
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EquityPeakTrackerService> _logger;

    public EquityPeakTrackerService(
        IServiceScopeFactory scopeFactory,
        ILogger<EquityPeakTrackerService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "EquityPeakTracker started, tick={Sec}s modes={Modes}",
            TickInterval.TotalSeconds,
            string.Join(",", TrackedModes));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "EquityPeakTracker tick failed");
            }

            try
            {
                await Task.Delay(TickInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    internal async Task TickOnceAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var equityProvider = scope.ServiceProvider.GetRequiredService<IEquitySnapshotProvider>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var dirty = 0;
        foreach (var mode in TrackedModes)
        {
            // Loop 12: realized-only equity — ignore unrealized swings (see class doc).
            var equity = await equityProvider.GetRealizedEquityAsync(mode, ct);
            if (equity <= 0m)
            {
                continue;
            }

            var profileId = RiskProfile.IdFor(mode);
            var profile = await db.RiskProfiles
                .FirstOrDefaultAsync(r => r.Id == profileId, ct);
            if (profile is null)
            {
                continue;
            }

            var peakBefore = profile.PeakEquity;
            var statusBefore = profile.CircuitBreakerStatus;
            profile.RecordPeakEquitySnapshot(equity, clock.UtcNow);

            if (profile.PeakEquity > peakBefore)
            {
                _logger.LogInformation(
                    "PEAK-TRACK mode={Mode} equity={Equity} peak {Before}→{After}",
                    mode, equity, peakBefore, profile.PeakEquity);
                dirty++;
            }
            else if (profile.CurrentDrawdownPct > 0m)
            {
                _logger.LogDebug(
                    "PEAK-TRACK mode={Mode} equity={Equity} peak={Peak} dd={DD}",
                    mode, equity, profile.PeakEquity, profile.CurrentDrawdownPct);
                dirty++;
            }

            // Loop 8 bug #19: surface tracker-driven CB trips so operators don't have
            // to grep across two log streams to reconstruct an intraday breach.
            if (statusBefore == CircuitBreakerStatus.Healthy
                && profile.CircuitBreakerStatus == CircuitBreakerStatus.Tripped)
            {
                _logger.LogWarning(
                    "CB-AUDIT tripped-by-tracker mode={Mode} reason={Reason} equity={Equity} peak={Peak} dd={DD} dd24h={DD24h} ddAll={DDAll}",
                    mode, profile.CircuitBreakerReason, equity, profile.PeakEquity,
                    profile.CurrentDrawdownPct, profile.MaxDrawdown24hPct, profile.MaxDrawdownAllTimePct);
                dirty++;
            }
        }

        if (dirty > 0)
        {
            await db.SaveChangesAsync(ct);
        }
    }
}
