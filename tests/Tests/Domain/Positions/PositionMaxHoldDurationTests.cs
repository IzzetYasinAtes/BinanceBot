using BinanceBot.Domain.Common;
using BinanceBot.Domain.Positions;
using BinanceBot.Domain.ValueObjects;
using FluentAssertions;

namespace BinanceBot.Tests.Domain.Positions;

/// <summary>
/// ADR-0017 §17.7 + ADR-0014 §14.5 — Position.MaxHoldDuration invariants.
/// The aggregate enforces positive duration when provided; null means the
/// time-stop branch is inert for this Position.
/// </summary>
public class PositionMaxHoldDurationTests
{
    private static Position OpenWithHold(TimeSpan? maxHold)
    {
        return Position.Open(
            Symbol.From("BTCUSDT"),
            PositionSide.Long,
            quantity: 0.01m,
            entryPrice: 30000m,
            stopPrice: null,
            strategyId: 1,
            mode: TradingMode.Paper,
            now: DateTimeOffset.UtcNow,
            maxHoldDuration: maxHold);
    }

    [Fact]
    public void Open_WithPositiveMaxHold_PersistsDuration()
    {
        var pos = OpenWithHold(TimeSpan.FromMinutes(10));
        pos.MaxHoldDuration.Should().Be(TimeSpan.FromMinutes(10));
    }

    [Fact]
    public void Open_WithoutMaxHold_LeavesDurationNull()
    {
        var pos = OpenWithHold(null);
        pos.MaxHoldDuration.Should().BeNull();
    }

    [Fact]
    public void Open_WithZeroMaxHold_Throws()
    {
        var act = () => OpenWithHold(TimeSpan.Zero);
        act.Should().Throw<DomainException>()
            .WithMessage("Max hold duration must be positive when set.");
    }

    [Fact]
    public void Open_WithNegativeMaxHold_Throws()
    {
        var act = () => OpenWithHold(TimeSpan.FromSeconds(-1));
        act.Should().Throw<DomainException>();
    }
}
