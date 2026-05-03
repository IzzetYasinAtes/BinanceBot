using BinanceBot.Domain.Common;
using BinanceBot.Domain.Positions;
using BinanceBot.Domain.Positions.Events;
using BinanceBot.Domain.ValueObjects;
using FluentAssertions;

namespace BinanceBot.Tests.Domain.Positions;

/// <summary>
/// Loop 76 — trailing-stop domain method <see cref="Position.UpdatePeakAndCheckTrailing"/>.
/// Üç-state result enum: NotEligible (BE applied null), PeakUpdated (yeni high
/// veya in-band no-op), ExitTriggered (mark &lt; peak × (1 − trailPct) → event raise).
/// </summary>
public class PositionTrailingStopTests
{
    private static readonly DateTimeOffset T0 = new(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);

    private static Position OpenLong(decimal entry, decimal? initialStop = null)
    {
        return Position.Open(
            Symbol.From("BTCUSDT"),
            TradeDirection.Long,
            quantity: 0.01m,
            entryPrice: entry,
            stopPrice: initialStop,
            strategyId: 1,
            mode: TradingMode.Paper,
            now: T0);
    }

    private static Position OpenLongWithBeApplied(decimal entry, decimal beStop, decimal initialStop = 0m)
    {
        var pos = OpenLong(entry, initialStop > 0m ? initialStop : entry * 0.99m);
        // BE applied — trailing'i "arm" et
        pos.MoveStopToBreakEven(beStop, T0.AddSeconds(60));
        return pos;
    }

    [Fact]
    public void UpdatePeakAndCheckTrailing_BeNotApplied_StillUpdatesPeakButDoesNotExit()
    {
        // Loop 94 davranış değişikliği: BE applied yokken peak tracking ARTIK aktif
        // — pozisyon açıldığı andan itibaren extreme refresh edilir. Trailing exit
        // *karar*ı yine BE applied sonrası (beArmed gate). Long ilk tick mark > 0
        // olduğunda peak yatırılır, ExitTriggered dönmez.
        var pos = OpenLong(entry: 95000m, initialStop: 94240m);

        var result = pos.UpdatePeakAndCheckTrailing(
            markPrice: 95400m, trailPct: 0.0015m, asOf: T0.AddMinutes(5));

        result.Should().Be(TrailingResult.PeakUpdated);
        pos.ExtremeMarkPrice.Should().Be(95400m, "Loop 94: peak tracking BE'den bağımsız her zaman aktif");
        pos.DomainEvents.Should().NotContain(e => e is PositionTrailingExitTriggeredEvent);
    }

    [Fact]
    public void UpdatePeakAndCheckTrailing_BeNotApplied_LongPriceDip_DoesNotTriggerExit()
    {
        // Loop 94: BE armed değilken peak güncellenir AMA exit kontrolü yapılmaz.
        // Mark peak'ten %0.5 düşse bile ExitTriggered dönmez.
        var pos = OpenLong(entry: 95000m, initialStop: 94240m);
        pos.UpdatePeakAndCheckTrailing(95600m, 0.0015m, T0.AddMinutes(5));  // peak=95600
        pos.ExtremeMarkPrice.Should().Be(95600m);

        // peak × (1 - 0.0015) = 95456.6 — mark altına düştü ama BE yok ⇒ exit yok.
        var result = pos.UpdatePeakAndCheckTrailing(
            markPrice: 95300m, trailPct: 0.0015m, asOf: T0.AddMinutes(10));

        result.Should().Be(TrailingResult.PeakUpdated, "BE armed değilken exit eval yok");
        pos.ExtremeMarkPrice.Should().Be(95600m, "in-band tick peak'i indirmez");
        pos.DomainEvents.Should().NotContain(e => e is PositionTrailingExitTriggeredEvent);
    }

    [Fact]
    public void UpdatePeakAndCheckTrailing_BeNotApplied_ShortFirstTick_RecordsTrough()
    {
        // Loop 94: Short pozisyon ilk tick — sentinel 0 ⇒ mark trough'a yatırılır,
        // BE armed değilse exit kontrolü yapılmaz, sadece PeakUpdated döner.
        var pos = Position.Open(
            Symbol.From("BTCUSDT"),
            TradeDirection.Short,
            quantity: 0.01m,
            entryPrice: 95000m,
            stopPrice: 95760m,
            strategyId: 1,
            mode: TradingMode.Paper,
            now: T0);

        var result = pos.UpdatePeakAndCheckTrailing(
            markPrice: 94800m, trailPct: 0.0015m, asOf: T0.AddMinutes(5));

        result.Should().Be(TrailingResult.PeakUpdated);
        pos.ExtremeMarkPrice.Should().Be(94800m, "Short ilk tick sentinel: mark trough'a yatırılır");
    }

