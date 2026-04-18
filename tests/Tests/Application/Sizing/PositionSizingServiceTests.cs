using BinanceBot.Application.Abstractions.Trading;
using BinanceBot.Application.Sizing;
using FluentAssertions;

namespace BinanceBot.Tests.Application.Sizing;

/// <summary>
/// 12 sizing scenarios mandated by decision-sizing.md §1.6 + Reviewer Checklist §11.
/// All numbers are derived in the comment above each Fact for traceability.
/// </summary>
public class PositionSizingServiceTests
{
    private readonly IPositionSizingService _sut = new PositionSizingService();

    // --- Happy path: ATR-style stop sizes BTC long ---------------------------------------
    // equity 100, riskPct 0.01 -> riskAmount 1
    // entry 30000 with slip 0.0005 -> effEntry 30015
    // stop 29850 -> stopDistance 150 (vs raw entry — ADR keeps geometry on the raw side)
    // qtyByRisk = 1 / 150 = 0.00666... -> stepped at 0.00001 -> 0.00666
    // qtyByCap  = (100 * 0.15) / 30015 = 15 / 30015 = 0.0004997...
    // min => qtyByCap wins (cap clamps risk-based sizing)
    [Fact]
    public void HappyBtcWithStop_CapClampsRisk_StepsToStepSize()
    {
        var input = new PositionSizingInput(
            Equity: 100m,
            EntryPrice: 30000m,
            StopDistance: 150m,
            RiskPct: 0.01m,
            MaxPositionPct: 0.15m,
            MinNotional: 5m,
            StepSize: 0.00001m,
            MinQty: 0.00001m,
            SlippagePct: 0.0005m);

        var result = _sut.Calculate(input);

        result.SkipReason.Should().BeNull();
        result.Quantity.Should().Be(0.00049m);
        result.NotionalEstimate.Should().BeApproximately(0.00049m * 30015m, 0.000001m);
    }

    // --- Happy path BNB no stop: cap branch only -----------------------------------------
    // equity 100, riskPct 0.01, no stop (0)
    // entry 600, slip 0 -> effEntry 600
    // qtyByCap = 15 / 600 = 0.025 -> stepped at 0.001 -> 0.025
    [Fact]
    public void HappyBnbNoStop_CollapsesToCapBranch()
    {
        var input = new PositionSizingInput(
            Equity: 100m, EntryPrice: 600m, StopDistance: 0m,
            RiskPct: 0.01m, MaxPositionPct: 0.15m,
            MinNotional: 5m, StepSize: 0.001m, MinQty: 0.001m,
            SlippagePct: 0m);

        var result = _sut.Calculate(input);

        result.SkipReason.Should().BeNull();
        result.Quantity.Should().Be(0.025m);
        result.NotionalEstimate.Should().Be(15m);
    }

    // --- XRP step alignment --------------------------------------------------------------
    // equity 100, entry 0.65, slip 0
    // qtyByCap = 15 / 0.65 = 23.0769... -> stepped at 1 -> 23
    [Fact]
    public void HappyXrp_StepFloorsToInteger()
    {
        var input = new PositionSizingInput(
            Equity: 100m, EntryPrice: 0.65m, StopDistance: 0m,
            RiskPct: 0.01m, MaxPositionPct: 0.15m,
            MinNotional: 5m, StepSize: 1m, MinQty: 1m,
            SlippagePct: 0m);

        var result = _sut.Calculate(input);

        result.SkipReason.Should().BeNull();
        result.Quantity.Should().Be(23m);
        result.NotionalEstimate.Should().Be(23m * 0.65m);
    }

