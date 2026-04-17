using BinanceBot.Domain.Common;
using BinanceBot.Domain.ValueObjects;
using FluentAssertions;

namespace BinanceBot.Tests.Domain.ValueObjects;

public class SymbolTests
{
    [Theory]
    [InlineData("btcusdt", "BTCUSDT")]
    [InlineData(" ethusdt ", "ETHUSDT")]
    [InlineData("BNBUSDT", "BNBUSDT")]
    public void From_NormalizesUppercaseAndTrims(string raw, string expected)
    {
        var symbol = Symbol.From(raw);
        symbol.Value.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("BTC")]
    [InlineData("THIS_IS_TOO_LONG_FOR_A_SYMBOL_NAME")]
    public void From_InvalidRaw_Throws(string raw)
    {
        var act = () => Symbol.From(raw);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Equality_TwoSymbolsWithSameValue_AreEqual()
    {
        var a = Symbol.From("BTCUSDT");
        var b = Symbol.From("btcusdt");
        a.Should().Be(b);
    }
}
