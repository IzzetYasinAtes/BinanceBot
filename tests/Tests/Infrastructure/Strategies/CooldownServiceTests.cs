using BinanceBot.Infrastructure.Strategies.Cooldowns;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace BinanceBot.Tests.Infrastructure.Strategies;

/// <summary>
/// Loop 42 P0 fix — <see cref="CooldownService"/> contract:
///   1. Henüz kayıt yok ⇒ cooldown false.
///   2. Aynı (strategy, symbol) için kayıt sonrası pencere içinde true.
///   3. Pencere geçtikten sonra false.
///   4. Negatif/sıfır cooldownBars ⇒ disable.
///   5. Symbol normalize (case-insensitive).
///   6. Farklı strategyId aynı symbol bağımsız.
/// </summary>
public class CooldownServiceTests
{
    private const long StrategyId = 100L;
    private const string Symbol = "LTCUSDT";

    private static CooldownService NewService() =>
        new(NullLogger<CooldownService>.Instance);

    [Fact]
    public void IsCooldown_NoRecord_ReturnsFalse()
    {
        var sut = NewService();
        var now = DateTimeOffset.UtcNow;

        sut.IsCooldown(StrategyId, Symbol, cooldownBars: 6, barMinutes: 15, now)
           .Should().BeFalse();
    }

    [Fact]
    public void IsCooldown_WithinWindow_ReturnsTrue()
    {
        var sut = NewService();
        var t0 = DateTimeOffset.UtcNow;
        sut.RecordSignal(StrategyId, Symbol, t0);

        // 6 bar × 15dk = 90dk pencere; t+30dk hâlâ içinde.
        sut.IsCooldown(StrategyId, Symbol, 6, 15, t0.AddMinutes(30))
           .Should().BeTrue();
    }

    [Fact]
    public void IsCooldown_AfterWindow_ReturnsFalse()
    {
        var sut = NewService();
        var t0 = DateTimeOffset.UtcNow;
        sut.RecordSignal(StrategyId, Symbol, t0);

        // 6 bar × 15dk = 90dk; t+91dk pencerenin dışı.
        sut.IsCooldown(StrategyId, Symbol, 6, 15, t0.AddMinutes(91))
           .Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void IsCooldown_NonPositiveBars_Disabled(int bars)
    {
        var sut = NewService();
        var t0 = DateTimeOffset.UtcNow;
        sut.RecordSignal(StrategyId, Symbol, t0);

        sut.IsCooldown(StrategyId, Symbol, bars, 15, t0)
           .Should().BeFalse();
    }

    [Fact]
    public void IsCooldown_SymbolCaseInsensitive()
    {
        var sut = NewService();
        var t0 = DateTimeOffset.UtcNow;
        sut.RecordSignal(StrategyId, "ltcusdt", t0);

        sut.IsCooldown(StrategyId, "LTCUSDT", 6, 15, t0.AddMinutes(10))
           .Should().BeTrue();
    }

    [Fact]
    public void IsCooldown_DifferentStrategyId_Independent()
    {
        var sut = NewService();
        var t0 = DateTimeOffset.UtcNow;
        sut.RecordSignal(StrategyId, Symbol, t0);

        sut.IsCooldown(StrategyId + 1, Symbol, 6, 15, t0.AddMinutes(10))
           .Should().BeFalse();
    }

    [Fact]
    public void RecordSignal_Overwrites_ExtendsWindow()
    {
        var sut = NewService();
        var t0 = DateTimeOffset.UtcNow;
        sut.RecordSignal(StrategyId, Symbol, t0);

        // 80dk sonra yeni sinyal — pencere t0+80'den itibaren tekrar 90dk.
        var t1 = t0.AddMinutes(80);
        sut.RecordSignal(StrategyId, Symbol, t1);

        sut.IsCooldown(StrategyId, Symbol, 6, 15, t1.AddMinutes(60))
           .Should().BeTrue();
    }
}
