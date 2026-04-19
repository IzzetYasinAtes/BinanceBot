using BinanceBot.Application.Strategies.Indicators;
using BinanceBot.Domain.MarketData;
using BinanceBot.Domain.Strategies;
using BinanceBot.Infrastructure.Strategies.Evaluators;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace BinanceBot.Tests.Infrastructure.Strategies;

/// <summary>
/// ADR-0015 §15.2 contract — <see cref="VwapEmaStrategyEvaluator"/> must emit a Long
/// signal only when the four entry conditions hold simultaneously: direction gate
/// (EMA21 1h rising), VWAP context (prev bar below VWAP), VWAP reclaim (last bar above
/// VWAP) and volume confirm (last volume >= SMA20 × multiplier). Stop = entry × (1 −
/// stopPct); TP = swingHigh20 × tpSafetyFactor. Null snapshots yield no signal.
/// </summary>
public class VwapEmaStrategyEvaluatorTests
{
    private const string Symbol = "BTCUSDT";

    private static readonly string DefaultParams =
        "{\"EmaPeriod\":21,\"EmaTimeframe\":\"1h\",\"VwapWindowBars\":1440," +
        "\"StopPct\":0.008,\"SwingLookback\":20,\"VolumeMultiplier\":1.2," +
        "\"TimeStopMinutes\":15}";

    private static (VwapEmaStrategyEvaluator Sut, Mock<IMarketIndicatorService> Indicators)
        Build(MarketIndicatorSnapshot? snapshot)
    {
        var mock = new Mock<IMarketIndicatorService>();
        mock.Setup(i => i.TryGetSnapshot(Symbol)).Returns(snapshot);
        var sut = new VwapEmaStrategyEvaluator(
            mock.Object, NullLogger<VwapEmaStrategyEvaluator>.Instance);
        return (sut, mock);
    }

    private static MarketIndicatorSnapshot HappySnapshot(
        decimal vwap = 100m,
        decimal prevBarClose = 99.8m,      // below vwap
        decimal lastBarClose = 100.2m,     // above vwap
        decimal lastBarVolume = 150m,      // 1.5x sma20
        decimal volumeSma20 = 100m,
        decimal ema1h21Now = 99.5m,        // rising
        decimal ema1h21Prev = 99.4m,
        decimal swingHigh20 = 101.5m)
    {
        return new MarketIndicatorSnapshot(
            Vwap: vwap,
            PrevBarClose: prevBarClose,
            LastBarClose: lastBarClose,
            LastBarVolume: lastBarVolume,
            VolumeSma20: volumeSma20,
            Ema1h21Now: ema1h21Now,
            Ema1h21Prev: ema1h21Prev,
            SwingHigh20: swingHigh20,
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
    public async Task AllConditionsMet_TpAboveEntry_EmitsLong()
    {
        // swingHigh20 × 0.95 must be > entry (100.2). Require swingHigh ≥ 105.5 to produce TP ≈ 100.225.
        var snapshot = HappySnapshot(swingHigh20: 110m);
        var (sut, _) = Build(snapshot);

        var result = await sut.EvaluateAsync(
            strategyId: 1, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(), CancellationToken.None);

        result.Should().NotBeNull();
        result!.Direction.Should().Be(StrategySignalDirection.Long);
        // TP = 110 × 0.95 = 104.5
        result.SuggestedTakeProfit.Should().Be(104.5m);
        result.SuggestedStopPrice.Should().BeApproximately(99.3984m, 0.0001m);
        result.ContextJson.Should().Contain("vwap-ema-hybrid");
    }

    [Fact]
    public async Task DirectionGateDown_DoesNotEmit()
    {
        var snapshot = HappySnapshot(ema1h21Now: 99.3m, ema1h21Prev: 99.4m, swingHigh20: 110m);
        var (sut, _) = Build(snapshot);

        var result = await sut.EvaluateAsync(
            strategyId: 1, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task VwapContextMissing_PrevBarAboveVwap_DoesNotEmit()
    {
        // Both bars above VWAP — no pullback, no reclaim pattern.
        var snapshot = HappySnapshot(prevBarClose: 100.3m, swingHigh20: 110m);
        var (sut, _) = Build(snapshot);

        var result = await sut.EvaluateAsync(
            strategyId: 1, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task VwapReclaimMissing_LastBarBelowVwap_DoesNotEmit()
    {
        // Both bars below VWAP — never reclaimed.
        var snapshot = HappySnapshot(lastBarClose: 99.9m, swingHigh20: 110m);
        var (sut, _) = Build(snapshot);

        var result = await sut.EvaluateAsync(
            strategyId: 1, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task VolumeBelowMultiplier_DoesNotEmit()
    {
        // Volume 110 / SMA 100 = 1.10, below default 1.2 multiplier.
        var snapshot = HappySnapshot(lastBarVolume: 110m, swingHigh20: 110m);
        var (sut, _) = Build(snapshot);

        var result = await sut.EvaluateAsync(
            strategyId: 1, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task TakeProfitBelowEntry_GeometryGuard_DoesNotEmit()
    {
        // swingHigh too low -> TP < entry -> reject.
        var snapshot = HappySnapshot(swingHigh20: 101m);  // TP = 95.95 < 100.2
        var (sut, _) = Build(snapshot);

        var result = await sut.EvaluateAsync(
            strategyId: 1, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task EmptyParameters_UsesDefaultsAndEmits()
    {
        // Default stopPct=0.008, multiplier=1.2, tpSafety=0.95 — with the happy snapshot
        // and swingHigh20=110, the evaluator should still emit.
        var snapshot = HappySnapshot(swingHigh20: 110m);
        var (sut, _) = Build(snapshot);

        var result = await sut.EvaluateAsync(
            strategyId: 1, parametersJson: "{}", Symbol,
            closedBars: Array.Empty<Kline>(), CancellationToken.None);

        result.Should().NotBeNull();
        result!.Direction.Should().Be(StrategySignalDirection.Long);
    }

    [Fact]
    public async Task Type_IsVwapEmaHybrid()
    {
        var (sut, _) = Build(snapshot: null);
        sut.Type.Should().Be(StrategyType.VwapEmaHybrid);
    }
}
