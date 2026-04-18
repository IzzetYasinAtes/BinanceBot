using BinanceBot.Domain.Common;
using BinanceBot.Domain.Positions;
using BinanceBot.Domain.Positions.Events;
using BinanceBot.Domain.ValueObjects;
using FluentAssertions;

namespace BinanceBot.Tests.Domain.Positions;

public class PositionModeTests
{
    [Theory]
    [InlineData(TradingMode.Paper)]
    [InlineData(TradingMode.LiveTestnet)]
    [InlineData(TradingMode.LiveMainnet)]
    public void Open_PersistsMode_AndEmitsEventWithMode(TradingMode mode)
    {
        var position = Position.Open(
            Symbol.From("BTCUSDT"),
            PositionSide.Long,
            quantity: 0.01m,
            entryPrice: 30000m,
            stopPrice: null,
            strategyId: 1,
            mode: mode,
            now: DateTimeOffset.UtcNow);

        position.Mode.Should().Be(mode);
        position.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<PositionOpenedEvent>()
            .Which.Mode.Should().Be(mode);
    }

    [Fact]
    public void Close_EmitsClosedEventWithMode()
    {
        var p = Position.Open(Symbol.From("BTCUSDT"), PositionSide.Long,
            0.01m, 30000m, null, 1, TradingMode.LiveTestnet, DateTimeOffset.UtcNow);
        p.ClearDomainEvents();

        p.Close(exitPrice: 31000m, reason: "tp_hit", now: DateTimeOffset.UtcNow);

        p.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<PositionClosedEvent>()
            .Which.Mode.Should().Be(TradingMode.LiveTestnet);
    }
}
