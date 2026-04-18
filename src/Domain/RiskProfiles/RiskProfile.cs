using BinanceBot.Domain.Common;
using BinanceBot.Domain.RiskProfiles.Events;

namespace BinanceBot.Domain.RiskProfiles;

public sealed class RiskProfile : AggregateRoot<int>
{
    /// <summary>
    /// RiskProfile is singleton-per-mode (ADR-0008):
    /// Id 1=Paper, 2=LiveTestnet, 3=LiveMainnet. See <see cref="IdFor"/>.
    /// </summary>
    public static int IdFor(TradingMode mode) => (int)mode;

    public decimal RiskPerTradePct { get; private set; }
    public decimal MaxPositionSizePct { get; private set; }
    public decimal MaxDrawdown24hPct { get; private set; }
    public decimal MaxDrawdownAllTimePct { get; private set; }
    public int MaxConsecutiveLosses { get; private set; }

    public decimal? RiskPerTradeCap { get; private set; }
    public decimal? MaxPositionCap { get; private set; }
    public string? CapsAdminNote { get; private set; }

    public CircuitBreakerStatus CircuitBreakerStatus { get; private set; }
    public DateTimeOffset? CircuitBreakerTrippedAt { get; private set; }
    public string? CircuitBreakerReason { get; private set; }

