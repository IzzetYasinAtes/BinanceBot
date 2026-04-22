using BinanceBot.Application.Strategies.Indicators;
using BinanceBot.Domain.MarketData;
using BinanceBot.Domain.Strategies;
using BinanceBot.Infrastructure.Strategies.Evaluators;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace BinanceBot.Tests.Infrastructure.Strategies;

/// <summary>
/// Loop 33 AR-GE (Strateji D) contract — <see cref="AtrScalperVwapEmaEvaluator"/>
/// MicroScalper filter gate'lerini korur ancak TP/SL'i ATR14 çarpanları ile
/// dinamik hesaplar. Test kapsamı:
///   1. Filter gate'leri (slope / VWAP context / VWAP reclaim / volume) —
///      MicroScalper ile aynı davranış.
///   2. ATR dinamik TP/SL — <c>atr14 × multiplier / entry</c> clip içinde.
///   3. ATR clip — Min/Max sınırlarında çit bağlayıcı.
///   4. ATR fallback — <c>atr14 = 0</c> iken <c>TpGrossPct/StopPct</c> fallback.
///   5. SlopeTolerance sınırı — AR-GE default -0.003 hafif negatif slope'a izin.
/// </summary>
public class AtrScalperVwapEmaEvaluatorTests
{
    private const string Symbol = "SOLUSDT";

    // AR-GE §5 SOL-AtrScalper seed parametreleri (clip çitleri loose — ATR
    // multiplier testlerinde bağlayıcı değil).
    private static readonly string DefaultParams =
        "{\"KlineInterval\":\"1m\",\"EmaTimeframe\":\"1m\",\"EmaPeriod\":20," +
        "\"VwapWindowBars\":15,\"VwapTolerancePct\":0.008,\"VolumeSmaBars\":20," +
        "\"VolumeMultiplier\":0.5,\"SlopeTolerance\":-0.003," +
        "\"AtrPeriod\":14,\"TpAtrMultiplier\":1.5,\"SlAtrMultiplier\":0.8," +
        "\"MinTpPct\":0.001,\"MaxTpPct\":0.050,\"MinSlPct\":0.0005,\"MaxSlPct\":0.025," +
        "\"MaxHoldMinutes\":6}";

    private static (AtrScalperVwapEmaEvaluator Sut, Mock<IMarketIndicatorService> Indicators)
        Build(MicroScalperIndicatorSnapshot? snapshot, string symbol = Symbol)
    {
        var mock = new Mock<IMarketIndicatorService>();
        mock.Setup(i => i.TryGetMicroScalperSnapshot(symbol)).Returns(snapshot);
        var sut = new AtrScalperVwapEmaEvaluator(
            mock.Object, NullLogger<AtrScalperVwapEmaEvaluator>.Instance);
        return (sut, mock);
    }

    private static MicroScalperIndicatorSnapshot HappySnapshot(
        decimal vwap = 100m,
        decimal prevBarClose = 99.8m,     // below vwap (pullback)
        decimal lastBarClose = 100.2m,    // above vwap (reclaim)
        decimal lastBarVolume = 160m,     // 1.6× sma20 → clears 0.5 multiplier
        decimal volumeSma20 = 100m,
        decimal ema20Now = 100.05m,       // rising (slope > 0)
        decimal ema20Prev = 100.00m,
        decimal atr14 = 0.5m)             // ATR = 0.50 @ price 100.2 → ~%0.499
    {
        return new MicroScalperIndicatorSnapshot(
            Vwap: vwap,
            PrevBarClose: prevBarClose,
            LastBarClose: lastBarClose,
            LastBarVolume: lastBarVolume,
            VolumeSma20: volumeSma20,
            Ema20Now: ema20Now,
            Ema20Prev: ema20Prev,
            AsOf: DateTimeOffset.UtcNow,
            Atr14: atr14);
    }

    [Fact]
    public void Type_IsAtrScalperVwapEma1m()
    {
        var (sut, _) = Build(snapshot: null);
        sut.Type.Should().Be(StrategyType.AtrScalperVwapEma1m);
    }