    // --- Skip: notional below MIN_NOTIONAL floor -----------------------------------------
    // equity 5 -> qtyByCap (5*0.15)/30000 = 0.000025 -> snapped 0.00002
    // notional = 0.00002 * 30000 = 0.6 < 5 -> skip
    [Fact]
    public void SkipMinNotionalFloor_NotionalBelowFilter()
    {
        var input = new PositionSizingInput(
            Equity: 5m, EntryPrice: 30000m, StopDistance: 0m,
            RiskPct: 0.01m, MaxPositionPct: 0.15m,
            MinNotional: 5m, StepSize: 0.00001m, MinQty: 0.00001m,
            SlippagePct: 0m);

        var result = _sut.Calculate(input);

        result.Quantity.Should().Be(0m);
        result.SkipReason.Should().Be("min_notional_floor");
    }

    // --- Skip: stepped qty below MIN_QTY -------------------------------------------------
    // equity 0.50 -> qtyByCap = 0.075 / 30000 = 0.0000025 -> floor at 0.00001 -> 0
    [Fact]
    public void SkipQtyBelowMinQty_AfterStepFloor()
    {
        var input = new PositionSizingInput(
            Equity: 0.5m, EntryPrice: 30000m, StopDistance: 0m,
            RiskPct: 0.01m, MaxPositionPct: 0.15m,
            MinNotional: 5m, StepSize: 0.00001m, MinQty: 0.00001m,
            SlippagePct: 0m);

        var result = _sut.Calculate(input);

        result.Quantity.Should().Be(0m);
        result.SkipReason.Should().BeOneOf("qty_below_min_qty", "qty_non_positive");
    }

    // --- Skip: equity zero ---------------------------------------------------------------
    [Fact]
    public void SkipEquityZero_ShortCircuits()
    {
        var input = new PositionSizingInput(
            Equity: 0m, EntryPrice: 30000m, StopDistance: 150m,
            RiskPct: 0.01m, MaxPositionPct: 0.15m,
            MinNotional: 5m, StepSize: 0.00001m, MinQty: 0.00001m,
            SlippagePct: 0m);

        var result = _sut.Calculate(input);

        result.Quantity.Should().Be(0m);
        result.SkipReason.Should().Be("equity_zero");
    }

    // --- Cap clamps qty when risk is huge ------------------------------------------------
    // equity 1000, riskPct 0.02, stopDistance 1 (very tight)
    // qtyByRisk = 20 / 1 = 20
    // qtyByCap  = (1000 * 0.10) / 30000 = 100/30000 = 0.00333... -> stepped 0.00333
    [Fact]
    public void CapClampsQtyWhenRiskTooLarge()
    {
        var input = new PositionSizingInput(
            Equity: 1000m, EntryPrice: 30000m, StopDistance: 1m,
            RiskPct: 0.02m, MaxPositionPct: 0.10m,
            MinNotional: 5m, StepSize: 0.00001m, MinQty: 0.00001m,
            SlippagePct: 0m);

        var result = _sut.Calculate(input);

        result.SkipReason.Should().BeNull();
        result.Quantity.Should().Be(0.00333m);
    }

    // --- Slippage increases effective price (and thus reduces qty) -----------------------
    [Fact]
    public void Slippage_RaisesEffectiveEntryAndShrinksCapQty()
    {
        var noSlip = _sut.Calculate(new PositionSizingInput(
            Equity: 100m, EntryPrice: 600m, StopDistance: 0m,
            RiskPct: 0.01m, MaxPositionPct: 0.15m,
            MinNotional: 5m, StepSize: 0.00001m, MinQty: 0.00001m,
            SlippagePct: 0m));

        var withSlip = _sut.Calculate(new PositionSizingInput(
            Equity: 100m, EntryPrice: 600m, StopDistance: 0m,
            RiskPct: 0.01m, MaxPositionPct: 0.15m,
            MinNotional: 5m, StepSize: 0.00001m, MinQty: 0.00001m,
            SlippagePct: 0.01m)); // 1% slippage

        withSlip.Quantity.Should().BeLessThan(noSlip.Quantity);
    }

