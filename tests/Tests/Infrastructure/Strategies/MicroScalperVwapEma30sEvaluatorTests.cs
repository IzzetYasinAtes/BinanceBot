using BinanceBot.Application.Strategies.Indicators;
using BinanceBot.Domain.MarketData;
using BinanceBot.Domain.Strategies;
using BinanceBot.Infrastructure.Strategies.Evaluators;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace BinanceBot.Tests.Infrastructure.Strategies;

/// <summary>
/// ADR-0018 §18.7 + §18.8 + §18.9 contract — <see cref="MicroScalperVwapEma30sEvaluator"/>
/// must emit a Long signal only when all four 30sn entry conditions hold:
///   1. Slope gate: <c>emaNow &gt; emaPrev × (1 + SlopeTolerance)</c>
///      (default SlopeTolerance = 0 → strict positive slope).
///   2. VWAP context: prev bar close &lt; VWAP (pullback).
///   3. VWAP reclaim: last bar close &gt; VWAP (strict, tolerance default 0).
///   4. Volume confirm: <c>lastVolume &gt;= volumeSma20 × VolumeMultiplier</c>
///      (default multiplier 1.5).
/// TP = <c>entry × (1 + TpGrossPct)</c>; SL = <c>entry × (1 − StopPct)</c>;
/// ContextJson carries <c>maxHoldMinutes = 2</c> (ADR-0017 §17.7 TimeStop key).
/// </summary>
public class MicroScalperVwapEma30sEvaluatorTests
{
    private const string Symbol = "BTCUSDT";

    // ADR-0018 §18.7 seed-level defaults.
    private static readonly string DefaultParams =
        "{\"KlineInterval\":\"30s\",\"EmaTimeframe\":\"30s\",\"EmaPeriod\":20," +
        "\"VwapWindowBars\":15,\"VwapTolerancePct\":0.0,\"VolumeSmaBars\":20," +
        "\"VolumeMultiplier\":1.5,\"SlopeTolerance\":0.0," +
        "\"TpGrossPct\":0.006,\"StopPct\":0.0035,\"MaxHoldMinutes\":2}";

    private static (MicroScalperVwapEma30sEvaluator Sut, Mock<IMarketIndicatorService> Indicators)
        Build(MicroScalperIndicatorSnapshot? snapshot, string symbol = Symbol)
    {
        var mock = new Mock<IMarketIndicatorService>();
        mock.Setup(i => i.TryGetMicroScalperSnapshot(symbol)).Returns(snapshot);
        var sut = new MicroScalperVwapEma30sEvaluator(
            mock.Object, NullLogger<MicroScalperVwapEma30sEvaluator>.Instance);
        return (sut, mock);
    }