    [Fact]
    public void UpdatePeakAndCheckTrailing_BeApplied_FirstTick_RecordsPeakAndReturnsPeakUpdated()
    {
        var pos = OpenLongWithBeApplied(entry: 95000m, beStop: 95019m);

        // İlk eligible tick — ExtremeMarkPrice default 0, mark > 0 → peak refresh.
        var result = pos.UpdatePeakAndCheckTrailing(
            markPrice: 95400m, trailPct: 0.0015m, asOf: T0.AddMinutes(15));

        result.Should().Be(TrailingResult.PeakUpdated);
        pos.ExtremeMarkPrice.Should().Be(95400m);
        pos.UpdatedAt.Should().Be(T0.AddMinutes(15));
        pos.DomainEvents.Should().NotContain(e => e is PositionTrailingExitTriggeredEvent);
    }

    [Fact]
    public void UpdatePeakAndCheckTrailing_NewHighAfterPeak_RefreshesPeak()
    {
        var pos = OpenLongWithBeApplied(entry: 95000m, beStop: 95019m);
        pos.UpdatePeakAndCheckTrailing(95400m, 0.0015m, T0.AddMinutes(15));  // peak=95400

        var result = pos.UpdatePeakAndCheckTrailing(
            markPrice: 95600m, trailPct: 0.0015m, asOf: T0.AddMinutes(20));

        result.Should().Be(TrailingResult.PeakUpdated);
        pos.ExtremeMarkPrice.Should().Be(95600m, "yeni high peak'i ileri taşır");
    }

    [Fact]
    public void UpdatePeakAndCheckTrailing_MarkInBand_DoesNotMutatePeakAndNoExit()
    {
        // Peak 95600 → trailingStop = 95600 × (1 − 0.0015) = 95456.6
        // Mark 95500 → exit eşiğinin üstünde, peak değişmedi → "still tracking" no-op.
        var pos = OpenLongWithBeApplied(entry: 95000m, beStop: 95019m);
        pos.UpdatePeakAndCheckTrailing(95600m, 0.0015m, T0.AddMinutes(20));

        var result = pos.UpdatePeakAndCheckTrailing(
            markPrice: 95500m, trailPct: 0.0015m, asOf: T0.AddMinutes(22));

        result.Should().Be(TrailingResult.PeakUpdated);
        pos.ExtremeMarkPrice.Should().Be(95600m, "in-band tick peak'i indirmez");
        pos.DomainEvents.Should().NotContain(e => e is PositionTrailingExitTriggeredEvent);
    }

    [Fact]
    public void UpdatePeakAndCheckTrailing_MarkBelowTrail_ReturnsExitTriggeredAndRaisesEvent()
    {
        // binance-expert spec walkthrough: peak=95600, trail=0.0015 → trailingStop=95456.6
        // mark=95300 → 95300 < 95456.6 → ExitTriggered + event.
        var pos = OpenLongWithBeApplied(entry: 95000m, beStop: 95019m);
        pos.UpdatePeakAndCheckTrailing(95600m, 0.0015m, T0.AddMinutes(20));

        var result = pos.UpdatePeakAndCheckTrailing(
            markPrice: 95300m, trailPct: 0.0015m, asOf: T0.AddMinutes(25));

        result.Should().Be(TrailingResult.ExitTriggered);
        pos.ExtremeMarkPrice.Should().Be(95600m, "exit'te peak değişmez (audit için yatık kalır)");

        var evt = pos.DomainEvents.OfType<PositionTrailingExitTriggeredEvent>().Should().ContainSingle().Subject;
        evt.PositionId.Should().Be(pos.Id);
        evt.Symbol.Should().Be("BTCUSDT");
        evt.PeakPrice.Should().Be(95600m);
        evt.MarkPrice.Should().Be(95300m);
        evt.TrailPct.Should().Be(0.0015m);
    }

    [Fact]
    public void UpdatePeakAndCheckTrailing_NonPositiveMark_Throws()
    {
        var pos = OpenLongWithBeApplied(entry: 95000m, beStop: 95019m);

        var act = () => pos.UpdatePeakAndCheckTrailing(0m, 0.0015m, T0.AddMinutes(15));

        act.Should().Throw<DomainException>().WithMessage("Mark price must be positive.");
    }

    [Fact]
    public void UpdatePeakAndCheckTrailing_NonPositiveTrailPct_Throws()
    {
        var pos = OpenLongWithBeApplied(entry: 95000m, beStop: 95019m);

        var act = () => pos.UpdatePeakAndCheckTrailing(95400m, 0m, T0.AddMinutes(15));

        act.Should().Throw<DomainException>().WithMessage("Trail percentage must be positive.");
    }

    [Fact]
    public void UpdatePeakAndCheckTrailing_OnClosedPosition_Throws()
    {
        var pos = OpenLongWithBeApplied(entry: 95000m, beStop: 95019m);
        pos.Close(exitPrice: 95800m, reason: "tp", now: T0.AddMinutes(30));

        var act = () => pos.UpdatePeakAndCheckTrailing(95400m, 0.0015m, T0.AddMinutes(31));

        act.Should().Throw<DomainException>().WithMessage("*not open*");
    }
}
