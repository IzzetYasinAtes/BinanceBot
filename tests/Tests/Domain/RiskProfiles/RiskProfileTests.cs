using BinanceBot.Domain.Common;
using BinanceBot.Domain.RiskProfiles;
using BinanceBot.Domain.RiskProfiles.Events;
using FluentAssertions;

namespace BinanceBot.Tests.Domain.RiskProfiles;

/// <summary>
/// Domain-level guarantees for <see cref="RiskProfile"/>. Loop 5 t90 hardening:
/// the CB-bug regression set proves PeakEquity tracks the equity stream
/// (balance + unrealized) — not raw realized-PnL deltas — so drawdown stays
/// in [0, 1) for any healthy paper session.
/// </summary>
public class RiskProfileTests
{
    private static readonly DateTimeOffset T0 = new(2026, 4, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CreateDefault_Paper_HasZeroPeakEquity_AndHealthyCb()
    {
        var rp = RiskProfile.CreateDefault(TradingMode.Paper, T0);

        rp.Id.Should().Be(1);
        rp.PeakEquity.Should().Be(0m);
        rp.CurrentDrawdownPct.Should().Be(0m);
        rp.CircuitBreakerStatus.Should().Be(CircuitBreakerStatus.Healthy);
    }

    [Fact]
    public void RecordTradeOutcome_FirstWinningClose_BumpsPeakEquity_AndDrawdownStaysZero()
    {
        var rp = RiskProfile.CreateDefault(TradingMode.Paper, T0);

        // Paper $100 → $112.27 after a winning trade (the Loop 5 happy path).
        rp.RecordTradeOutcome(realizedPnl: 12.27m, equityAfter: 112.27m, T0.AddMinutes(1));

        rp.PeakEquity.Should().Be(112.27m);
        rp.CurrentDrawdownPct.Should().Be(0m);
        rp.ConsecutiveLosses.Should().Be(0);
        rp.RealizedPnlAllTime.Should().Be(12.27m);
    }

    [Fact]
    public void RecordTradeOutcome_DrawdownIsRatioOfPeakMinusCurrent_OverPeak()
    {
        var rp = RiskProfile.CreateDefault(TradingMode.Paper, T0);

        rp.RecordTradeOutcome(10m, 110m, T0);                 // peak=110
        rp.RecordTradeOutcome(-5m, 105m, T0.AddMinutes(1));   // dd = (110-105)/110 ≈ 0.0454

        rp.PeakEquity.Should().Be(110m);
        rp.CurrentDrawdownPct.Should().BeApproximately(0.04545m, 0.0001m);
    }

    /// <summary>
    /// Regression for Loop 5 CB-bug: when the caller previously passed
    /// realized-PnL deltas (~$0.01) instead of full equity (~$112), PeakEquity
    /// shrank to a near-zero value and the next outcome with `equityAfter≈0`
    /// produced drawdown ≈ 1.0+ (recorded as 1.6494 in the live trace),
    /// tripping the CB on a profitable session. The handler now feeds true
    /// equity, so this scenario is impossible — but we lock in the domain
    /// formula here so any future caller drift is caught at the domain layer.
    /// </summary>
    [Fact]
    public void RecordTradeOutcome_TrueEquityFlow_NeverProducesDrawdownAboveOne()
    {
        var rp = RiskProfile.CreateDefault(TradingMode.Paper, T0);

        // Realistic paper sequence: small wins/losses around a $100 base.
        rp.RecordTradeOutcome(0.50m, 100.50m, T0);
        rp.RecordTradeOutcome(-0.30m, 100.20m, T0.AddMinutes(1));
        rp.RecordTradeOutcome(2.00m, 102.20m, T0.AddMinutes(2));
        rp.RecordTradeOutcome(-1.00m, 101.20m, T0.AddMinutes(3));

        rp.PeakEquity.Should().Be(102.20m);
        rp.CurrentDrawdownPct.Should().BeLessThan(0.05m);
        rp.CurrentDrawdownPct.Should().BeGreaterThanOrEqualTo(0m);
    }

    /// <summary>
    /// Loop 7 bug #17 — <see cref="RiskProfile.RecordPeakEquitySnapshot"/> contract:
    /// ratchets PeakEquity upward and rebases drawdown without altering
    /// ConsecutiveLosses. Loop 8 bug #19 update: it MAY trip the CB if the rebased
    /// drawdown breaches the configured ceiling — this test stays safely under the
    /// 24h ceiling (5%) so the CB stays Healthy.
    /// </summary>
    [Fact]
    public void RecordPeakEquitySnapshot_BelowCeiling_KeepsCbHealthy()
    {
        var rp = RiskProfile.CreateDefault(TradingMode.Paper, T0);

        // Below-ceiling sequence: peak $100, dip to $97 → 3% dd < 5% ceiling.
        rp.RecordPeakEquitySnapshot(100m, T0.AddMinutes(30));
        rp.RecordPeakEquitySnapshot(97m, T0.AddMinutes(60));

        rp.PeakEquity.Should().Be(100m);
        rp.CurrentDrawdownPct.Should().BeApproximately(0.03m, 0.0001m);
        rp.ConsecutiveLosses.Should().Be(0);
        rp.CircuitBreakerStatus.Should().Be(CircuitBreakerStatus.Healthy);
    }

    [Fact]
    public void RecordPeakEquitySnapshot_NonPositiveEquity_IsIgnored()
    {
        var rp = RiskProfile.CreateDefault(TradingMode.Paper, T0);
        rp.RecordPeakEquitySnapshot(100m, T0);

        rp.RecordPeakEquitySnapshot(0m, T0.AddMinutes(1));
        rp.RecordPeakEquitySnapshot(-5m, T0.AddMinutes(2));

        rp.PeakEquity.Should().Be(100m);
        rp.CurrentDrawdownPct.Should().Be(0m);
    }

    [Fact]
    public void RecordPeakEquitySnapshot_BelowCeiling_RaisesNoDomainEvents()
    {
        var rp = RiskProfile.CreateDefault(TradingMode.Paper, T0);
        rp.ClearDomainEvents();

        // 100 → 97 = 3% dd, well below the 5% 24h ceiling.
        rp.RecordPeakEquitySnapshot(100m, T0);
        rp.RecordPeakEquitySnapshot(97m, T0.AddMinutes(1));

        rp.DomainEvents.Should().BeEmpty();
    }

    /// <summary>
    /// Loop 8 bug #19 — primary regression. Live trace observed
    /// CurrentDrawdownPct=0.2925 with MaxDrawdown24hPct=0.05 yet
    /// CircuitBreakerStatus stayed Healthy. Trip evaluation must now run in
    /// RecordPeakEquitySnapshot, use the tighter of the 24h / all-time ceilings,
    /// and raise CircuitBreakerTrippedEvent.
    /// </summary>
    [Fact]
    public void RecordPeakEquitySnapshot_DrawdownBreaches24hCeiling_TripsCb()
    {
        var rp = RiskProfile.CreateDefault(TradingMode.Paper, T0); // 24h=5%, all-time=25%
        rp.ClearDomainEvents();

        // Loop 7 t30 trace: peak $100 → equity $70.75 ⇒ 29.25% dd > 5% ceiling.
        rp.RecordPeakEquitySnapshot(100m, T0);
        rp.RecordPeakEquitySnapshot(70.75m, T0.AddMinutes(30));

        rp.CircuitBreakerStatus.Should().Be(CircuitBreakerStatus.Tripped);
        rp.CircuitBreakerReason.Should().NotBeNullOrWhiteSpace();
        rp.CircuitBreakerReason.Should().Contain("24h");
        rp.CurrentDrawdownPct.Should().BeApproximately(0.2925m, 0.0001m);

        rp.DomainEvents.OfType<CircuitBreakerTrippedEvent>().Should().ContainSingle()
            .Which.ObservedDrawdownPct.Should().BeApproximately(0.2925m, 0.0001m);
    }

    /// <summary>
    /// Loop 8 bug #19 — defensive: if the 24h ceiling is reconfigured higher than the
    /// all-time fuse, the all-time fuse must still trip when crossed.
    /// </summary>
    [Fact]
    public void RecordPeakEquitySnapshot_AllTimeIsTighterThan24h_TripsOnAllTime()
    {
        var rp = RiskProfile.CreateDefault(TradingMode.Paper, T0);
        // Reconfigure: 24h=10% (loose), all-time=8% (tighter than 24h).
        rp.UpdateLimits(0.01m, 0.10m, 0.10m, 0.08m, 3, T0);
        rp.ClearDomainEvents();

        rp.RecordPeakEquitySnapshot(100m, T0);
        rp.RecordPeakEquitySnapshot(91m, T0.AddMinutes(1)); // 9% dd

        rp.CircuitBreakerStatus.Should().Be(CircuitBreakerStatus.Tripped);
        rp.CircuitBreakerReason.Should().Contain("all_time");
        rp.DomainEvents.OfType<CircuitBreakerTrippedEvent>().Should().ContainSingle();
    }

    /// <summary>
    /// Loop 8 bug #19 — once tripped, subsequent snapshots must not re-trip
    /// (no duplicate CircuitBreakerTrippedEvent storm into the kill-switch handler).
    /// </summary>
    [Fact]
    public void RecordPeakEquitySnapshot_AlreadyTripped_DoesNotReRaiseEvent()
    {
        var rp = RiskProfile.CreateDefault(TradingMode.Paper, T0);

        rp.RecordPeakEquitySnapshot(100m, T0);
        rp.RecordPeakEquitySnapshot(70m, T0.AddMinutes(1)); // first trip
        rp.ClearDomainEvents();

        rp.RecordPeakEquitySnapshot(60m, T0.AddMinutes(2)); // deeper, but already tripped

        rp.CircuitBreakerStatus.Should().Be(CircuitBreakerStatus.Tripped);
        rp.DomainEvents.OfType<CircuitBreakerTrippedEvent>().Should().BeEmpty();
    }

    /// <summary>
    /// Loop 8 bug #19 — RecordTradeOutcome was already supposed to evaluate trip
    /// on the all-time ceiling. The fix unifies on the tighter of the two ceilings,
    /// so a -29% close vs a $100 peak must trip on the 24h ceiling (5%) too.
    /// </summary>
    [Fact]
    public void RecordTradeOutcome_DrawdownBreaches24hCeiling_TripsCb()
    {
        var rp = RiskProfile.CreateDefault(TradingMode.Paper, T0);
        rp.RecordTradeOutcome(0m, 100m, T0);          // peak=100
        rp.ClearDomainEvents();

        rp.RecordTradeOutcome(-29.25m, 70.75m, T0.AddMinutes(1));

        rp.CircuitBreakerStatus.Should().Be(CircuitBreakerStatus.Tripped);
        rp.CircuitBreakerReason.Should().Contain("24h");
        rp.DomainEvents.OfType<CircuitBreakerTrippedEvent>().Should().ContainSingle();
    }

    /// <summary>
    /// Loop 8 bug #19 — consecutive-losses still wins when both conditions are true,
    /// because the consec branch runs first (kill-switch reason is more actionable).
    /// </summary>
    [Fact]
    public void RecordTradeOutcome_ConsecAndDrawdownBothBreached_ReasonIsConsec()
    {
        var rp = RiskProfile.CreateDefault(TradingMode.Paper, T0);
        rp.RecordTradeOutcome(0m, 100m, T0);
        rp.RecordTradeOutcome(-1m, 99m, T0.AddMinutes(1));
        rp.RecordTradeOutcome(-1m, 98m, T0.AddMinutes(2));
        rp.ClearDomainEvents();

        // 3rd loss + a -29% slide in one shot.
        rp.RecordTradeOutcome(-29m, 69m, T0.AddMinutes(3));

        rp.CircuitBreakerStatus.Should().Be(CircuitBreakerStatus.Tripped);
        rp.CircuitBreakerReason.Should().StartWith("consecutive_losses=");
        rp.DomainEvents.OfType<CircuitBreakerTrippedEvent>().Should().ContainSingle();
    }

    [Fact]
    public void RecordTradeOutcome_ConsecutiveLosses_IncrementOnLoss_ResetOnWin()
    {
        var rp = RiskProfile.CreateDefault(TradingMode.Paper, T0);

        rp.RecordTradeOutcome(-1m, 99m, T0);
        rp.RecordTradeOutcome(-1m, 98m, T0.AddMinutes(1));
        rp.ConsecutiveLosses.Should().Be(2);

        rp.RecordTradeOutcome(2m, 100m, T0.AddMinutes(2));
        rp.ConsecutiveLosses.Should().Be(0);
    }
}
