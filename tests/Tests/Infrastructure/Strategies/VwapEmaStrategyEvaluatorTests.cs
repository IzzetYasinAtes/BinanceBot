using BinanceBot.Application.Strategies.Indicators;
using BinanceBot.Domain.MarketData;
using BinanceBot.Domain.Strategies;
using BinanceBot.Infrastructure.Strategies.Evaluators;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace BinanceBot.Tests.Infrastructure.Strategies;

/// <summary>
/// ADR-0016 §16.2 / §16.3 contract — <see cref="VwapEmaStrategyEvaluator"/> must emit a Long
/// signal only when the four V2 entry conditions hold simultaneously:
///   1. Slope gate: <c>nowEma &gt;= prevEma × (1 + SlopeTolerance)</c> (tolerance default -0.0005).
///   2. VWAP context: prev bar below VWAP.
///   3. VWAP reclaim or zone: <c>last &gt; VWAP</c> OR <c>|last − VWAP| / VWAP &lt; VwapTolerancePct</c>.
///   4. Volume confirm: <c>lastVol &gt; SMA20 × VolumeMultiplier</c> (default multiplier 1.05).
/// TP = <c>entry × (1 + TpGrossPct)</c>; SL = <c>entry × (1 − StopPct)</c>. Per-symbol
/// stop override is honoured when the seed carries <c>StopPctPerSymbol</c>.
/// </summary>
public class VwapEmaStrategyEvaluatorTests
{
    private const string Symbol = "BTCUSDT";

    // ADR-0016 §16.6 — seed-level defaults (BTC values). Tests that need the XRP
    // profile override StopPct per-test.
    private static readonly string DefaultParams =
        "{\"EmaPeriod\":21,\"EmaTimeframe\":\"1h\",\"VwapWindowBars\":1440," +
        "\"SlopeTolerance\":-0.0005,\"VwapTolerancePct\":0.0015," +
        "\"VolumeMultiplier\":1.05,\"StopPct\":0.003,\"TpGrossPct\":0.007," +
        "\"SwingLookback\":20,\"MaxHoldMinutes\":12}";

    private static (VwapEmaStrategyEvaluator Sut, Mock<IMarketIndicatorService> Indicators)
        Build(MarketIndicatorSnapshot? snapshot, string symbol = Symbol)
    {
        var mock = new Mock<IMarketIndicatorService>();
        mock.Setup(i => i.TryGetSnapshot(symbol)).Returns(snapshot);
        var sut = new VwapEmaStrategyEvaluator(
            mock.Object, NullLogger<VwapEmaStrategyEvaluator>.Instance);
        return (sut, mock);
    }