    [Fact]
    public async Task NullSnapshot_ReturnsNullSignal()
    {
        var (sut, _) = Build(snapshot: null);

        var result = await sut.EvaluateAsync(
            strategyId: 1, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task AllConditionsMet_WithAtr_EmitsLong_DynamicGeometry()
    {
        // entry=100.2, atr14=0.5, TpMult=1.5, SlMult=0.8
        // rawTpPct = 0.5 * 1.5 / 100.2 = 0.007485...
        // rawSlPct = 0.5 * 0.8 / 100.2 = 0.003992...
        // Clip çitleri loose — bağlayıcı değil, raw değerler kullanılır.
        // TP = 100.2 * (1 + 0.007485) = 100.95005
        // SL = 100.2 * (1 - 0.003992) = 99.80000
        var snapshot = HappySnapshot();
        var (sut, _) = Build(snapshot);

        var result = await sut.EvaluateAsync(
            strategyId: 1, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(), CancellationToken.None);

        result.Should().NotBeNull();
        result!.Direction.Should().Be(StrategySignalDirection.Long);
        result.SuggestedPrice.Should().Be(100.2m);
        result.SuggestedTakeProfit.Should().BeApproximately(100.9500m, 0.0005m);
        result.SuggestedStopPrice.Should().BeApproximately(99.8000m, 0.0005m);
        result.ContextJson.Should().Contain("atr-scalper-vwap-ema-1m");
        result.ContextJson.Should().Contain("atr-dynamic");
        result.ContextJson.Should().Contain("atr14");
        result.ContextJson.Should().Contain("maxHoldMinutes");
    }

    [Fact]
    public async Task AtrZero_FallsBackTo_TpGrossPct_AndStopPct()
    {
        // Fallback path: ATR14 = 0 → TpGrossPct/StopPct kullanılır. Default
        // params'ta bunlar 0 — fallback+clip MinTpPct/MinSlPct'ye sabitlenir.
        var fallbackParams = DefaultParams.Replace(
            "\"MaxHoldMinutes\":6",
            "\"TpGrossPct\":0.006,\"StopPct\":0.0035,\"MaxHoldMinutes\":6");
        var snapshot = HappySnapshot(atr14: 0m);
        var (sut, _) = Build(snapshot);

        var result = await sut.EvaluateAsync(
            strategyId: 1, fallbackParams, Symbol,
            closedBars: Array.Empty<Kline>(), CancellationToken.None);

        result.Should().NotBeNull();
        // TP = 100.2 * 1.006 = 100.8012
        result!.SuggestedTakeProfit.Should().BeApproximately(100.8012m, 0.0005m);
        // SL = 100.2 * 0.9965 = 99.8493
        result.SuggestedStopPrice.Should().BeApproximately(99.8493m, 0.0005m);
        result.ContextJson.Should().Contain("atr-fallback");
    }

    [Fact]
    public async Task AtrExtreme_ClippedTo_MaxTpPct()
    {
        // Çok yüksek ATR (gerçek SOL ani spike senaryosu) — TP clip çitine
        // sabitlenir. MaxTpPct=0.050 default params'ta; atr14=10 → raw≈%15.
        var snapshot = HappySnapshot(atr14: 10m);
        var (sut, _) = Build(snapshot);

        var result = await sut.EvaluateAsync(
            strategyId: 1, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(), CancellationToken.None);

        result.Should().NotBeNull();
        // TP clip: 100.2 * 1.050 = 105.21
        result!.SuggestedTakeProfit.Should().BeApproximately(105.21m, 0.01m);
        // SL clip: 100.2 * (1 - 0.025) = 97.695
        result.SuggestedStopPrice.Should().BeApproximately(97.695m, 0.01m);
    }

    [Fact]
    public async Task SlopeTolerance_Negative_AllowsMildDownwardSlope()
    {
        // AR-GE SlopeTolerance = -0.003 hafif düşüş izni — emaNow 99.8 &
        // emaPrev 100 → slope = -0.002 (> -0.003 gate). directionGate passes.
        var snapshot = HappySnapshot(ema20Now: 99.8m, ema20Prev: 100m);
        var (sut, _) = Build(snapshot);

        var result = await sut.EvaluateAsync(
            strategyId: 1, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(), CancellationToken.None);

        result.Should().NotBeNull();
        result!.Direction.Should().Be(StrategySignalDirection.Long);
    }

    [Fact]
    public async Task SlopeTolerance_BreachedNegative_DoesNotEmit()
    {
        // slope = -0.01 < -0.003 → gate fails.
        var snapshot = HappySnapshot(ema20Now: 99m, ema20Prev: 100m);
        var (sut, _) = Build(snapshot);

        var result = await sut.EvaluateAsync(
            strategyId: 1, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task VwapContextMissing_PrevBarAboveVwap_DoesNotEmit()
    {
        // Pullback context required.
        var snapshot = HappySnapshot(prevBarClose: 100.3m);
        var (sut, _) = Build(snapshot);

        var result = await sut.EvaluateAsync(
            strategyId: 1, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task VwapReclaim_WithinTolerance_Emits()
    {
        // Last bar slightly below VWAP, ama VwapTolerancePct=0.008 zone içinde.
        // distance = |99.95 - 100| / 100 = 0.0005 < 0.008 → zone ok.
        var snapshot = HappySnapshot(lastBarClose: 99.95m);
        var (sut, _) = Build(snapshot);

        var result = await sut.EvaluateAsync(
            strategyId: 1, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(), CancellationToken.None);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task VolumeBelowMultiplier_DoesNotEmit()
    {
        // 40 / 100 = 0.4 < 0.5 → volume gate fails.
        var snapshot = HappySnapshot(lastBarVolume: 40m);
        var (sut, _) = Build(snapshot);

        var result = await sut.EvaluateAsync(
            strategyId: 1, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task IncompleteSnapshot_ZeroVwap_DoesNotEmit()
    {
        // Degenerate zero-vwap snapshot (pre-warmup guard).
        var snapshot = HappySnapshot(vwap: 0m);
        var (sut, _) = Build(snapshot);

        var result = await sut.EvaluateAsync(
            strategyId: 1, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task EmptyParameters_UsesDefaults_AndEmits()
    {
        // Empty params → Parameters defaults (VolMult 0.5, SlopeTol -0.003,
        // TpMult 1.5, SlMult 0.8, Atr snapshot 0.5).
        var snapshot = HappySnapshot();
        var (sut, _) = Build(snapshot);

        var result = await sut.EvaluateAsync(
            strategyId: 1, parametersJson: "{}", Symbol,
            closedBars: Array.Empty<Kline>(), CancellationToken.None);

        result.Should().NotBeNull();
        result!.Direction.Should().Be(StrategySignalDirection.Long);
    }

    [Fact]
    public async Task ContextJson_Contains_AtrMetadataAndClippedPcts()
    {
        var snapshot = HappySnapshot();
        var (sut, _) = Build(snapshot);

        var result = await sut.EvaluateAsync(
            strategyId: 1, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(), CancellationToken.None);

        result.Should().NotBeNull();
        result!.ContextJson.Should().Contain("tpAtrMultiplier");
        result.ContextJson.Should().Contain("slAtrMultiplier");
        result.ContextJson.Should().Contain("tpPct");
        result.ContextJson.Should().Contain("slPct");
        result.ContextJson.Should().Contain("volumeRatio");
        result.ContextJson.Should().Contain("slope");
    }
}