    // --- StepFloor truncates correctly ---------------------------------------------------
    [Fact]
    public void StepFloor_DropsRemainder_DoesNotRoundUp()
    {
        var input = new PositionSizingInput(
            Equity: 100m, EntryPrice: 100m, StopDistance: 0m,
            RiskPct: 0.01m, MaxPositionPct: 0.15m,
            MinNotional: 1m, StepSize: 0.1m, MinQty: 0.1m,
            SlippagePct: 0m);

        // qtyByCap = 15 / 100 = 0.15 -> floor to 0.1
        var result = _sut.Calculate(input);

        result.SkipReason.Should().BeNull();
        result.Quantity.Should().Be(0.1m);
    }

    // --- Zero stop distance falls back to cap branch -------------------------------------
    [Fact]
    public void ZeroStopDistance_FallsBackToCap()
    {
        var input = new PositionSizingInput(
            Equity: 100m, EntryPrice: 600m, StopDistance: 0m,
            RiskPct: 0.01m, MaxPositionPct: 0.15m,
            MinNotional: 5m, StepSize: 0.001m, MinQty: 0.001m,
            SlippagePct: 0m);

        var result = _sut.Calculate(input);

        // Cap branch: 15 / 600 = 0.025
        result.Quantity.Should().Be(0.025m);
    }

    // --- Invalid entry price ------------------------------------------------------------
    [Fact]
    public void InvalidEntryPrice_ShortCircuitsToSkip()
    {
        var input = new PositionSizingInput(
            Equity: 100m, EntryPrice: 0m, StopDistance: 150m,
            RiskPct: 0.01m, MaxPositionPct: 0.15m,
            MinNotional: 5m, StepSize: 0.00001m, MinQty: 0.00001m,
            SlippagePct: 0m);

        var result = _sut.Calculate(input);

        result.Quantity.Should().Be(0m);
        result.SkipReason.Should().Be("entry_invalid");
    }

    // --- Loop 14 widened envelope: %40 cap on $100 equity ($40 notional ceiling) ----------
    // research-paper-live-and-sizing.md §B/C4 promotes the default cap from 15% to 40% so
    // a $100 paper account can finally take a meaningful BNB position. With BNB at 600,
    // 0% slippage, no stop:
    //   qtyByCap = (100 * 0.40) / 600 = 0.0666... -> floor at 0.001 -> 0.066
    //   notional = 0.066 * 600 = 39.6 (just under the 40 ceiling, matches step floor)
    [Fact]
    public void Loop14_FortyPctCapOn100Equity_BnbSizesNearFortyDollarsNotional()
    {
        var input = new PositionSizingInput(
            Equity: 100m, EntryPrice: 600m, StopDistance: 0m,
            RiskPct: 0.02m, MaxPositionPct: 0.40m,
            MinNotional: 5m, StepSize: 0.001m, MinQty: 0.001m,
            SlippagePct: 0m);

        var result = _sut.Calculate(input);

        result.SkipReason.Should().BeNull();
        result.Quantity.Should().Be(0.066m);
        result.NotionalEstimate.Should().Be(39.6m);
        // Sanity: the new envelope must NOT let notional slip past the 40 ceiling.
        result.NotionalEstimate.Should().BeLessThanOrEqualTo(40m);
    }

    // --- Risk branch beats cap when stop is loose ---------------------------------------
    // equity 1000, riskPct 0.01 -> riskAmount 10
    // stopDistance 5000 (loose) -> qtyByRisk = 10 / 5000 = 0.002
    // qtyByCap = 100 / 30000 = 0.00333
    // min => qtyByRisk = 0.002 -> stepped 0.002
    [Fact]
    public void RiskBranchWins_WhenStopLooseEnough()
    {
        var input = new PositionSizingInput(
            Equity: 1000m, EntryPrice: 30000m, StopDistance: 5000m,
            RiskPct: 0.01m, MaxPositionPct: 0.10m,
            MinNotional: 5m, StepSize: 0.00001m, MinQty: 0.00001m,
            SlippagePct: 0m);

        var result = _sut.Calculate(input);

        result.SkipReason.Should().BeNull();
        result.Quantity.Should().Be(0.002m);
    }
}
