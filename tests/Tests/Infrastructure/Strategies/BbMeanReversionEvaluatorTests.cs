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
/// Loop 44 AR-GE — <see cref="BbMeanReversionEvaluator"/> giriş AND filtreleri ve
/// ATR-tabanlı çıkış geometrisi sözleşmesi:
///   1. Tüm koşullar sağlandığında long sinyal + ATR-dynamic TP/SL emit.
///   2. BB lower kapışı yoksa skip.
///   3. RSI oversold değilse skip.
///   4. Volume Z-Score eşik altındaysa skip.
///   5. ATR &lt; MinAtrPct sessiz piyasa skip.
///   6. Snapshot null ⇒ skip (warmup yetersiz proxy).
///   7. ContextJson kontrat: type=bb-mean-reversion-15m, key alanlar mevcut.
/// </summary>
public class BbMeanReversionEvaluatorTests
{
    private const string Symbol = "BTCUSDT";
    private const long StrategyId = 1L;

    // binance-expert spec default — 15m BB(20, 2σ) + RSI14<30 + Vol Z>1.0 + ATR14
    // dinamik TP/SL clip [0.4–1.0%] / [0.3–0.6%], MaxHold 90dk, Cooldown 4 bar.
    private const string DefaultParams =
        "{\"KlineInterval\":\"15m\",\"BbPeriod\":20,\"BbStdMultiplier\":2.0," +
        "\"RsiPeriod\":14,\"RsiOversoldThreshold\":30,\"VolumeWindow\":20," +
        "\"VolumeZScoreThreshold\":1.0,\"AtrPeriod\":14,\"TpAtrMultiplier\":1.5," +
        "\"SlAtrMultiplier\":1.0,\"MinTpPct\":0.004,\"MaxTpPct\":0.010," +
        "\"MinSlPct\":0.003,\"MaxSlPct\":0.006,\"MaxHoldMinutes\":90," +
        "\"MinAtrPct\":0.0007,\"CooldownBarsAfterSignal\":4}";

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
        public long BinanceServerTimeMs => UtcNow.ToUnixTimeMilliseconds();
        public long DriftMs => 0;
    }

    private static (BbMeanReversionEvaluator Sut, Mock<IMarketIndicatorService> Indicators)
        Build(BbMeanReversionIndicatorSnapshot? snapshot, ICooldownService? cooldown = null)
    {
        var mock = new Mock<IMarketIndicatorService>();
        mock.Setup(i => i.TryGetBbMeanReversionSnapshot(
                Symbol,
                It.IsAny<int>(),
                It.IsAny<decimal>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<int>()))
            .Returns(snapshot);

        var clock = new FixedClock(DateTimeOffset.UtcNow);
        var sut = new BbMeanReversionEvaluator(
            mock.Object,
            cooldown ?? new CooldownService(NullLogger<CooldownService>.Instance),
            clock,
            NullLogger<BbMeanReversionEvaluator>.Instance);
        return (sut, mock);
    }

    /// <summary>
    /// Happy path snapshot — tüm AND koşulları sağlanır:
    ///   - currentClose=99.0 &lt; bbLower=99.5 (kapış)
    ///   - rsi14=25 &lt; 30 (oversold)
    ///   - currentVolume=200, mean=100, std=50 → Z=2.0 &gt; 1.0
    ///   - atr14=0.20, atrPct=0.20/99=0.00202 &gt; 0.0007 (rejim aktif)
    ///
    /// Beklenen geometri:
    ///   rawTpPct = 0.20 * 1.5 / 99 = 0.003030 → clamp [0.004,0.010] = 0.004
    ///   rawSlPct = 0.20 * 1.0 / 99 = 0.002020 → clamp [0.003,0.006] = 0.003
    ///   TP = 99 * 1.004 = 99.396 ; SL = 99 * 0.997 = 98.703
    /// </summary>
    private static BbMeanReversionIndicatorSnapshot HappySnapshot(
        decimal currentClose = 99.0m,
        decimal bbLower = 99.5m,
        decimal bbMiddle = 100.0m,
        decimal bbUpper = 100.5m,
        decimal rsi14 = 25m,
        decimal volumeAvg20 = 100m,
        decimal volumeStd20 = 50m,
        decimal currentVolume = 200m,
        decimal atr14 = 0.20m,
        bool barClosed = true,
        // Loop 58 — EMA200 trend filtresi default uptrend: close=99 > ema200=90.
        // Mevcut happy path testleri trend filtresinden geçmek için bu varsayım
        // ile tutarlı kalır. Downtrend testi explicit ema200 > close ile yazılır.
        decimal ema200_15m = 90m)
    {
        return new BbMeanReversionIndicatorSnapshot(
            BbUpper: bbUpper,
            BbMiddle: bbMiddle,
            BbLower: bbLower,
            Rsi14: rsi14,
            VolumeAvg20: volumeAvg20,
            VolumeStd20: volumeStd20,
            CurrentVolume: currentVolume,
            Atr14: atr14,
            CurrentClose: currentClose,
            BarClosed: barClosed,
            LastBarOpenTime: DateTimeOffset.UtcNow.AddMinutes(-15),
            AsOf: DateTimeOffset.UtcNow,
            Ema200_15m: ema200_15m);
    }

    [Fact]
    public void Type_IsBbMeanReversion15m()
    {
        var (sut, _) = Build(snapshot: null);
        sut.Type.Should().Be(StrategyType.BbMeanReversion15m);
    }

    [Fact]
    public async Task NullSnapshot_ReturnsNull()
    {
        var (sut, _) = Build(snapshot: null);

        var result = await sut.EvaluateAsync(
            StrategyId, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task AllConditionsMet_EmitsLong_WithAtrDynamicGeometry()
    {
        var (sut, _) = Build(HappySnapshot());

        var result = await sut.EvaluateAsync(
            StrategyId, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(), CancellationToken.None);

        result.Should().NotBeNull();
        result!.Direction.Should().Be(StrategySignalDirection.Long);
        result.SuggestedPrice.Should().Be(99.0m);
        // TP clamp altı (rawTp=%0.30 → clamp %0.40) → 99 * 1.004 = 99.396
        result.SuggestedTakeProfit.Should().BeApproximately(99.396m, 0.0005m);
        // SL clamp altı (rawSl=%0.20 → clamp %0.30) → 99 * 0.997 = 98.703
        result.SuggestedStopPrice.Should().BeApproximately(98.703m, 0.0005m);
        result.ContextJson.Should().Contain("bb-mean-reversion-15m");
        result.ContextJson.Should().Contain("rsi14");
        result.ContextJson.Should().Contain("volumeZScore");
        result.ContextJson.Should().Contain("maxHoldMinutes");
    }

    [Fact]
    public async Task BbLowerNotBreached_ReturnsNull()
    {
        // currentClose >= bbLower → kapış yok.
        var snap = HappySnapshot(currentClose: 99.6m, bbLower: 99.5m);
        var (sut, _) = Build(snap);

        var result = await sut.EvaluateAsync(
            StrategyId, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task RsiAboveThreshold_ReturnsNull()
    {
        // rsi14 = 35 >= 30 oversold değil.
        var snap = HappySnapshot(rsi14: 35m);
        var (sut, _) = Build(snap);

        var result = await sut.EvaluateAsync(
            StrategyId, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task VolumeZBelowThreshold_ReturnsNull()
    {
        // currentVolume=120, mean=100, std=50 → Z = 0.4 ≤ 1.0
        var snap = HappySnapshot(currentVolume: 120m);
        var (sut, _) = Build(snap);

        var result = await sut.EvaluateAsync(
            StrategyId, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task VolumeStdZero_ReturnsNull()
    {
        // Std == 0 ⇒ Z hesaplanamaz → no-signal.
        var snap = HappySnapshot(volumeStd20: 0m);
        var (sut, _) = Build(snap);

        var result = await sut.EvaluateAsync(
            StrategyId, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(), CancellationToken.None);

        result.Should().BeNull();
    }

    /// <summary>
    /// Loop 56 hotfix — testnet düz hacim rejiminde (VolumeStd20 ≈ 0)
    /// <c>VolumeZScoreThreshold = 0.0</c> config ile volume filtresinin
    /// tamamen devre dışı bırakılabilmesi. Daha önce
    /// <c>VolumeStd20 &gt; 0m</c> guard threshold ne olursa olsun bypass'a
    /// izin vermiyordu → tüm sinyaller bloke. Fix: threshold &lt;= 0 ⇒
    /// volumeOk her zaman true.
    /// </summary>
    [Fact]
    public async Task Threshold0_VolumeFilterOff_AllowsSignal()
    {
        // VolumeStd20 = 0 (flat hacim, Z hesaplanamaz) AMA threshold=0 ⇒ filter off.
        var snap = HappySnapshot(volumeStd20: 0m, currentVolume: 100m);
        var (sut, _) = Build(snap);

        // Threshold = 0.0 — diğer tüm parametreler default.
        var paramsThresholdOff =
            "{\"KlineInterval\":\"15m\",\"BbPeriod\":20,\"BbStdMultiplier\":2.0," +
            "\"RsiPeriod\":14,\"RsiOversoldThreshold\":30,\"VolumeWindow\":20," +
            "\"VolumeZScoreThreshold\":0.0,\"AtrPeriod\":14,\"TpAtrMultiplier\":1.5," +
            "\"SlAtrMultiplier\":1.0,\"MinTpPct\":0.004,\"MaxTpPct\":0.010," +
            "\"MinSlPct\":0.003,\"MaxSlPct\":0.006,\"MaxHoldMinutes\":90," +
            "\"MinAtrPct\":0.0007,\"CooldownBarsAfterSignal\":4}";

        var result = await sut.EvaluateAsync(
            StrategyId, paramsThresholdOff, Symbol,
            closedBars: Array.Empty<Kline>(), CancellationToken.None);

        result.Should().NotBeNull();
        result!.Direction.Should().Be(StrategySignalDirection.Long);
    }

    [Fact]
    public async Task AtrPctBelowMin_SilentMarket_ReturnsNull()
    {
        // atr14=0.05 / close=99 = 0.000505 < MinAtrPct 0.0007.
        var snap = HappySnapshot(atr14: 0.05m);
        var (sut, _) = Build(snap);

        var result = await sut.EvaluateAsync(
            StrategyId, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task BarNotClosed_ReturnsNull()
    {
        var snap = HappySnapshot(barClosed: false);
        var (sut, _) = Build(snap);

        var result = await sut.EvaluateAsync(
            StrategyId, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(), CancellationToken.None);

        result.Should().BeNull();
    }

    /// <summary>
    /// Loop 58 disaster recovery — EMA200 trend filtresi PASS branch.
    /// currentClose &gt; Ema200_15m ⇒ uptrend ⇒ trend filtresi sinyale izin verir
    /// (diğer tüm AND koşulları sağlanmış olduğunda emit).
    /// </summary>
    [Fact]
    public async Task CloseAboveEma200_TrendFilterPasses_AllowsSignal()
    {
        // currentClose=99.0 > ema200=90.0 → uptrend.
        var snap = HappySnapshot(currentClose: 99.0m, ema200_15m: 90.0m);
        var (sut, _) = Build(snap);

        var result = await sut.EvaluateAsync(
            StrategyId, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(), CancellationToken.None);

        result.Should().NotBeNull();
        result!.Direction.Should().Be(StrategySignalDirection.Long);
    }

    /// <summary>
    /// Loop 58 disaster recovery — EMA200 trend filtresi BLOCK branch.
    /// currentClose &lt;= Ema200_15m ⇒ downtrend ⇒ "düşen bıçağı tutmak" anti-
    /// pattern'i; sinyal üretilmez. BB lower kapışı + RSI oversold + Vol Z koşulları
    /// sağlansa bile downtrend filtresi tek başına bloke eder.
    /// </summary>
    [Fact]
    public async Task CloseBelowEma200_TrendFilterBlocks_DowntrendSkip()
    {
        // currentClose=99.0 < ema200=110.0 → downtrend ("düşen bıçak"). Diğer tüm
        // koşullar happy snapshot ile sağlanır; sinyal yine de bloke olmalı.
        var snap = HappySnapshot(currentClose: 99.0m, ema200_15m: 110.0m);
        var (sut, _) = Build(snap);

        var result = await sut.EvaluateAsync(
            StrategyId, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task SecondCallWithinCooldown_ReturnsNull()
    {
        // Aynı CooldownService paylaşılır → ilk emit cooldown başlatır,
        // ikinci çağrı (aynı anda) IsCooldown=true ile skip eder.
        var cooldown = new CooldownService(NullLogger<CooldownService>.Instance);
        var (sut, _) = Build(HappySnapshot(), cooldown);

        var first = await sut.EvaluateAsync(
            StrategyId, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(), CancellationToken.None);
        first.Should().NotBeNull();

        var second = await sut.EvaluateAsync(
            StrategyId, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(), CancellationToken.None);
        second.Should().BeNull();
    }
}
