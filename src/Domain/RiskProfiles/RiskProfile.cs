using BinanceBot.Domain.Common;
using BinanceBot.Domain.RiskProfiles.Events;

namespace BinanceBot.Domain.RiskProfiles;

public sealed class RiskProfile : AggregateRoot<int>
{
    public const int SingletonId = 1;

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

    public static RiskProfile CreateDefault(DateTimeOffset now) =>
        new()
        {
            Id = SingletonId,
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
    }
}
