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
/// Loop 46 AR-GE — <see cref="EmaScalper1mEvaluator"/> giriş AND filtreleri ve
/// ATR-tabanlı çıkış geometrisi sözleşmesi:
///   1. Tüm koşullar sağlandığında long sinyal + ATR-dinamik TP/SL emit.
///   2. EMA9 &lt;= EMA21 (trend negatif) skip.
///   3. RSI nötr aralık dışı (alt ya da üst) skip.
///   4. Volume gevşek eşiğin altında skip.
///   5. ATR &lt; MinAtrPct sessiz piyasa skip.
///   6. Snapshot null ⇒ skip (warmup yetersiz proxy).
///   7. ContextJson kontrat: type=ema-scalper-1m, key alanlar mevcut.
/// </summary>
public class EmaScalper1mEvaluatorTests
{
    private const string Symbol = "BTCUSDT";
    private const long StrategyId = 1L;

    // binance-expert spec default — 1m EMA9/EMA21 + RSI[40,65] + Vol×0.8 + ATR14
    // dinamik TP/SL clip [0.30–0.80%] / [0.20–0.50%], MaxHold 8dk, Cooldown 2 bar.
    private const string DefaultParams =
        "{\"KlineInterval\":\"1m\",\"EmaFastPeriod\":9,\"EmaSlowPeriod\":21," +
        "\"RsiPeriod\":14,\"RsiLowerBand\":40,\"RsiUpperBand\":65," +
        "\"VolumeWindow\":20,\"VolumeMultiplier\":0.8,\"AtrPeriod\":14," +
        "\"TpAtrMultiplier\":1.5,\"SlAtrMultiplier\":0.8,\"MinTpPct\":0.003," +
        "\"MaxTpPct\":0.008,\"MinSlPct\":0.002,\"MaxSlPct\":0.005," +
        "\"MaxHoldMinutes\":8,\"MinAtrPct\":0.0003,\"CooldownBarsAfterSignal\":2}";

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
        public long BinanceServerTimeMs => UtcNow.ToUnixTimeMilliseconds();
        public long DriftMs => 0;
    }

    private static (EmaScalper1mEvaluator Sut, Mock<IMarketIndicatorService> Indicators)
        Build(EmaScalperIndicatorSnapshot? snapshot, ICooldownService? cooldown = null)
    {
        var mock = new Mock<IMarketIndicatorService>();
        mock.Setup(i => i.TryGetEmaScalperSnapshot(
                Symbol,
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<int>()))
            .Returns(snapshot);

        var clock = new FixedClock(DateTimeOffset.UtcNow);
        var sut = new EmaScalper1mEvaluator(
            mock.Object,
            cooldown ?? new CooldownService(NullLogger<CooldownService>.Instance),
            clock,
            NullLogger<EmaScalper1mEvaluator>.Instance);
        return (sut, mock);
    }

    /// <summary>
    /// Happy path snapshot — tüm AND koşulları sağlanır:
    ///   - Ema9Now=101, Ema21Now=100 (trend pozitif)
    ///   - currentClose=102 > Ema9Now=101 (momentum)
    ///   - rsi14=55 ∈ [40, 65] (nötr)
    ///   - currentVolume=120, volumeSma20=100 → 120 > 100 × 0.8 = 80
    ///   - atr14=0.50, atrPct=0.50/102=0.0049 > 0.0003
    ///
    /// Beklenen geometri:
    ///   rawTpPct = 0.50 * 1.5 / 102 = 0.007353 → clamp [0.003, 0.008] = 0.007353
    ///   rawSlPct = 0.50 * 0.8 / 102 = 0.003922 → clamp [0.002, 0.005] = 0.003922
    ///   TP ≈ 102 * 1.007353 = 102.750 ; SL ≈ 102 * 0.996078 = 101.600
    /// </summary>
    private static EmaScalperIndicatorSnapshot HappySnapshot(
        decimal ema9Now = 101m,
        decimal ema9Prev = 100.5m,
        decimal ema21Now = 100m,
        decimal ema21Prev = 99.8m,
        decimal rsi14 = 55m,
        decimal volumeSma20 = 100m,
        decimal currentVolume = 120m,
        decimal atr14 = 0.50m,
        decimal currentClose = 102m,
        bool barClosed = true)
    {
        return new EmaScalperIndicatorSnapshot(
            Ema9Now: ema9Now,
            Ema9Prev: ema9Prev,
            Ema21Now: ema21Now,
            Ema21Prev: ema21Prev,
            Rsi14: rsi14,
            VolumeSma20: volumeSma20,
            CurrentVolume: currentVolume,
            Atr14: atr14,
            CurrentClose: currentClose,
            BarClosed: barClosed,
            LastBarOpenTime: DateTimeOffset.UtcNow.AddMinutes(-1),
            AsOf: DateTimeOffset.UtcNow);
    }

    [Fact]
    public void Type_IsEmaScalper1m()
    {
        var (sut, _) = Build(snapshot: null);
        sut.Type.Should().Be(StrategyType.EmaScalper1m);
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
    public async Task HappyPath_EmitsLongWithAtrGeometry()
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

        // rawTpPct = 0.50 * 1.5 / 102 ≈ 0.007353 → clip [0.003, 0.008] geçerli
        // TP = 102 * 1.007353 ≈ 102.7500
        result.SuggestedTakeProfit.Should().BeApproximately(102.7500m, 0.01m);

        // rawSlPct = 0.50 * 0.8 / 102 ≈ 0.003922 → clip [0.002, 0.005] geçerli
        // SL = 102 * 0.996078 ≈ 101.6000
        result.SuggestedStopPrice.Should().BeApproximately(101.6000m, 0.01m);
    }

    [Fact]
    public async Task TrendDown_EmaFastBelowSlow_Skip()
    {
        // EMA9 < EMA21 → trend negatif, sinyal yok.
        var snap = HappySnapshot(ema9Now: 99m, ema21Now: 100m);
        var (sut, _) = Build(snap);

        var result = await sut.EvaluateAsync(
            StrategyId, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(),
            cancellationToken: CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task MomentumWeak_CloseBelowEma9_Skip()
    {
        // currentClose <= Ema9Now → momentum koşulu fail.
        var snap = HappySnapshot(currentClose: 100.5m, ema9Now: 101m);
        var (sut, _) = Build(snap);

        var result = await sut.EvaluateAsync(
            StrategyId, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(),
            cancellationToken: CancellationToken.None);

        result.Should().BeNull();
    }

    [Theory]
    [InlineData(35)] // alt eşik altı (RsiLowerBand 40)
    [InlineData(70)] // üst eşik üstü (RsiUpperBand 65)
    public async Task RsiOutOfNeutralBand_Skip(int rsi)
    {
        var snap = HappySnapshot(rsi14: rsi);
        var (sut, _) = Build(snap);

        var result = await sut.EvaluateAsync(
            StrategyId, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(),
            cancellationToken: CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task VolumeBelowThreshold_Skip()
    {
        // currentVolume = volumeSma20 × 0.8 - epsilon → fail.
        // volumeSma20 = 100, mult = 0.8 → eşik 80; vol = 79.
        var snap = HappySnapshot(volumeSma20: 100m, currentVolume: 79m);
        var (sut, _) = Build(snap);

        var result = await sut.EvaluateAsync(
            StrategyId, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(),
            cancellationToken: CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task AtrBelowMinPct_QuietMarket_Skip()
    {
        // atr14 / close < MinAtrPct (0.0003 = %0.03).
        // close = 102, atr = 0.02 → atrPct = 0.000196 < 0.0003.
        var snap = HappySnapshot(atr14: 0.02m, currentClose: 102m);
        var (sut, _) = Build(snap);

        var result = await sut.EvaluateAsync(
            StrategyId, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(),
            cancellationToken: CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task CooldownActive_Skip()
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

        // İkinci çağrı aynı clock anında: cooldown 2 bar × 1dk = 2dk açık.
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
        result!.ContextJson.Should().Contain("ema-scalper-1m");
        result.ContextJson.Should().Contain("entryReason");
        result.ContextJson.Should().Contain("ema_crossover");
        result.ContextJson.Should().Contain("ema9");
        result.ContextJson.Should().Contain("ema21");
        result.ContextJson.Should().Contain("rsi14");
        result.ContextJson.Should().Contain("atr14");
        // ADR-0017 §17.7 TimeStop handler kontratı — maxHoldMinutes anahtarı zorunlu.
        result.ContextJson.Should().Contain("maxHoldMinutes");
        result.ContextJson.Should().Contain("cooldownBarsAfterSignal");
    }

    [Fact]
    public async Task BarNotClosed_Skip()
    {
        var snap = HappySnapshot(barClosed: false);
        var (sut, _) = Build(snap);

        var result = await sut.EvaluateAsync(
            StrategyId, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(),
            cancellationToken: CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task UnsupportedInterval_Skip()
    {
        var snap = HappySnapshot();
        var (sut, _) = Build(snap);

        var paramsWith15m =
            "{\"KlineInterval\":\"15m\",\"EmaFastPeriod\":9,\"EmaSlowPeriod\":21," +
            "\"RsiPeriod\":14,\"RsiLowerBand\":40,\"RsiUpperBand\":65," +
            "\"VolumeWindow\":20,\"VolumeMultiplier\":0.8,\"AtrPeriod\":14," +
            "\"TpAtrMultiplier\":1.5,\"SlAtrMultiplier\":0.8,\"MinTpPct\":0.003," +
            "\"MaxTpPct\":0.008,\"MinSlPct\":0.002,\"MaxSlPct\":0.005," +
            "\"MaxHoldMinutes\":8,\"MinAtrPct\":0.0003,\"CooldownBarsAfterSignal\":2}";

        var result = await sut.EvaluateAsync(
            StrategyId, paramsWith15m, Symbol,
            closedBars: Array.Empty<Kline>(),
            cancellationToken: CancellationToken.None);

        result.Should().BeNull();
    }
}
