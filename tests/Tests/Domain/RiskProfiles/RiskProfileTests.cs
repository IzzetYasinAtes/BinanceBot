using BinanceBot.Domain.Common;
using BinanceBot.Domain.RiskProfiles;
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
