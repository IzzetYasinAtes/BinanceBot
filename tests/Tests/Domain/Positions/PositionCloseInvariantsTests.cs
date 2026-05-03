using BinanceBot.Domain.Common;
using BinanceBot.Domain.Positions;
using BinanceBot.Domain.Positions.Events;
using BinanceBot.Domain.ValueObjects;
using FluentAssertions;

namespace BinanceBot.Tests.Domain.Positions;

/// <summary>
/// Loop 73 zombi-position regression suite. Locks in the atomic-close invariant:
/// when <see cref="Position.Close"/> succeeds it MUST simultaneously set
/// <see cref="Position.Status"/> to <see cref="PositionStatus.Closed"/>,
/// <see cref="Position.ClosedAt"/> to a non-null value, and
/// <see cref="Position.ExitPrice"/> to a non-null value. Any future refactor
/// that breaks this trio (e.g. a deferred status flip or an extra "Closing"
/// stage) would let the DB accumulate "zombi" rows where event payload says
/// closed but the row column says otherwise — exactly the symptom PM logged
/// against Loop 72 (which on inspection turned out to be an enum-naming
/// confusion, not a real bug; this suite hardens the contract).
/// </summary>
public class PositionCloseInvariantsTests
{
    private static readonly DateTimeOffset T0 = new(2026, 5, 1, 4, 7, 0, TimeSpan.Zero);

    private static Position OpenLong(decimal entry = 100m, decimal qty = 1m)
    {
        return Position.Open(
            Symbol.From("BTCUSDT"),
            TradeDirection.Long,
            quantity: qty,
            entryPrice: entry,
            stopPrice: null,
            strategyId: 42L,
            mode: TradingMode.Paper,
            now: T0);
    }

    [Fact]
    public void Close_AtomicallySets_Status_ClosedAt_AndExitPrice()
    {
        var position = OpenLong(entry: 100m, qty: 0.5m);
        position.Status.Should().Be(PositionStatus.Open);
        position.ClosedAt.Should().BeNull();
        position.ExitPrice.Should().BeNull();

        var closeAt = T0.AddMinutes(7);
        position.Close(exitPrice: 105m, reason: "tp", now: closeAt);

        // The atomic-close invariant: all three fields flip together.
        position.Status.Should().Be(PositionStatus.Closed);
        position.ClosedAt.Should().Be(closeAt);
        position.ExitPrice.Should().Be(105m);
        position.UnrealizedPnl.Should().Be(0m); // wiped on close
    }

    [Fact]
    public void Close_PublishesPositionClosedEvent_WithMatchingRealizedPnl()
    {
        // Domain event payload's RealizedPnl MUST equal the row column. PM Loop
        // 72 raporu "event payload doğru ama row Status takılı" iddia ediyordu;
        // bu test her iki tarafın bağlı kaldığını sabitler.
        var position = OpenLong(entry: 100m, qty: 1m);

        position.Close(exitPrice: 110m, reason: "tp", now: T0.AddMinutes(3));

        var evt = position.DomainEvents
            .OfType<PositionClosedEvent>()
            .Should().ContainSingle().Subject;
        evt.RealizedPnl.Should().Be(position.RealizedPnl);
        evt.RealizedPnl.Should().Be(10m); // (110 - 100) * 1 - 0 - 0
    }

    [Fact]
    public void Close_OnAlreadyClosedPosition_Throws_AndDoesNotMutate()
    {
        // Idempotency contract: a second Close call (e.g. duplicate exit fill
        // event) must NOT roll back the first close's column values. The
        // EnsureOpen() guard inside Position.Close enforces this; the handler
        // (OrderFilledPositionHandler) catches the resulting DomainException.
        var position = OpenLong();
        position.Close(exitPrice: 105m, reason: "tp", now: T0.AddMinutes(1));

        var firstClosedAt = position.ClosedAt;
        var firstExitPrice = position.ExitPrice;
        var firstRealizedPnl = position.RealizedPnl;

        Action secondClose = () => position.Close(
            exitPrice: 200m, reason: "duplicate", now: T0.AddMinutes(2));

        secondClose.Should().Throw<DomainException>()
            .WithMessage("*not open*");
        position.ClosedAt.Should().Be(firstClosedAt);
        position.ExitPrice.Should().Be(firstExitPrice);
        position.RealizedPnl.Should().Be(firstRealizedPnl);
    }

    [Fact]
    public void PositionStatus_HasOnly_Open_And_Closed_Values()
    {
        // Locks in the enum surface — any future "Closing" or "Pending" addition
        // must update every Status==Open / Status==Closed query in the codebase
        // (StrategySignalToOrderHandler capacity check, GetPortfolioSummary,
        // StopLossMonitorService, etc). This guard fails the build first so the
        // refactorer is forced to update every consumer.
        Enum.GetValues<PositionStatus>()
            .Should().BeEquivalentTo(new[] { PositionStatus.Open, PositionStatus.Closed });

        // Underlying numeric values are persisted (HasConversion<int>); changing
        // them is a breaking DB schema change.
        ((int)PositionStatus.Open).Should().Be(1);
        ((int)PositionStatus.Closed).Should().Be(2);
    }
}