    private static MicroScalperIndicatorSnapshot HappySnapshot(
        decimal vwap = 100m,
        decimal prevBarClose = 99.8m,     // below vwap (pullback)
        decimal lastBarClose = 100.2m,    // above vwap (reclaim)
        decimal lastBarVolume = 160m,     // 1.6× sma20 → clears 1.5 multiplier
        decimal volumeSma20 = 100m,
        decimal ema20Now = 100.05m,       // rising (slope > 0)
        decimal ema20Prev = 100.00m)
    {
        return new MicroScalperIndicatorSnapshot(
            Vwap: vwap,
            PrevBarClose: prevBarClose,
            LastBarClose: lastBarClose,
            LastBarVolume: lastBarVolume,
            VolumeSma20: volumeSma20,
            Ema20Now: ema20Now,
            Ema20Prev: ema20Prev,
            AsOf: DateTimeOffset.UtcNow);
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
    public async Task Type_IsMicroScalperVwapEma30s()
    {
        var (sut, _) = Build(snapshot: null);
        sut.Type.Should().Be(StrategyType.MicroScalperVwapEma30s);
    }

    [Fact]
    public async Task AllConditionsMet_EmitsLong_WithTpSlAndContext()
    {
        var snapshot = HappySnapshot();
        var (sut, _) = Build(snapshot);

        var result = await sut.EvaluateAsync(
            strategyId: 1, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(), CancellationToken.None);

        result.Should().NotBeNull();
        result!.Direction.Should().Be(StrategySignalDirection.Long);
        // entry = lastBarClose = 100.2 → TP = 100.2 * 1.006 = 100.8012
        result.SuggestedTakeProfit.Should().BeApproximately(100.8012m, 0.0001m);
        // SL = 100.2 * 0.9965 = 99.8493
        result.SuggestedStopPrice.Should().BeApproximately(99.8493m, 0.0001m);
        result.ContextJson.Should().Contain("micro-scalper-vwap-ema-30s");
        result.ContextJson.Should().Contain("maxHoldMinutes");
        result.ContextJson.Should().Contain("2");
    }

    [Fact]
    public async Task SlopeZero_DoesNotEmit_StrictPositiveSlopeRequired()
    {
        // emaNow == emaPrev → slope = 0 → directionGate fails with default
        // SlopeTolerance = 0 (strict greater-than).
        var snapshot = HappySnapshot(ema20Now: 100m, ema20Prev: 100m);
        var (sut, _) = Build(snapshot);

        var result = await sut.EvaluateAsync(
            strategyId: 1, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task SlopeNegative_DoesNotEmit()
    {
        // Downward slope fails the direction gate.
        var snapshot = HappySnapshot(ema20Now: 99.9m, ema20Prev: 100m);
        var (sut, _) = Build(snapshot);

        var result = await sut.EvaluateAsync(
            strategyId: 1, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task VwapReclaimMissing_LastBarBelowVwap_DoesNotEmit()
    {
        // Strict reclaim: lastBarClose must be > VWAP. Default tolerance 0 → no zone.
        var snapshot = HappySnapshot(lastBarClose: 99.95m);
        var (sut, _) = Build(snapshot);

        var result = await sut.EvaluateAsync(
            strategyId: 1, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task VwapContextMissing_PrevBarAboveVwap_DoesNotEmit()
    {
        // Pullback context required — both bars above VWAP = no pullback.
        var snapshot = HappySnapshot(prevBarClose: 100.3m);
        var (sut, _) = Build(snapshot);

        var result = await sut.EvaluateAsync(
            strategyId: 1, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task VolumeBelowMultiplier_DoesNotEmit()
    {
        // Volume 140 / SMA 100 = 1.4, below default 1.5 threshold → skip.
        var snapshot = HappySnapshot(lastBarVolume: 140m);
        var (sut, _) = Build(snapshot);

        var result = await sut.EvaluateAsync(
            strategyId: 1, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task VolumeExactlyAtMultiplier_Emits_GreaterOrEqual()
    {
        // Volume 150 / SMA 100 = 1.5 exactly — the check is >= (ADR-0018 §18.8).
        var snapshot = HappySnapshot(lastBarVolume: 150m);
        var (sut, _) = Build(snapshot);

        var result = await sut.EvaluateAsync(
            strategyId: 1, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(), CancellationToken.None);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task TpAndSlGeometry_MatchesAdrFormulas()
    {
        // Use a round entry = 200 so decimals land cleanly.
        var snapshot = HappySnapshot(
            vwap: 199m,
            prevBarClose: 198m,
            lastBarClose: 200m,
            ema20Now: 199.5m,
            ema20Prev: 199m);
        var (sut, _) = Build(snapshot);

        var result = await sut.EvaluateAsync(
            strategyId: 1, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(), CancellationToken.None);

        result.Should().NotBeNull();
        // TP = 200 × 1.006 = 201.20
        result!.SuggestedTakeProfit.Should().BeApproximately(201.20m, 0.0001m);
        // SL = 200 × 0.9965 = 199.30
        result.SuggestedStopPrice.Should().BeApproximately(199.30m, 0.0001m);
    }

    [Fact]
    public async Task EmptyParameters_UsesAdr0018Defaults_AndEmits()
    {
        // Empty params → Parameters defaults (ADR-0018 §18.7):
        // TpGrossPct=0.006, StopPct=0.0035, VolumeMultiplier=1.5,
        // SlopeTolerance=0, VwapTolerancePct=0, MaxHoldMinutes=2.
        var snapshot = HappySnapshot();
        var (sut, _) = Build(snapshot);

        var result = await sut.EvaluateAsync(
            strategyId: 1, parametersJson: "{}", Symbol,
            closedBars: Array.Empty<Kline>(), CancellationToken.None);

        result.Should().NotBeNull();
        result!.Direction.Should().Be(StrategySignalDirection.Long);
        result.SuggestedTakeProfit.Should().BeApproximately(100.8012m, 0.0001m);
        result.SuggestedStopPrice.Should().BeApproximately(99.8493m, 0.0001m);
    }

    [Fact]
    public async Task ContextJson_Contains_Ema20AndSlopeAndMaxHoldMinutes()
    {
        var snapshot = HappySnapshot();
        var (sut, _) = Build(snapshot);

        var result = await sut.EvaluateAsync(
            strategyId: 1, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(), CancellationToken.None);

        result.Should().NotBeNull();
        result!.ContextJson.Should().Contain("ema20_30sNow");
        result.ContextJson.Should().Contain("ema20_30sPrev");
        result.ContextJson.Should().Contain("slope");
        result.ContextJson.Should().Contain("volumeRatio");
        result.ContextJson.Should().Contain("maxHoldMinutes");
    }
}
