using BinanceBot.Application.Abstractions;
using BinanceBot.Application.Strategies.Cooldowns;
using BinanceBot.Application.Strategies.Indicators;
using BinanceBot.Application.Strategies.Patterns;
using BinanceBot.Domain.MarketData;
using BinanceBot.Domain.Strategies;
using BinanceBot.Domain.ValueObjects;
using BinanceBot.Infrastructure.Strategies.Cooldowns;
using BinanceBot.Infrastructure.Strategies.Evaluators;
using BinanceBot.Infrastructure.Strategies.Patterns;
using BinanceBot.Infrastructure.Strategies.Patterns.Detectors;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace BinanceBot.Tests.Infrastructure.Strategies.Patterns;

/// <summary>
/// Loop 81 — PatternCompositeEvaluator integration: snapshot mock + real
/// composer + real registry. 4 senaryo.
/// </summary>
public class PatternCompositeEvaluatorTests
{
    private const string SymbolStr = "BTCUSDT";
    private const long StrategyId = 1L;

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
        public long BinanceServerTimeMs => UtcNow.ToUnixTimeMilliseconds();
        public long DriftMs => 0;
    }

    private static BarSnapshot MakeFullEmitSnapshot()
    {
        // EMA squeeze break + volume spike donchian + volume gate + spread + ADX in regime
        var bars = new List<Kline>();
        for (var i = 0; i < 30; i++)
        {
            var t = DateTimeOffset.UnixEpoch.AddMinutes(i * 5);
            bars.Add(Kline.Ingest(
                Symbol.From("BTCUSDT"), KlineInterval.FiveMinutes,
                openTime: t, closeTime: t.AddMinutes(5),
                open: 100m, high: 100.5m, low: 99.5m, close: 100m,
                volume: 100m, quoteVolume: 10000m, tradeCount: 50,
                takerBuyBase: 0m, takerBuyQuote: 0m, isClosed: true));
        }

        return new BarSnapshot(
            Symbol: SymbolStr,
            BarOpenTime: DateTimeOffset.UtcNow.AddMinutes(-5),
            BarCloseTime: DateTimeOffset.UtcNow,
            Open: 99.5m, High: 101m, Low: 99m, Close: 100.5m, // close > ema21=100
            Volume: 300m, TradeCount: 150, // surge ratio 3
            PrevOpen: 99m, PrevHigh: 99.5m, PrevLow: 98.5m, PrevClose: 99m,
            PrevTradeCount: 50,
            Rsi14: 55m, Rsi14Prev: 50m,
            Ema9: 100.3m, Ema9Prev: 100m,
            Ema21: 100m, Ema21Prev5: 99.5m,
            Ema50: 99m, Ema200: 98m,
            Atr14: 0.5m,
            BollingerLower: 99m, BollingerMiddle: 100m, BollingerUpper: 100.5m,
            BollingerBandWidth: 0.002m, BollingerBandWidthMin6: 0.002m, // squeeze
            Adx14: 22m, // in regime
            AvgVolume20: 100m, AvgTradeCount20: 50m,
            DonchianHigh20: 100m, DonchianLow20: 99m, // close 100.5 > 100 break
            Vwap: 99.8m,
            MacdLine: 0.05m, MacdLinePrev: 0.04m,
            SpreadPct: 0.0005m,
            // Loop 87 — MTF gate slope > 0 (büyük TF yukarı): 100.4 - 99.9 = +0.5 ⇒ pass.
            Ema21_15m: 100.4m,
            Ema21Prev5_15m: 99.9m,
            RecentBars: bars);
    }

    private static PatternCompositeEvaluator Build(BarSnapshot? snap, ICooldownService? cooldown = null)
    {
        var indicators = new Mock<IMarketIndicatorService>();
        indicators.Setup(i => i.TryGetBarSnapshot(It.IsAny<string>())).Returns(snap);

        var detectors = new IPatternDetector[]
        {
            new EmaSqueezeBreakDetector(),
            new VwapBounceDetector(),
            new InsideBarBreakoutDetector(),
            new RsiOversoldRecoveryDetector(),
            new VolumeSpikeDonchianDetector(),
            new HigherLowEmaTouchDetector(),
            new MacdZeroCrossDetector(),
            new BullishEngulfingDetector(),
            new HammerReversalDetector(),
            new BollingerLowerReversalDetector(),
            new VolumeSurgeGate(),
            new SpreadGuardGate(),
            new AdxRegimeFilter(),
        };
        var registry = new PatternRegistry(detectors);
        var composer = new WeightedScorePatternComposer(NullLogger<WeightedScorePatternComposer>.Instance);
        var cd = cooldown ?? new CooldownService(NullLogger<CooldownService>.Instance);
        var clock = new FixedClock(DateTimeOffset.UtcNow);

        return new PatternCompositeEvaluator(
            indicators.Object, registry, composer, cd, clock,
            NullLogger<PatternCompositeEvaluator>.Instance);
    }

    private const string DefaultParams =
        "{\"RequiredScore\":5,\"SlAtrMultiplier\":1.2,\"MinSlPct\":0.006,\"MaxSlPct\":0.012," +
        "\"TpRiskRewardRatio\":2.0,\"MaxHoldMinutes\":60,\"CooldownBarsAfterSignal\":2}";

    [Fact]
    public void Type_IsPatternComposite()
    {
        var sut = Build(snap: null);
        sut.Type.Should().Be(StrategyType.PatternComposite);
    }

    [Fact]
    public async Task EvaluateAsync_NullSnapshot_ReturnsNull()
    {
        var sut = Build(snap: null);
        var r = await sut.EvaluateAsync(StrategyId, DefaultParams, SymbolStr, Array.Empty<Kline>(), default);
        r.Should().BeNull();
    }

    [Fact]
    public async Task EvaluateAsync_FullEmitSnapshot_ReturnsLongSignalWithGeometry()
    {
        var sut = Build(MakeFullEmitSnapshot());
        var r = await sut.EvaluateAsync(StrategyId, DefaultParams, SymbolStr, Array.Empty<Kline>(), default);
        r.Should().NotBeNull();
        r!.Direction.Should().Be(StrategySignalDirection.Long);
        r.SuggestedPrice.Should().Be(100.5m);
        r.SuggestedStopPrice.Should().NotBeNull();
        r.SuggestedTakeProfit.Should().NotBeNull();
        r.SuggestedTakeProfit!.Value.Should().BeGreaterThan(r.SuggestedPrice!.Value);
        r.SuggestedStopPrice!.Value.Should().BeLessThan(r.SuggestedPrice.Value);
    }

    [Fact]
    public async Task EvaluateAsync_CooldownActive_ReturnsNullAfterFirstEmit()
    {
        var snap = MakeFullEmitSnapshot();
        var cd = new CooldownService(NullLogger<CooldownService>.Instance);
        var sut = Build(snap, cd);

        var first = await sut.EvaluateAsync(StrategyId, DefaultParams, SymbolStr, Array.Empty<Kline>(), default);
        first.Should().NotBeNull();

        var second = await sut.EvaluateAsync(StrategyId, DefaultParams, SymbolStr, Array.Empty<Kline>(), default);
        second.Should().BeNull();
    }

    // ────────── Loop 87 — MTF + RSI cap gate testleri ──────────

    [Fact]
    public async Task EvaluateAsync_Mtf15mSlopeDown_ReturnsNullSkip()
    {
        // Slope 15m negatif (büyük TF aşağı): emit_score yeterli olsa bile skip.
        var snap = MakeFullEmitSnapshot() with
        {
            Ema21_15m = 100m,
            Ema21Prev5_15m = 100.5m, // slope = -0.5 ⇒ skip
        };
        var sut = Build(snap);
        var r = await sut.EvaluateAsync(StrategyId, DefaultParams, SymbolStr, Array.Empty<Kline>(), default);
        r.Should().BeNull("15m EMA21 slope ≤ 0 ⇒ MTF gate skip");
    }

    [Fact]
    public async Task EvaluateAsync_Mtf15mWarmupNotReady_ReturnsNullSkip()
    {
        // 15m warmup tamamlanmamış (Ema21_15m = 0): güvenli tarafa skip.
        var snap = MakeFullEmitSnapshot() with
        {
            Ema21_15m = 0m,
            Ema21Prev5_15m = 0m,
        };
        var sut = Build(snap);
        var r = await sut.EvaluateAsync(StrategyId, DefaultParams, SymbolStr, Array.Empty<Kline>(), default);
        r.Should().BeNull("15m EMA21 == 0 ⇒ warmup yetersiz, MTF gate skip");
    }

    [Fact]
    public async Task EvaluateAsync_Mtf15mSlopePositive_StillEmits()
    {
        // Slope pozitif (büyük TF yukarı) + score yeterli ⇒ emit.
        var snap = MakeFullEmitSnapshot() with
        {
            Ema21_15m = 101m,
            Ema21Prev5_15m = 100m, // slope +1 ⇒ pass
        };
        var sut = Build(snap);
        var r = await sut.EvaluateAsync(StrategyId, DefaultParams, SymbolStr, Array.Empty<Kline>(), default);
        r.Should().NotBeNull("15m slope > 0 ⇒ MTF gate pass + score yeterli ⇒ emit");
    }

    [Fact]
    public async Task EvaluateAsync_Rsi14AboveCap_ReturnsNullSkip()
    {
        // RSI 80 > cap 75 ⇒ aşırı alım skip.
        var snap = MakeFullEmitSnapshot() with { Rsi14 = 80m };
        var sut = Build(snap);
        var r = await sut.EvaluateAsync(StrategyId, DefaultParams, SymbolStr, Array.Empty<Kline>(), default);
        r.Should().BeNull("RSI > RsiMaxEmit ⇒ rsi_overbought skip");
    }

    [Fact]
    public async Task EvaluateAsync_Rsi14AtCap_StillEmits()
    {
        // RSI cap (75) sınırda — strict üstünde değil ⇒ pass.
        var snap = MakeFullEmitSnapshot() with { Rsi14 = 75m };
        var sut = Build(snap);
        var r = await sut.EvaluateAsync(StrategyId, DefaultParams, SymbolStr, Array.Empty<Kline>(), default);
        r.Should().NotBeNull("RSI == cap (strict-greater-than gate) ⇒ pass");
    }

    [Fact]
    public async Task EvaluateAsync_RsiCapOverridenLow_SkipsEvenWithinDefault()
    {
        // ParametersJson içinde RsiMaxEmit=50, snapshot RSI=55 ⇒ skip.
        const string lowCap =
            "{\"RequiredScore\":5,\"SlAtrMultiplier\":1.2,\"MinSlPct\":0.006,\"MaxSlPct\":0.012," +
            "\"TpRiskRewardRatio\":2.0,\"MaxHoldMinutes\":60,\"CooldownBarsAfterSignal\":2," +
            "\"RsiMaxEmit\":50}";
        var sut = Build(MakeFullEmitSnapshot()); // RSI default 55
        var r = await sut.EvaluateAsync(StrategyId, lowCap, SymbolStr, Array.Empty<Kline>(), default);
        r.Should().BeNull("RsiMaxEmit override ile RSI 55 > 50 ⇒ skip");
    }
}
