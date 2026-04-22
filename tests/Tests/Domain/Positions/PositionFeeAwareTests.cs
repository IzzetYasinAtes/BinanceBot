using BinanceBot.Domain.Common;
using BinanceBot.Domain.Positions;
using BinanceBot.Domain.ValueObjects;
using FluentAssertions;

namespace BinanceBot.Tests.Domain.Positions;

/// <summary>
/// ADR-0020 §20.6 — fee-aware <see cref="Position"/> aggregate. Validates that
/// <c>RealizedPnl</c> nets <c>EntryCommission + ExitCommission</c> (both quote
/// USDT) from the gross price delta, and that cumulative <c>AddFill</c> fees
/// roll up into <c>EntryCommission</c>. Covers the invariant checks
/// (negative fees rejected, zero default preserves legacy gross behaviour).
/// </summary>
public class PositionFeeAwareTests
{
    private static readonly DateTimeOffset T0 = new(2026, 4, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Close_NetsEntryAndExitCommissions_FromGrossRealizedPnl()
    {
        // ADR-0020 §20.6 TRAM scenario: gross 1.0, entry fee 0.3, exit fee 0.3 -> realized 0.4.
        // qty=1, long, entry 10, exit 11 -> gross = (11-10)*1 = 1.0.
        var position = Position.Open(
            Symbol.From("BTCUSDT"),
            PositionSide.Long,
            quantity: 1m,
            entryPrice: 10m,
            stopPrice: null,
            strategyId: null,
            mode: TradingMode.Paper,
            now: T0,
            entryCommission: 0.3m);

        position.EntryCommission.Should().Be(0.3m);
        position.ExitCommission.Should().Be(0m);

        position.Close(
            exitPrice: 11m,
            reason: "tp",
            now: T0.AddMinutes(5),
            exitCommission: 0.3m);

        position.ExitCommission.Should().Be(0.3m);
        position.RealizedPnl.Should().Be(0.4m); // 1.0 - 0.3 - 0.3
    }

    [Fact]
    public void Close_WithZeroCommissions_PreservesGrossBehaviour()
    {
        // Backward compatibility: callers that don't opt into fee-aware path
        // keep observing the pre-ADR-0020 gross RealizedPnl (loop 32 diagnostic
        // baseline). Prevents silent semantic drift in admin/test code paths.
        var position = Position.Open(
            Symbol.From("ETHUSDT"),
            PositionSide.Long,
            quantity: 0.02m,
            entryPrice: 2500m,
            stopPrice: null,
            strategyId: null,
            mode: TradingMode.Paper,
            now: T0);

        position.Close(exitPrice: 2600m, reason: "tp", now: T0.AddMinutes(1));

        position.EntryCommission.Should().Be(0m);
        position.ExitCommission.Should().Be(0m);
        position.RealizedPnl.Should().Be(2m); // (2600 - 2500) * 0.02
    }

    [Fact]
    public void AddFill_AccumulatesEntryCommission_Cumulatively()
    {
        // ADR-0020 §20.6 — partial fill commissions sum into EntryCommission
        // so a close after multiple fills nets the full entry-side fee ledger.
        var position = Position.Open(
            Symbol.From("BTCUSDT"),
            PositionSide.Long,
            quantity: 0.001m,
            entryPrice: 30000m,
            stopPrice: null,
            strategyId: null,
            mode: TradingMode.Paper,
            now: T0,
            entryCommission: 0.03m);

        position.AddFill(addQuantity: 0.001m, addPrice: 30100m, now: T0.AddSeconds(1),
            addCommission: 0.0301m);

        position.EntryCommission.Should().Be(0.0601m); // 0.03 + 0.0301 cumulative
        position.Quantity.Should().Be(0.002m);
    }

    [Fact]
    public void Open_NegativeEntryCommission_Throws()
    {
        Action act = () => Position.Open(
            Symbol.From("BTCUSDT"),
            PositionSide.Long,
            quantity: 0.001m,
            entryPrice: 30000m,
            stopPrice: null,
            strategyId: null,
            mode: TradingMode.Paper,
            now: T0,
            entryCommission: -0.01m);

        act.Should().Throw<DomainException>()
            .WithMessage("*non-negative*");
    }

    [Fact]
    public void Close_NegativeExitCommission_Throws()
    {
        var position = Position.Open(
            Symbol.From("BTCUSDT"),
            PositionSide.Long,
            quantity: 0.001m,
            entryPrice: 30000m,
            stopPrice: null,
            strategyId: null,
            mode: TradingMode.Paper,
            now: T0);

        Action act = () => position.Close(
            exitPrice: 31000m, reason: "tp", now: T0.AddMinutes(1),
            exitCommission: -0.05m);

        act.Should().Throw<DomainException>()
            .WithMessage("*non-negative*");
    }
}