    public int ConsecutiveLosses { get; private set; }
    public decimal RealizedPnl24h { get; private set; }
    public decimal RealizedPnlAllTime { get; private set; }
    public decimal PeakEquity { get; private set; }
    public decimal CurrentDrawdownPct { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private RiskProfile() { }

    public static RiskProfile CreateDefault(TradingMode mode, DateTimeOffset now) =>
        new()
        {
            Id = IdFor(mode),
            RiskPerTradePct = 0.01m,
            MaxPositionSizePct = 0.10m,
            MaxDrawdown24hPct = 0.05m,
            MaxDrawdownAllTimePct = 0.25m,
            MaxConsecutiveLosses = 3,
            CircuitBreakerStatus = CircuitBreakerStatus.Healthy,
            PeakEquity = 0m,
            UpdatedAt = now,
        };

    public void UpdateLimits(
        decimal riskPerTradePct,
        decimal maxPositionSizePct,
        decimal maxDrawdown24hPct,
        decimal maxDrawdownAllTimePct,
        int maxConsecutiveLosses,
        DateTimeOffset now)
    {
        if (riskPerTradePct is <= 0m or > 0.02m)
        {
            throw new DomainException("RiskPerTradePct must be (0, 0.02].");
        }
        if (maxPositionSizePct is <= 0m or > 0.20m)
        {
            throw new DomainException("MaxPositionSizePct must be (0, 0.20].");
        }
        if (maxDrawdown24hPct is <= 0m or > 0.10m)
        {
            throw new DomainException("MaxDrawdown24hPct must be (0, 0.10].");
        }
        if (maxDrawdownAllTimePct is <= 0m or > 0.50m)
        {
            throw new DomainException("MaxDrawdownAllTimePct must be (0, 0.50].");
        }
        if (maxConsecutiveLosses is < 1 or > 10)
        {
            throw new DomainException("MaxConsecutiveLosses must be [1, 10].");
        }

        RiskPerTradePct = riskPerTradePct;
        MaxPositionSizePct = maxPositionSizePct;
        MaxDrawdown24hPct = maxDrawdown24hPct;
        MaxDrawdownAllTimePct = maxDrawdownAllTimePct;
        MaxConsecutiveLosses = maxConsecutiveLosses;
        UpdatedAt = now;

        RaiseDomainEvent(new RiskProfileUpdatedEvent(
            riskPerTradePct, maxPositionSizePct,
            maxDrawdown24hPct, maxDrawdownAllTimePct, maxConsecutiveLosses));
    }

    public void OverrideCaps(
        decimal riskPerTradeCap,
        decimal maxPositionCap,
        string adminNote,
        DateTimeOffset now)
    {
        if (riskPerTradeCap <= 0m || maxPositionCap <= 0m)
        {
            throw new DomainException("Caps must be positive.");
        }
        if (string.IsNullOrWhiteSpace(adminNote))
        {
            throw new DomainException("AdminNote required for cap override.");
        }

        RiskPerTradeCap = riskPerTradeCap;
        MaxPositionCap = maxPositionCap;
        CapsAdminNote = adminNote.Trim();
        UpdatedAt = now;

        RaiseDomainEvent(new RiskCapsOverriddenEvent(riskPerTradeCap, maxPositionCap, CapsAdminNote));
    }

    public void TripCircuitBreaker(string reason, decimal observedDrawdownPct, DateTimeOffset now)
    {
        if (CircuitBreakerStatus == CircuitBreakerStatus.Tripped)
        {
            return;
        }
        CircuitBreakerStatus = CircuitBreakerStatus.Tripped;
        CircuitBreakerReason = reason;
        CircuitBreakerTrippedAt = now;
        UpdatedAt = now;
        RaiseDomainEvent(new CircuitBreakerTrippedEvent(reason, observedDrawdownPct, now));
    }

    public void ResetCircuitBreaker(string adminNote, DateTimeOffset now)
    {
        if (CircuitBreakerStatus == CircuitBreakerStatus.Healthy)
        {
            return;
        }
        if (string.IsNullOrWhiteSpace(adminNote))
        {
            throw new DomainException("AdminNote required for CB reset.");
        }

        CircuitBreakerStatus = CircuitBreakerStatus.Healthy;
        CircuitBreakerReason = null;
        CircuitBreakerTrippedAt = null;
        ConsecutiveLosses = 0;
        UpdatedAt = now;
        RaiseDomainEvent(new CircuitBreakerResetEvent(adminNote.Trim(), now));
    }

    /// <summary>
    /// Loop 6 → Loop 7 bug #17 fix: PeakEquity must follow the live equity stream
    /// (cash + unrealized PnL), not just realized closes. Otherwise an intraday spike
    /// like Loop 6 t30 ($195 unrealized peak → t90 $56 close) computes drawdown
    /// against a stale $99 peak and trips the CB at -43% when the real intraday
    /// drawdown was -71% from a peak that was never recorded.
    ///
    /// Loop 7 → Loop 8 bug #19 fix: this method now also evaluates the drawdown
    /// circuit-breaker. Previously trip evaluation lived only on
    /// <see cref="RecordTradeOutcome"/>, so an intraday equity slide that crossed the
    /// drawdown ceiling without a closed trade would never trip the CB. The tracker
    /// is the only path that observes such slides, so it must own the drawdown trip.
    /// Consecutive-losses trip remains exclusive to <see cref="RecordTradeOutcome"/>.
    /// </summary>
    public void RecordPeakEquitySnapshot(decimal currentEquity, DateTimeOffset now)
    {
        if (currentEquity <= 0m)
        {
            return;
        }

        if (currentEquity > PeakEquity)
        {
            PeakEquity = currentEquity;
            CurrentDrawdownPct = 0m;
        }
        else if (PeakEquity > 0m)
        {
            CurrentDrawdownPct = (PeakEquity - currentEquity) / PeakEquity;
        }

        UpdatedAt = now;

        TripIfDrawdownBreached(now);
    }

    public void RecordTradeOutcome(decimal realizedPnl, decimal equityAfter, DateTimeOffset now)
    {
        if (realizedPnl < 0m)
        {
            ConsecutiveLosses++;
        }
        else if (realizedPnl > 0m)
        {
            ConsecutiveLosses = 0;
        }

        RealizedPnl24h += realizedPnl;
        RealizedPnlAllTime += realizedPnl;

        if (equityAfter > PeakEquity)
        {
            PeakEquity = equityAfter;
            CurrentDrawdownPct = 0m;
        }
        else if (PeakEquity > 0m)
        {
            CurrentDrawdownPct = (PeakEquity - equityAfter) / PeakEquity;
        }

        UpdatedAt = now;
        RaiseDomainEvent(new TradeOutcomeRecordedEvent(realizedPnl, ConsecutiveLosses));

        if (CircuitBreakerStatus == CircuitBreakerStatus.Healthy
            && ConsecutiveLosses >= MaxConsecutiveLosses)
        {
            TripCircuitBreaker(
                $"consecutive_losses={ConsecutiveLosses}",
                CurrentDrawdownPct,
                now);
            return;
        }

        TripIfDrawdownBreached(now);
    }

    /// <summary>
    /// Loop 8 bug #19: evaluates whether <see cref="CurrentDrawdownPct"/> has breached
    /// the tighter of the 24h / all-time drawdown ceilings and trips the CB if so.
    /// Both <see cref="RecordTradeOutcome"/> and <see cref="RecordPeakEquitySnapshot"/>
    /// route through here so trip semantics stay identical regardless of the path that
    /// observed the breach (trade close vs. intraday equity slide).
    /// </summary>
    private void TripIfDrawdownBreached(DateTimeOffset now)
    {
        if (CircuitBreakerStatus != CircuitBreakerStatus.Healthy)
        {
            return;
        }

        // Use the strictest configured ceiling. 24h is normally smaller (e.g. 5%) than
        // the all-time fuse (e.g. 25%), so 24h fires first; but we keep the all-time
        // check too in case a profile is reconfigured with a tighter all-time fuse.
        var ceiling24h = MaxDrawdown24hPct;
        var ceilingAll = MaxDrawdownAllTimePct;
        var effectiveCeiling = ceiling24h < ceilingAll ? ceiling24h : ceilingAll;
        var breachedScope = ceiling24h < ceilingAll ? "24h" : "all_time";

        if (CurrentDrawdownPct >= effectiveCeiling)
        {
            var reason = $"drawdown_{breachedScope}={CurrentDrawdownPct:P2}>={effectiveCeiling:P2}";
            TripCircuitBreaker(reason, CurrentDrawdownPct, now);
        }
    }
}
