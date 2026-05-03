using BinanceBot.Domain.Common;
using BinanceBot.Domain.Positions;
using BinanceBot.Domain.Positions.Events;
using BinanceBot.Domain.ValueObjects;
using FluentAssertions;

namespace BinanceBot.Tests.Domain.Positions;

/// <summary>
/// Loop 75 — break-even SL move (binance-expert spec). Position aggregate
/// MoveStopToBreakEven domain method invariants:
/// idempotency, no-improve guard, audit timestamp, raised event.
/// </summary>
public class PositionBreakEvenSlTests
{
    private static readonly DateTimeOffset T0 = new(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);

    private static Position OpenLong(decimal entry, decimal? initialStop)
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

    [Fact]
    public void MoveStopToBreakEven_FirstCall_AppliesAndStampsTimestamp()
    {
        var pos = OpenLong(entry: 30000m, initialStop: 29700m);

        var result = pos.MoveStopToBreakEven(newStop: 30006m, asOf: T0.AddSeconds(90));

        result.Should().Be(MoveStopResult.Applied);
        pos.StopPrice.Should().Be(30006m);
        pos.BreakEvenAppliedAt.Should().Be(T0.AddSeconds(90));
        pos.UpdatedAt.Should().Be(T0.AddSeconds(90));

        // Audit event raised — preserves previous stop for downstream consumers.
        pos.DomainEvents.Should().ContainSingle(e => e is PositionStopMovedEvent);
        var evt = (PositionStopMovedEvent)pos.DomainEvents.Single(e => e is PositionStopMovedEvent);
        evt.PreviousStopPrice.Should().Be(29700m);
        evt.NewStopPrice.Should().Be(30006m);
        evt.PositionId.Should().Be(pos.Id);
        evt.Symbol.Should().Be("BTCUSDT");
        evt.Reason.Should().Be("break_even_move");
    }

    [Fact]
    public void MoveStopToBreakEven_SecondCall_ReturnsAlreadyAppliedNoOp()
    {
        var pos = OpenLong(entry: 30000m, initialStop: 29700m);
        pos.MoveStopToBreakEven(newStop: 30006m, asOf: T0.AddSeconds(90));
        var stopAfterFirst = pos.StopPrice;
        var stampAfterFirst = pos.BreakEvenAppliedAt;

        // Caller, daha agresif bir stop denese bile aggregate koruma altında.
        var result = pos.MoveStopToBreakEven(newStop: 30030m, asOf: T0.AddMinutes(5));

        result.Should().Be(MoveStopResult.AlreadyApplied);
        pos.StopPrice.Should().Be(stopAfterFirst, "BE move bir kez uygulanır — ikinci çağrı no-op");
        pos.BreakEvenAppliedAt.Should().Be(stampAfterFirst, "audit timestamp idempotent");
    }

    [Fact]
    public void MoveStopToBreakEven_NewStopBelowCurrent_ReturnsNotImproving()
    {
        // Mevcut stop entry'den 0.5% altta (29850 — ATR-based SL); BE offset entry'nin
        // 0.02% üstünde olmalı (30006). Eğer caller yanlışlıkla mevcut stop'tan daha
        // kötü bir stop verirse aggregate riski azaltmamalı (BE move "improve only").
        var pos = OpenLong(entry: 30000m, initialStop: 30100m);  // synthetic invariant

        var result = pos.MoveStopToBreakEven(newStop: 29800m, asOf: T0.AddSeconds(90));

        result.Should().Be(MoveStopResult.NotImproving);
        pos.StopPrice.Should().Be(30100m, "no-improve attempt aggregate'ı mutate etmez");
        pos.BreakEvenAppliedAt.Should().BeNull("BE flag set edilmedi — bir sonraki tick yeniden değerlendirebilir");
    }

    [Fact]
    public void MoveStopToBreakEven_NullInitialStop_AppliesUnconditionally()
    {
        // StopPrice hiç set edilmemiş bir pozisyon (stop-less time-stop only) için
        // ilk BE move improvement guard'ından muaf — direkt apply.
        var pos = OpenLong(entry: 30000m, initialStop: null);

        var result = pos.MoveStopToBreakEven(newStop: 30006m, asOf: T0.AddSeconds(90));

        result.Should().Be(MoveStopResult.Applied);
        pos.StopPrice.Should().Be(30006m);
    }

    [Fact]
    public void MoveStopToBreakEven_NonPositiveStop_Throws()
    {
        var pos = OpenLong(entry: 30000m, initialStop: 29700m);

        var act = () => pos.MoveStopToBreakEven(newStop: 0m, asOf: T0.AddSeconds(90));

        act.Should().Throw<DomainException>()
            .WithMessage("Break-even stop price must be positive.");
    }

    [Fact]
    public void MoveStopToBreakEven_OnClosedPosition_Throws()
    {
        var pos = OpenLong(entry: 30000m, initialStop: 29700m);
        pos.Close(exitPrice: 30200m, reason: "tp", now: T0.AddMinutes(10));

        var act = () => pos.MoveStopToBreakEven(newStop: 30006m, asOf: T0.AddMinutes(11));

        act.Should().Throw<DomainException>()
            .WithMessage("*not open*");
    }
}
