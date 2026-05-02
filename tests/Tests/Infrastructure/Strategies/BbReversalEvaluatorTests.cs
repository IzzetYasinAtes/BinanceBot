using BinanceBot.Application.Abstractions;
using BinanceBot.Application.Abstractions.Binance;
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
/// Loop 79 — <see cref="BbReversalEvaluator"/> kontrat testleri:
///   1. Type doğru.
///   2. Default snapshot (lower band yakın + RSI dip recovery + range BBW) ⇒ emit.
///   3. BBW &lt; rangeMin (Dead) ⇒ skip.
///   4. BBW &gt; rangeMax (Trending) ⇒ skip.
///   5. RSI ≥ oversold threshold ⇒ skip.
///   6. RSI declining (Rsi14 ≤ Prev) ⇒ skip.
///   7. Spread ≥ threshold ⇒ skip.
///   8. Lower band'a yeterince yakın değil ⇒ skip.
///   9. Cooldown aktif ⇒ ikinci çağrıda skip.
///  10. Snapshot null (warmup yetersiz) ⇒ skip.
/// Geometry kontrolü: TP = BB middle, SL = entry × (1 - BufferPctExit).
/// </summary>
public class BbReversalEvaluatorTests
{
    private const string Symbol = "BTCUSDT";
    private const long StrategyId = 99L;

