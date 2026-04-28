using BinanceBot.Application.Abstractions;
using BinanceBot.Application.Strategies.Cooldowns;
using BinanceBot.Application.Strategies.Indicators;
using BinanceBot.Domain.MarketData;
using BinanceBot.Domain.Strategies;
using BinanceBot.Infrastructure.Strategies.Cooldowns;
using BinanceBot.Infrastructure.Strategies.Evaluators;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace BinanceBot.Tests.Infrastructure.Strategies;

/// <summary>
/// Loop 50 AR-GE — <see cref="HybridMomentum1mEvaluator"/> giriş AND filtreleri ve
/// ATR15m-tabanlı çıkış geometrisi sözleşmesi:
///   1. Tüm 7 koşul sağlandığında long sinyal + ATR15m-dinamik TP/SL emit.
///   2. 15m BB lower kapışı yok → skip.
///   3. 15m RSI eşik üstü → skip.
///   4. 15m RSI yukarı dönüş yok (curr &lt;= prev) → skip.
///   5. 1m EMA9 &lt;= EMA21 (trend negatif/cross yok) → skip.
///   6. 1m volume surge yok → skip.
///   7. 1m ATR sessiz piyasa → skip.
///   8. 15m bar kapanmamış → skip.
///   9. Snapshot null (warmup eksik proxy) → skip.
///  10. ContextJson kontrat: type=hybrid-momentum-1m + key alanlar mevcut.
/// </summary>
public class HybridMomentum1mEvaluatorTests
{
    private const string Symbol = "BTCUSDT";
    private const long StrategyId = 1L;

