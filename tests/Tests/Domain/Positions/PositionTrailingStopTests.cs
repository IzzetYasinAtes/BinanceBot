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

    /// <summary>
    /// Loop 111 fix #4 regresyon: Long pos'ta peak ilerlediğinde StopPrice da
    /// trailing formülüyle yansıtılır (peak × (1 − trailPct)). BE armed sonrası
    /// her yeni high yeni trailing stop'u devreye sokar — ASLA aşağı çekilmez.
    /// Loop 110 ADA örneği: peak $0.26540, AMA StopPrice $0.25934 sabit
    /// (BE move'dan kalan eski değer) → mark $0.26000 düştüğünde safety net
    /// SL hit olarak görmüyordu, çünkü stop trailing'i yansıtmıyordu.
    /// </summary>
    [Fact]
    public void UpdatePeakAndCheckTrailing_LongNewHigh_BeArmed_AdvancesStopPrice()
    {
        // entry=100, BE move stop'u 100.10'a taşıdı (entry × 1.001).
        var pos = OpenLongWithBeApplied(entry: 100m, beStop: 100.10m);
        pos.StopPrice.Should().Be(100.10m);

        // peak=102.5 (entry+%2.5). trailPct=0.003 → newStop = 102.5 × 0.997 = 102.1925.
        var result = pos.UpdatePeakAndCheckTrailing(102.5m, 0.003m, T0.AddMinutes(15));

        result.Should().Be(TrailingResult.PeakUpdated);
        pos.ExtremeMarkPrice.Should().Be(102.5m);
        pos.StopPrice.Should().Be(102.5m * 0.997m,
            "Loop 111: Long peak ilerleyince trailing stop formülü uygulanır");

        var stopMoved = pos.DomainEvents.OfType<PositionStopMovedEvent>()
            .LastOrDefault(e => e.Reason == "trailing_stop_advance");
        stopMoved.Should().NotBeNull();
        stopMoved!.NewStopPrice.Should().Be(102.5m * 0.997m);
        stopMoved.PreviousStopPrice.Should().Be(100.10m);
    }

    /// <summary>
    /// Loop 111 fix #4: Long peak ilerlemese bile (in-band tick) StopPrice
    /// değişmez. Sadece *yeni high* trailing'i ileri taşır.
    /// </summary>
    [Fact]
    public void UpdatePeakAndCheckTrailing_LongInBandTick_DoesNotChangeStopPrice()
    {
        var pos = OpenLongWithBeApplied(entry: 100m, beStop: 100.10m);
        pos.UpdatePeakAndCheckTrailing(102.5m, 0.003m, T0.AddMinutes(15));  // peak=102.5
        var stopAfterFirstAdvance = pos.StopPrice;

        // mark 102.0 — peak'in altında ama trail eşiğinin üstünde (in-band).
        pos.UpdatePeakAndCheckTrailing(102.0m, 0.003m, T0.AddMinutes(20));

        pos.StopPrice.Should().Be(stopAfterFirstAdvance,
            "in-band tick StopPrice'ı değiştirmez (peak yatık kalır)");
    }

    /// <summary>
    /// Loop 111 fix #4: BE armed değilse trailing stop yansıtılmaz (BE move
    /// trailing'i "arm" eder). Peak yine güncellenir AMA StopPrice null/değişmez.
    /// </summary>
    [Fact]
    public void UpdatePeakAndCheckTrailing_LongBeNotArmed_DoesNotAdvanceStopPrice()
    {
        var pos = OpenLong(entry: 100m, initialStop: 99m);  // BE move yapılmadı
        pos.BreakEvenAppliedAt.Should().BeNull();

        pos.UpdatePeakAndCheckTrailing(102.5m, 0.003m, T0.AddMinutes(15));

        pos.ExtremeMarkPrice.Should().Be(102.5m, "peak yine güncellenir");
        pos.StopPrice.Should().Be(99m, "BE armed değilse stop değişmez");
    }

    /// <summary>
    /// Loop 111 fix #4: Short trough ilerlediğinde StopPrice aşağı taşınır
    /// (trough × (1 + trailPct)). BE armed sonrası.
    /// </summary>
    [Fact]
    public void UpdatePeakAndCheckTrailing_ShortNewTrough_BeArmed_AdvancesStopPriceDown()
    {
        // Short entry=100, BE move stop'u 99.90'a indirdi (entry × 0.999).
        var pos = Position.Open(
            Symbol.From("BTCUSDT"),
            TradeDirection.Short,
            quantity: 0.01m,
            entryPrice: 100m,
            stopPrice: 101m,
            strategyId: 1,
            mode: TradingMode.Paper,
            now: T0);
        pos.MoveStopToBreakEven(99.90m, T0.AddSeconds(60));
        pos.StopPrice.Should().Be(99.90m);

        // İlk tick: trough=98.0 (sentinel 0 ⇒ mark trough'a yatırılır).
        // newStop = 98.0 × 1.003 = 98.294.
        var result = pos.UpdatePeakAndCheckTrailing(98.0m, 0.003m, T0.AddMinutes(15));

        result.Should().Be(TrailingResult.PeakUpdated);
        pos.ExtremeMarkPrice.Should().Be(98.0m);
        pos.StopPrice.Should().Be(98.0m * 1.003m,
            "Loop 111: Short trough ilerleyince trailing stop aşağı taşınır");
    }

    /// <summary>
    /// Loop 111 fix #4: Short trough geri YUKARI çıkarsa (trough'tan uzaklaşma)
    /// StopPrice asla yukarı çekilmez (regression guard — BE move'dan kalan
    /// daha düşük stop korunur).
    /// </summary>
    [Fact]
    public void UpdatePeakAndCheckTrailing_ShortTroughReverts_StopPriceNeverWidens()
    {
        var pos = Position.Open(
            Symbol.From("BTCUSDT"),
            TradeDirection.Short,
            quantity: 0.01m,
            entryPrice: 100m,
            stopPrice: 101m,
            strategyId: 1,
            mode: TradingMode.Paper,
            now: T0);
        pos.MoveStopToBreakEven(99.90m, T0.AddSeconds(60));
        pos.UpdatePeakAndCheckTrailing(98.0m, 0.003m, T0.AddMinutes(15));  // trough=98, stop=98.294
        var tightStop = pos.StopPrice;

        // mark 98.5 — trough'tan uzaklaştı. trough refresh OLMAZ, stop da değişmez.
        pos.UpdatePeakAndCheckTrailing(98.5m, 0.003m, T0.AddMinutes(20));

        pos.ExtremeMarkPrice.Should().Be(98.0m, "trough yatık kalır");
        pos.StopPrice.Should().Be(tightStop, "stop asla yukarı çekilmez");
    }
}