    // binance-expert spec defaults — Loop 79 BB Reversal + Loop 80 (volume
    // surge gate, ADX hard-gate) parametre seti.
    private const string DefaultParams =
        "{\"RsiPeriod\":14,\"BbPeriod\":20,\"BbStdDev\":2.0,\"AtrPeriod\":14," +
        "\"BbwRangeMin\":0.003,\"BbwRangeMax\":0.010,\"RsiOversoldEntry\":35," +
        "\"BufferPctEntry\":0.0005,\"BufferPctExit\":0.001," +
        "\"MaxHoldMinutes\":20,\"SpreadThresholdPct\":0.005,\"CooldownBarsAfterSignal\":3," +
        "\"TradeCountWindow\":20,\"TradeCountSurgeMultiplier\":1.5," +
        "\"AdxGateEnabled\":true,\"AdxRangeMax\":25}";

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
        public long BinanceServerTimeMs => UtcNow.ToUnixTimeMilliseconds();
        public long DriftMs => 0;
    }

    private sealed class FakeBookTickerReader : IBookTickerReader
    {
        public WsBookTickerPayload? Payload { get; set; }
        public WsBookTickerPayload? GetLatest(string symbol) => Payload;
    }

    /// <summary>
    /// Snapshot factory — default değerler emit için optimize:
    ///   - CurrentClose = 99.95 (BollingerLower 100 × 1.0005 = 100.05 altında).
    ///   - Rsi14 = 30 < OversoldEntry 35 ⇒ oversold OK.
    ///   - Rsi14Prev = 28 (curr 30 > prev 28) ⇒ recovery OK.
    ///   - BollingerLower = 100, Mean = 101.
    ///   - BBW = 0.005 ∈ [0.003, 0.010] ⇒ range regime.
    /// Test'lerde tek tek parametre değişerek skip yolu test edilir.
    /// </summary>
    private static BbReversalSnapshot MakeSnapshot(
        decimal currentClose = 99.95m,
        decimal rsi14 = 30m,
        decimal rsi14Prev = 28m,
        decimal bollingerLower = 100m,
        decimal bollingerMean = 101m,
        decimal bollingerBandWidth = 0.005m,
        decimal atr14 = 0.40m,
        decimal avgTradeCount20 = 100m,
        // Default 200 > 100 × 1.5 ⇒ volume surge gate açık (emit yolu).
        int currentTradeCount = 200,
        // Default ADX 15 < 25 (range rejim) ⇒ ADX gate açık (emit yolu).
        decimal adx14 = 15m)
    {
        return new BbReversalSnapshot(
            CurrentClose: currentClose,
            Rsi14: rsi14,
            Rsi14Prev: rsi14Prev,
            BollingerLower: bollingerLower,
            BollingerMean: bollingerMean,
            BollingerBandWidth: bollingerBandWidth,
            Atr14: atr14,
            AvgTradeCount20: avgTradeCount20,
            CurrentTradeCount: currentTradeCount,
            Adx14: adx14,
            LastBarOpenTime: DateTimeOffset.UtcNow.AddMinutes(-5),
            AsOf: DateTimeOffset.UtcNow);
    }

    private static (BbReversalEvaluator Sut, FakeBookTickerReader Bt, ICooldownService Cd)
        Build(BbReversalSnapshot? snapshot, WsBookTickerPayload? booktick = null,
              ICooldownService? cooldown = null)
    {
        var indicators = new Mock<IMarketIndicatorService>();
        indicators.Setup(i => i.TryGetBbReversalSnapshot(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<decimal>(),
                It.IsAny<int>(),
                It.IsAny<int>()))
            .Returns(snapshot);

        var bt = new FakeBookTickerReader { Payload = booktick };
        var cd = cooldown ?? new CooldownService(NullLogger<CooldownService>.Instance);
        var clock = new FixedClock(DateTimeOffset.UtcNow);

        var sut = new BbReversalEvaluator(
            indicators.Object,
            bt,
            cd,
            clock,
            NullLogger<BbReversalEvaluator>.Instance);

        return (sut, bt, cd);
    }

    private static WsBookTickerPayload TightSpreadTicker(
        string symbol = Symbol, decimal bid = 99.94m, decimal ask = 99.95m) =>
        new(Symbol: symbol, UpdateId: 1, BidPrice: bid, BidQty: 1m,
            AskPrice: ask, AskQty: 1m, ReceivedAt: DateTimeOffset.UtcNow);

    [Fact]
    public void Type_IsBollingerBandReversal5m()
    {
        var (sut, _, _) = Build(snapshot: null);
        sut.Type.Should().Be(StrategyType.BollingerBandReversal5m);
    }

    /// <summary>
    /// Test 1 — Default koşullar ⇒ emit.
    /// TP = BB Mean = 101 ; SL = 99.95 × (1 - 0.001) = 99.85005
    /// </summary>
    [Fact]
    public async Task DefaultConditions_Emits_WithBbMiddleTakeProfit()
    {
        var snap = MakeSnapshot();
        var (sut, _, _) = Build(snap, TightSpreadTicker());

        var result = await sut.EvaluateAsync(
            StrategyId, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(),
            cancellationToken: CancellationToken.None);

        result.Should().NotBeNull();
        result!.Direction.Should().Be(StrategySignalDirection.Long);
        result.SuggestedPrice.Should().Be(99.95m);
        result.SuggestedTakeProfit.Should().Be(101m); // BB middle direkt
        result.SuggestedStopPrice.Should().BeApproximately(99.85m, 0.01m);

        result.ContextJson.Should().Contain("\"regime\":\"Range\"");
        result.ContextJson.Should().Contain("\"entryReason\":\"lower_band_oversold_recovery\"");
        result.ContextJson.Should().Contain("\"maxHoldMinutes\":20");
    }

    /// <summary>
    /// Test 2 — BBW &lt; rangeMin (Dead) ⇒ skip.
    /// </summary>
    [Fact]
    public async Task DeadRegime_BbwBelowMin_Skip()
    {
        var snap = MakeSnapshot(bollingerBandWidth: 0.001m); // < 0.003
        var (sut, _, _) = Build(snap, TightSpreadTicker());

        var result = await sut.EvaluateAsync(
            StrategyId, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(),
            cancellationToken: CancellationToken.None);

        result.Should().BeNull();
    }

    /// <summary>
    /// Test 3 — BBW &gt; rangeMax (Trending — KMS bölgesi) ⇒ skip.
    /// </summary>
    [Fact]
    public async Task TrendingRegime_BbwAboveMax_Skip()
    {
        var snap = MakeSnapshot(bollingerBandWidth: 0.020m); // > 0.010
        var (sut, _, _) = Build(snap, TightSpreadTicker());

        var result = await sut.EvaluateAsync(
            StrategyId, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(),
            cancellationToken: CancellationToken.None);

        result.Should().BeNull();
    }

    /// <summary>
    /// Test 4 — RSI ≥ oversold threshold (35) ⇒ skip.
    /// </summary>
    [Fact]
    public async Task RsiNotOversold_Skip()
    {
        var snap = MakeSnapshot(rsi14: 40m, rsi14Prev: 38m); // > 35 (oversold gate fail)
        var (sut, _, _) = Build(snap, TightSpreadTicker());

        var result = await sut.EvaluateAsync(
            StrategyId, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(),
            cancellationToken: CancellationToken.None);

        result.Should().BeNull();
    }

    /// <summary>
    /// Test 5 — RSI declining (Rsi14 ≤ Prev) ⇒ no recovery ⇒ skip.
    /// </summary>
    [Fact]
    public async Task RsiDeclining_NoRecovery_Skip()
    {
        var snap = MakeSnapshot(rsi14: 28m, rsi14Prev: 32m); // declining (28 < 32)
        var (sut, _, _) = Build(snap, TightSpreadTicker());

        var result = await sut.EvaluateAsync(
            StrategyId, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(),
            cancellationToken: CancellationToken.None);

        result.Should().BeNull();
    }

    /// <summary>
    /// Test 6 — Spread ≥ threshold ⇒ skip.
    /// Bid=95, Ask=100 ⇒ spread 0.05 &gt; 0.005.
    /// </summary>
    [Fact]
    public async Task SpreadAboveThreshold_Skip()
    {
        var snap = MakeSnapshot();
        var wide = new WsBookTickerPayload(
            Symbol: Symbol, UpdateId: 1,
            BidPrice: 95m, BidQty: 1m,
            AskPrice: 100m, AskQty: 1m,
            ReceivedAt: DateTimeOffset.UtcNow);
        var (sut, _, _) = Build(snap, wide);

        var result = await sut.EvaluateAsync(
            StrategyId, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(),
            cancellationToken: CancellationToken.None);

        result.Should().BeNull();
    }

    /// <summary>
    /// Test 7 — Close lower band'a yeterince yakın değil ⇒ skip.
    /// CurrentClose 100.50 &gt; lowerEntryThreshold (100 × 1.0005 = 100.05).
    /// </summary>
    [Fact]
    public async Task ClosePriceAboveLowerBandThreshold_Skip()
    {
        var snap = MakeSnapshot(currentClose: 100.50m); // > 100.05 threshold
        var (sut, _, _) = Build(snap, TightSpreadTicker(bid: 100.49m, ask: 100.50m));

        var result = await sut.EvaluateAsync(
            StrategyId, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(),
            cancellationToken: CancellationToken.None);

        result.Should().BeNull();
    }

    /// <summary>
    /// Test 8 — Cooldown: ilk emit sonrası ikinci çağrı 15dk pencerede skip.
    /// </summary>
    [Fact]
    public async Task Cooldown_AfterEmit_Skip()
    {
        var snap = MakeSnapshot();
        var cd = new CooldownService(NullLogger<CooldownService>.Instance);
        var (sut, _, _) = Build(snap, TightSpreadTicker(), cd);

        var first = await sut.EvaluateAsync(
            StrategyId, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(),
            cancellationToken: CancellationToken.None);
        first.Should().NotBeNull();

        var second = await sut.EvaluateAsync(
            StrategyId, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(),
            cancellationToken: CancellationToken.None);
        second.Should().BeNull();
    }

    /// <summary>
    /// Test 9 — Snapshot null (warmup yetersiz) ⇒ skip; cooldown'a yazılmaz.
    /// </summary>
    [Fact]
    public async Task SnapshotNull_Skip()
    {
        var (sut, _, _) = Build(snapshot: null);

        var result = await sut.EvaluateAsync(
            StrategyId, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(),
            cancellationToken: CancellationToken.None);

        result.Should().BeNull();
    }

    /// <summary>
    /// Test 10 — BookTicker yok ⇒ skip (yetersiz veri, defensive).
    /// </summary>
    [Fact]
    public async Task NoBookTicker_Skip()
    {
        var snap = MakeSnapshot();
        var (sut, _, _) = Build(snap, booktick: null);

        var result = await sut.EvaluateAsync(
            StrategyId, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(),
            cancellationToken: CancellationToken.None);

        result.Should().BeNull();
    }

    /// <summary>
    /// Loop 80 — Volume surge OK (CurrentTradeCount &gt; AvgTradeCount × 1.5) ⇒ emit.
    /// 200 &gt; 100 × 1.5 = 150 ⇒ pass.
    /// </summary>
    [Fact]
    public async Task VolumeSurge_AboveThreshold_Emits()
    {
        var snap = MakeSnapshot(avgTradeCount20: 100m, currentTradeCount: 200);
        var (sut, _, _) = Build(snap, TightSpreadTicker());

        var result = await sut.EvaluateAsync(
            StrategyId, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(),
            cancellationToken: CancellationToken.None);

        result.Should().NotBeNull();
        result!.ContextJson.Should().Contain("\"currentTradeCount\":200");
        result.ContextJson.Should().Contain("\"avgTradeCount20\":100");
    }

    /// <summary>
    /// Loop 80 — Volume surge altı (CurrentTradeCount ≤ AvgTradeCount × 1.5) ⇒ skip.
    /// 120 ≤ 100 × 1.5 = 150 ⇒ false-breakdown koruması; emit yok.
    /// </summary>
    [Fact]
    public async Task VolumeSurge_BelowThreshold_Skip()
    {
        var snap = MakeSnapshot(avgTradeCount20: 100m, currentTradeCount: 120);
        var (sut, _, _) = Build(snap, TightSpreadTicker());

        var result = await sut.EvaluateAsync(
            StrategyId, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(),
            cancellationToken: CancellationToken.None);

        result.Should().BeNull();
    }

    /// <summary>
    /// Loop 80 — Volume surge warmup bypass: <c>AvgTradeCount20 == 0</c>
    /// ⇒ gate açık (yeni bar, henüz 20 bar history yok). Emit kalır.
    /// </summary>
    [Fact]
    public async Task VolumeSurge_WarmupBypass_AvgZero_Emits()
    {
        var snap = MakeSnapshot(avgTradeCount20: 0m, currentTradeCount: 5);
        var (sut, _, _) = Build(snap, TightSpreadTicker());

        var result = await sut.EvaluateAsync(
            StrategyId, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(),
            cancellationToken: CancellationToken.None);

        result.Should().NotBeNull();
    }

    /// <summary>
    /// Loop 80 — ADX hard-gate: <c>Adx14 &gt;= AdxRangeMax (25)</c> ⇒ skip
    /// (trending rejim, mean-reversion ters yön).
    /// </summary>
    [Fact]
    public async Task AdxGate_AboveRangeMax_Skip()
    {
        var snap = MakeSnapshot(adx14: 30m); // > 25
        var (sut, _, _) = Build(snap, TightSpreadTicker());

        var result = await sut.EvaluateAsync(
            StrategyId, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(),
            cancellationToken: CancellationToken.None);

        result.Should().BeNull();
    }

    /// <summary>
    /// Loop 80 — ADX hard-gate warmup bypass: <c>Adx14 == 0</c> (warmup
    /// yetersiz, &lt; 29 bar) ⇒ gate açık. Emit kalır (defansif, frekans
    /// bozulmaz).
    /// </summary>
    [Fact]
    public async Task AdxGate_WarmupBypass_AdxZero_Emits()
    {
        var snap = MakeSnapshot(adx14: 0m);
        var (sut, _, _) = Build(snap, TightSpreadTicker());

        var result = await sut.EvaluateAsync(
            StrategyId, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(),
            cancellationToken: CancellationToken.None);

        result.Should().NotBeNull();
    }

    /// <summary>
    /// Loop 80 — <c>AdxGateEnabled=false</c> ile gate bypass: trending ADX
    /// olsa bile emit kalır (acil 0-emit sigortası).
    /// </summary>
    [Fact]
    public async Task AdxGate_Disabled_TrendingAdx_Emits()
    {
        var snap = MakeSnapshot(adx14: 50m); // çok trending ama gate kapalı
        var (sut, _, _) = Build(snap, TightSpreadTicker());

        var paramsBypass =
            "{\"RsiPeriod\":14,\"BbPeriod\":20,\"BbStdDev\":2.0,\"AtrPeriod\":14," +
            "\"BbwRangeMin\":0.003,\"BbwRangeMax\":0.010,\"RsiOversoldEntry\":35," +
            "\"BufferPctEntry\":0.0005,\"BufferPctExit\":0.001," +
            "\"MaxHoldMinutes\":20,\"SpreadThresholdPct\":0.005,\"CooldownBarsAfterSignal\":3," +
            "\"TradeCountWindow\":20,\"TradeCountSurgeMultiplier\":1.5," +
            "\"AdxGateEnabled\":false,\"AdxRangeMax\":25}";

        var result = await sut.EvaluateAsync(
            StrategyId, paramsBypass, Symbol,
            closedBars: Array.Empty<Kline>(),
            cancellationToken: CancellationToken.None);

        result.Should().NotBeNull();
    }
}