    // binance-expert spec default — 1m EMA9/EMA21 + 15m BB(20,2) + RSI14 oversold (&lt;40)
    // + 1m vol×1.2 + ATR1m %0.03; ATR15m clip TP[0.40-1.00%] / SL[0.20-0.40%]; MaxHold 30dk;
    // Cooldown 3 bar × 1dk.
    private const string DefaultParams =
        "{\"EmaFastPeriod\":9,\"EmaSlowPeriod\":21,\"VolumeWindow1m\":20,\"AtrPeriod1m\":14," +
        "\"BbPeriod15m\":20,\"BbStdMultiplier15m\":2.0,\"RsiPeriod15m\":14," +
        "\"RsiOversoldThreshold15m\":40,\"AtrPeriod15m\":14," +
        "\"VolumeMultiplier1m\":1.2,\"MinAtrPct1m\":0.0003," +
        "\"TpAtrMultiplier\":1.5,\"SlAtrMultiplier\":0.8," +
        "\"MinTpPct\":0.004,\"MaxTpPct\":0.010,\"MinSlPct\":0.002,\"MaxSlPct\":0.004," +
        "\"MaxHoldMinutes\":30,\"CooldownBarsAfterSignal\":3}";

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
        public long BinanceServerTimeMs => UtcNow.ToUnixTimeMilliseconds();
        public long DriftMs => 0;
    }

    private static (HybridMomentum1mEvaluator Sut, Mock<IMarketIndicatorService> Indicators)
        Build(HybridMomentum1mIndicatorSnapshot? snapshot, ICooldownService? cooldown = null)
    {
        var mock = new Mock<IMarketIndicatorService>();
        mock.Setup(i => i.TryGetHybridMomentum1mSnapshot(
                Symbol,
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<decimal>(),
                It.IsAny<int>(),
                It.IsAny<int>()))
            .Returns(snapshot);

        var clock = new FixedClock(DateTimeOffset.UtcNow);
        var sut = new HybridMomentum1mEvaluator(
            mock.Object,
            cooldown ?? new CooldownService(NullLogger<CooldownService>.Instance),
            clock,
            NullLogger<HybridMomentum1mEvaluator>.Instance);
        return (sut, mock);
    }

    /// <summary>
    /// Happy path snapshot — tüm 7 AND koşulu sağlanır:
    ///   15m kalite kapısı:
    ///     - currentClose_15m=99, BbLower_15m=100  → close &lt; bbLower (kapış)
    ///     - rsi14_15m_curr=35  &lt; 40 (oversold) AND rsi14_15m_curr &gt; rsi14_15m_prev=30 (yukarı dönüş)
    ///     - atr14_15m=0.50, BarClosed_15m=true
    ///   1m frekans tetiği:
    ///     - ema9_1m=101 &gt; ema21_1m=100 (crossover sustained)
    ///     - currentVolume_1m=150, volumeMa20_1m=100  → 150 &gt; 100 × 1.2 = 120 (surge)
    ///     - atr14_1m=0.05, currentClose_1m=102, atrPct=0.05/102=0.00049 &gt; 0.0003
    ///
    /// Beklenen geometri (entry=102 = 1m close, TP/SL = ATR15m=0.50 ile ölçek):
    ///   rawTpPct = 0.50 * 1.5 / 102 = 0.007353 → clamp [0.004, 0.010] = 0.007353
    ///   rawSlPct = 0.50 * 0.8 / 102 = 0.003922 → clamp [0.002, 0.004] = 0.003922
    ///   TP ≈ 102 * 1.007353 = 102.750 ; SL ≈ 102 * 0.996078 = 101.600
    /// </summary>
    private static HybridMomentum1mIndicatorSnapshot HappySnapshot(
        // 1m
        decimal ema9_1m = 101m,
        decimal ema21_1m = 100m,
        decimal ema9Prev_1m = 100.5m,
        decimal ema21Prev_1m = 99.8m,
        decimal currentVolume_1m = 150m,
        decimal volumeMa20_1m = 100m,
        decimal atr14_1m = 0.05m,
        decimal currentClose_1m = 102m,
        bool barClosed_1m = true,
        // 15m
        decimal bbUpper_15m = 105m,
        decimal bbMiddle_15m = 102.5m,
        decimal bbLower_15m = 100m,
        decimal rsi14_15m = 35m,
        decimal rsi14Prev_15m = 30m,
        decimal atr14_15m = 0.50m,
        decimal currentClose_15m = 99m,
        bool barClosed_15m = true)
    {
        return new HybridMomentum1mIndicatorSnapshot(
            // 1m
            Ema9_1m: ema9_1m,
            Ema21_1m: ema21_1m,
            Ema9Prev_1m: ema9Prev_1m,
            Ema21Prev_1m: ema21Prev_1m,
            CurrentVolume_1m: currentVolume_1m,
            VolumeMa20_1m: volumeMa20_1m,
            Atr14_1m: atr14_1m,
            CurrentClose_1m: currentClose_1m,
            BarClosed_1m: barClosed_1m,
            LastBarOpenTime_1m: DateTimeOffset.UtcNow.AddMinutes(-1),
            // 15m
            BbUpper_15m: bbUpper_15m,
            BbMiddle_15m: bbMiddle_15m,
            BbLower_15m: bbLower_15m,
            Rsi14_15m: rsi14_15m,
            Rsi14Prev_15m: rsi14Prev_15m,
            Atr14_15m: atr14_15m,
            CurrentClose_15m: currentClose_15m,
            BarClosed_15m: barClosed_15m,
            LastBarOpenTime_15m: DateTimeOffset.UtcNow.AddMinutes(-15),
            AsOf: DateTimeOffset.UtcNow);
    }

    [Fact]
    public void Type_IsHybridMomentum1m()
    {
        var (sut, _) = Build(snapshot: null);
        sut.Type.Should().Be(StrategyType.HybridMomentum1m);
    }

    [Fact]
    public async Task NullSnapshot_ReturnsNull()
    {
        var (sut, _) = Build(snapshot: null);

        var result = await sut.EvaluateAsync(
            StrategyId, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(),
            cancellationToken: CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task HappyPath_EmitsLongWithAtr15mGeometry()
    {
        var snap = HappySnapshot();
        var (sut, _) = Build(snap);

        var result = await sut.EvaluateAsync(
            StrategyId, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(),
            cancellationToken: CancellationToken.None);

        result.Should().NotBeNull();
        result!.Direction.Should().Be(StrategySignalDirection.Long);
        result.SuggestedPrice.Should().Be(102m);

        // rawTpPct = 0.50 * 1.5 / 102 ≈ 0.007353 → clip [0.004, 0.010] geçerli
        // TP = 102 * 1.007353 ≈ 102.7500
        result.SuggestedTakeProfit.Should().BeApproximately(102.7500m, 0.01m);

        // rawSlPct = 0.50 * 0.8 / 102 ≈ 0.003922 → clip [0.002, 0.004] = 0.003922
        // SL = 102 * 0.996078 ≈ 101.6000
        result.SuggestedStopPrice.Should().BeApproximately(101.6000m, 0.01m);
    }

    [Fact]
    public async Task BbLowerBreach_NotMet_Skip()
    {
        // currentClose_15m >= BbLower_15m → kalite kapısı reddi.
        var snap = HappySnapshot(currentClose_15m: 100m, bbLower_15m: 100m);
        var (sut, _) = Build(snap);

        var result = await sut.EvaluateAsync(
            StrategyId, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(),
            cancellationToken: CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task RsiAboveOversoldThreshold_Skip()
    {
        // rsi14_15m_curr >= 40 → oversold koşulu fail.
        var snap = HappySnapshot(rsi14_15m: 45m, rsi14Prev_15m: 40m);
        var (sut, _) = Build(snap);

        var result = await sut.EvaluateAsync(
            StrategyId, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(),
            cancellationToken: CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task RsiNotTurningUp_CurrLessThanOrEqualPrev_Skip()
    {
        // rsi14_15m_curr <= rsi14_15m_prev → momentum yukarı dönüş yok.
        var snap = HappySnapshot(rsi14_15m: 30m, rsi14Prev_15m: 35m);
        var (sut, _) = Build(snap);

        var result = await sut.EvaluateAsync(
            StrategyId, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(),
            cancellationToken: CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task EmaTrendDown_FastBelowSlow_Skip()
    {
        // 1m ema9 <= ema21 → trend negatif/cross yok.
        var snap = HappySnapshot(ema9_1m: 99m, ema21_1m: 100m);
        var (sut, _) = Build(snap);

        var result = await sut.EvaluateAsync(
            StrategyId, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(),
            cancellationToken: CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task VolumeSurgeNotMet_Skip()
    {
        // currentVolume_1m <= volumeMa20_1m × 1.2 → surge yok.
        // volumeMa20=100, mult=1.2 → eşik 120; vol = 119.
        var snap = HappySnapshot(volumeMa20_1m: 100m, currentVolume_1m: 119m);
        var (sut, _) = Build(snap);

        var result = await sut.EvaluateAsync(
            StrategyId, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(),
            cancellationToken: CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task AtrBelowMinPct1m_QuietMarket_Skip()
    {
        // atr14_1m / currentClose_1m < MinAtrPct1m (0.0003).
        // close=102, atr=0.02 → atrPct=0.000196 < 0.0003.
        var snap = HappySnapshot(atr14_1m: 0.02m, currentClose_1m: 102m);
        var (sut, _) = Build(snap);

        var result = await sut.EvaluateAsync(
            StrategyId, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(),
            cancellationToken: CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task BarNotClosed15m_Skip()
    {
        var snap = HappySnapshot(barClosed_15m: false);
        var (sut, _) = Build(snap);

        var result = await sut.EvaluateAsync(
            StrategyId, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(),
            cancellationToken: CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task CooldownActive_AfterFirstEmit_SecondCallSkip()
    {
        var cooldown = new CooldownService(NullLogger<CooldownService>.Instance);
        var snap = HappySnapshot();
        var (sut, _) = Build(snap, cooldown);

        // İlk emit — sinyal yayınlanır + cooldown record edilir.
        var first = await sut.EvaluateAsync(
            StrategyId, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(),
            cancellationToken: CancellationToken.None);
        first.Should().NotBeNull();

        // İkinci çağrı aynı clock anında: cooldown 3 bar × 1dk = 3dk açık.
        var second = await sut.EvaluateAsync(
            StrategyId, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(),
            cancellationToken: CancellationToken.None);
        second.Should().BeNull();
    }

    [Fact]
    public async Task ContextJson_ContainsExpectedKeys()
    {
        var snap = HappySnapshot();
        var (sut, _) = Build(snap);

        var result = await sut.EvaluateAsync(
            StrategyId, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(),
            cancellationToken: CancellationToken.None);

        result.Should().NotBeNull();
        result!.ContextJson.Should().Contain("hybrid-momentum-1m");
        result.ContextJson.Should().Contain("entryReason");
        // System.Text.Json default encoder '+' karakterini + olarak escape
        // eder. Bu nedenle ham JSON tek-tek anahtar parçaları (alt-tire ile
        // ayrılmış 3 segment) üzerinden doğrulanır; runtime JSON parse "bb_dip+
        // ema_cross+rsi_turn" değerini orijinal haline çözer.
        result.ContextJson.Should().Contain("bb_dip");
        result.ContextJson.Should().Contain("ema_cross");
        result.ContextJson.Should().Contain("rsi_turn");
        result.ContextJson.Should().Contain("bbLower15m");
        result.ContextJson.Should().Contain("rsi14_15m");
        result.ContextJson.Should().Contain("ema9_1m");
        result.ContextJson.Should().Contain("ema21_1m");
        result.ContextJson.Should().Contain("atr14_1m");
        result.ContextJson.Should().Contain("atr14_15m");
        result.ContextJson.Should().Contain("volumeRatio");
        // ADR-0017 §17.7 TimeStop handler kontratı — maxHoldMinutes anahtarı zorunlu.
        result.ContextJson.Should().Contain("maxHoldMinutes");
        result.ContextJson.Should().Contain("cooldownBarsAfterSignal");
    }
}