    private static MarketIndicatorSnapshot HappySnapshot(
        decimal vwap = 100m,
        decimal prevBarClose = 99.8m,      // below vwap
        decimal lastBarClose = 100.2m,     // above vwap
        decimal lastBarVolume = 150m,      // 1.5x sma20 — clears 1.05 threshold
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
    public async Task AllConditionsMet_UsesFixedTpGrossPct_EmitsLong()
    {
        // entry = lastBarClose = 100.2 → TP = 100.2 × 1.007 = 100.9014
        //                                SL = 100.2 × 0.997 = 99.8994
        var snapshot = HappySnapshot();
        var (sut, _) = Build(snapshot);

        var result = await sut.EvaluateAsync(
            strategyId: 1, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(), CancellationToken.None);

        result.Should().NotBeNull();
        result!.Direction.Should().Be(StrategySignalDirection.Long);
        result.SuggestedTakeProfit.Should().BeApproximately(100.9014m, 0.0001m);
        result.SuggestedStopPrice.Should().BeApproximately(99.8994m, 0.0001m);
        result.ContextJson.Should().Contain("vwap-ema-hybrid-v2");
        result.ContextJson.Should().Contain("tpGrossPct");
    }

    [Fact]
    public async Task Emits_When_Slope_Within_Tolerance()
    {
        // nowEma = prevEma × 0.99995 (slope ≈ -0.005%) which exceeds the default
        // SlopeTolerance of -0.0005 (-0.05%) → directionGate = true.
        var prev = 100m;
        var now = prev * 0.99995m;
        var snapshot = HappySnapshot(ema1h21Now: now, ema1h21Prev: prev);
        var (sut, _) = Build(snapshot);

        var result = await sut.EvaluateAsync(
            strategyId: 1, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(), CancellationToken.None);

        result.Should().NotBeNull();
        result!.Direction.Should().Be(StrategySignalDirection.Long);
    }

    [Fact]
    public async Task DoesNotEmit_When_Slope_Below_Tolerance()
    {
        // nowEma = prevEma × 0.999 (slope -0.1%) — below tolerance (-0.05%) → skip.
        var prev = 100m;
        var now = prev * 0.999m;
        var snapshot = HappySnapshot(ema1h21Now: now, ema1h21Prev: prev);
        var (sut, _) = Build(snapshot);

        var result = await sut.EvaluateAsync(
            strategyId: 1, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Emits_When_Price_In_Vwap_Zone_Even_If_Last_Below_Vwap()
    {
        // last = VWAP × 0.9988 → distance ≈ 0.0012 < VwapTolerancePct 0.0015
        // prev stays below VWAP so vwapContext holds. Reclaim path triggers via the zone.
        const decimal vwap = 100m;
        const decimal last = 99.88m;           // |100-99.88|/100 = 0.0012
        var snapshot = HappySnapshot(
            vwap: vwap,
            prevBarClose: 99.5m,
            lastBarClose: last);
        var (sut, _) = Build(snapshot);

        var result = await sut.EvaluateAsync(
            strategyId: 1, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(), CancellationToken.None);

        result.Should().NotBeNull();
        result!.Direction.Should().Be(StrategySignalDirection.Long);
    }

    [Fact]
    public async Task DoesNotEmit_When_Price_Outside_Vwap_Zone_And_Below()
    {
        // last = VWAP × 0.997 → distance = 0.003 > 0.0015 → zone fails, and last < vwap.
        var snapshot = HappySnapshot(vwap: 100m, prevBarClose: 99.5m, lastBarClose: 99.7m);
        var (sut, _) = Build(snapshot);

        var result = await sut.EvaluateAsync(
            strategyId: 1, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task VwapContextMissing_PrevBarAboveVwap_DoesNotEmit()
    {
        // Both bars above VWAP — no pullback context.
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
        // Volume 104 / SMA 100 = 1.04, below default 1.05 multiplier (strict >).
        var snapshot = HappySnapshot(lastBarVolume: 104m);
        var (sut, _) = Build(snapshot);

        var result = await sut.EvaluateAsync(
            strategyId: 1, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Uses_Per_Symbol_StopPct_Via_Dictionary()
    {
        // ParametersJson carries StopPctPerSymbol {XRPUSDT: 0.004, BTCUSDT: 0.003}.
        const string symbol = "XRPUSDT";
        const string xrpParams =
            "{\"SlopeTolerance\":-0.0005,\"VwapTolerancePct\":0.0015," +
            "\"VolumeMultiplier\":1.05,\"StopPct\":0.003,\"TpGrossPct\":0.007," +
            "\"MaxHoldMinutes\":12,\"StopPctPerSymbol\":{\"XRPUSDT\":0.004}}";
        var snapshot = HappySnapshot();
        var (sut, _) = Build(snapshot, symbol);

        var result = await sut.EvaluateAsync(
            strategyId: 1, xrpParams, symbol,
            closedBars: Array.Empty<Kline>(), CancellationToken.None);

        result.Should().NotBeNull();
        // entry = 100.2, XRP override stopPct = 0.004 → stop = 100.2 × 0.996 = 99.7992
        result!.SuggestedStopPrice.Should().BeApproximately(99.7992m, 0.0001m);
    }

    [Fact]
    public async Task Uses_Seed_StopPct_When_No_Per_Symbol_Override()
    {
        // Default params carry StopPct=0.003 with no StopPctPerSymbol entry for BTCUSDT.
        // entry = 100.2 → stop = 100.2 × 0.997 = 99.8994.
        var snapshot = HappySnapshot();
        var (sut, _) = Build(snapshot);

        var result = await sut.EvaluateAsync(
            strategyId: 1, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(), CancellationToken.None);

        result.Should().NotBeNull();
        result!.SuggestedStopPrice.Should().BeApproximately(99.8994m, 0.0001m);
    }

    [Fact]
    public async Task EmptyParameters_UsesAdr0016Defaults_AndEmits()
    {
        // Empty params → Parameters defaults (ADR-0016 §16.5):
        // StopPct=0.003, VolumeMultiplier=1.05, TpGrossPct=0.007, etc.
        var snapshot = HappySnapshot();
        var (sut, _) = Build(snapshot);

        var result = await sut.EvaluateAsync(
            strategyId: 1, parametersJson: "{}", Symbol,
            closedBars: Array.Empty<Kline>(), CancellationToken.None);

        result.Should().NotBeNull();
        result!.Direction.Should().Be(StrategySignalDirection.Long);
        // TP = 100.2 × 1.007 = 100.9014; SL = 100.2 × 0.997 = 99.8994
        result.SuggestedTakeProfit.Should().BeApproximately(100.9014m, 0.0001m);
        result.SuggestedStopPrice.Should().BeApproximately(99.8994m, 0.0001m);
    }

    [Fact]
    public async Task Type_IsVwapEmaHybrid()
    {
        var (sut, _) = Build(snapshot: null);
        sut.Type.Should().Be(StrategyType.VwapEmaHybrid);
    }
}
