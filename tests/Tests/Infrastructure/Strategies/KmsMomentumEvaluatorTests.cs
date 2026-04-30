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
/// Loop 67 KMS — <see cref="KmsMomentumEvaluator"/> giriş AND filtreleri ve
/// ATR-tabanlı çıkış geometrisi sözleşmesi:
///   1. Snapshot null (warmup eksik proxy) ⇒ skip.
///   2. Tüm 5 koşul + spread + ATR sağlandığında long sinyal emit.
///   3. RSI cross koşulu sağlanmazsa (zaten threshold üstünde) skip.
///   4. Spread çok geniş ⇒ skip.
///   5. Sinyal sonrası cooldown bitmeden tekrar çağrı ⇒ skip.
/// </summary>
public class KmsMomentumEvaluatorTests
{
    private const string Symbol = "BTCUSDT";
    private const long StrategyId = 1L;

    // binance-expert spec defaults — RSI threshold 32, TradeCount × 1.1
    // (testnet), Spread 0.0015, ATR 1.8/0.75 clip, Cooldown 3 bar (15dk).
    private const string DefaultParams =
        "{\"RsiPeriod\":14,\"RsiRecoveryThreshold\":32.0,\"EmaPeriod\":9," +
        "\"TradeCountWindow\":20,\"TradeCountMultiplier\":1.1,\"AtrPeriod\":14," +
        "\"TpAtrMultiplier\":1.8,\"SlAtrMultiplier\":0.75," +
        "\"MinTpPct\":0.005,\"MaxTpPct\":0.018," +
        "\"MinSlPct\":0.003,\"MaxSlPct\":0.008," +
        "\"MaxHoldMinutes\":45,\"MinAtrPct\":0.0005," +
        "\"SpreadThresholdPct\":0.0015,\"CooldownBarsAfterSignal\":3}";

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
    /// Happy path snapshot — tüm AND koşulları sağlanır:
    ///   - Rsi14 = 33 > 32 (recovery)
    ///   - Rsi14Prev = 31 < 32 (oversold önceki bar)
    ///   - Ema9Now = 100.20 > Ema9Prev = 100.10 (slope+)
    ///   - CurrentTradeCount = 160, AvgTradeCount20 = 100 → 160 > 100 × 1.1 = 110
    ///   - Atr14 = 0.50 ; CurrentClose = 100 → atrPct = 0.005 ≥ MinAtrPct 0.0005
    ///
    /// Spread (BookTicker mock): Bid 99.99 / Ask 100.00 → spread 0.0001 < 0.0015.
    /// </summary>
    private static KmsMomentumSnapshot HappySnapshot(
        decimal currentClose = 100m,
        decimal rsi14 = 33m,
        decimal rsi14Prev = 31m,
        decimal ema9Now = 100.20m,
        decimal ema9Prev = 100.10m,
        decimal atr14 = 0.50m,
        decimal avgTradeCount20 = 100m,
        int currentTradeCount = 160)
    {
        return new KmsMomentumSnapshot(
            CurrentClose: currentClose,
            Rsi14: rsi14,
            Rsi14Prev: rsi14Prev,
            Ema9Now: ema9Now,
            Ema9Prev: ema9Prev,
            Atr14: atr14,
            AvgTradeCount20: avgTradeCount20,
            CurrentTradeCount: currentTradeCount,
            LastBarOpenTime: DateTimeOffset.UtcNow.AddMinutes(-5),
            AsOf: DateTimeOffset.UtcNow);
    }

    private static (KmsMomentumEvaluator Sut, FakeBookTickerReader Bt, ICooldownService Cd)
        Build(KmsMomentumSnapshot? snapshot, WsBookTickerPayload? booktick = null,
              ICooldownService? cooldown = null)
    {
        var indicators = new Mock<IMarketIndicatorService>();
        indicators.Setup(i => i.TryGetKmsMomentumSnapshot(
                Symbol,
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<int>()))
            .Returns(snapshot);

        var bt = new FakeBookTickerReader { Payload = booktick };
        var cd = cooldown ?? new CooldownService(NullLogger<CooldownService>.Instance);
        var clock = new FixedClock(DateTimeOffset.UtcNow);

        var sut = new KmsMomentumEvaluator(
            indicators.Object,
            bt,
            cd,
            clock,
            NullLogger<KmsMomentumEvaluator>.Instance);

        return (sut, bt, cd);
    }

    private static WsBookTickerPayload TightSpreadTicker(decimal bid = 99.99m, decimal ask = 100.00m) =>
        new(Symbol: Symbol, UpdateId: 1, BidPrice: bid, BidQty: 1m,
            AskPrice: ask, AskQty: 1m, ReceivedAt: DateTimeOffset.UtcNow);

    [Fact]
    public void Type_IsKlineMomentumSpread5m()
    {
        var (sut, _, _) = Build(snapshot: null);
        sut.Type.Should().Be(StrategyType.KlineMomentumSpread5m);
    }

    /// <summary>
    /// Test 1 — KmsSnapshotReturnsNullWhenWarmupIncomplete: snapshot null
    /// proxy (service tarafı warmup eksik bar < 22 ⇒ null), evaluator skip.
    /// </summary>
    [Fact]
    public async Task KmsSnapshotReturnsNullWhenWarmupIncomplete_Skip()
    {
        var (sut, _, _) = Build(snapshot: null);

        var result = await sut.EvaluateAsync(
            StrategyId, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(),
            cancellationToken: CancellationToken.None);

        result.Should().BeNull();
    }

    /// <summary>
    /// Test 2 — KmsEvaluatorEmitsWhenAllConditionsMet: 5 AND + spread + ATR
    /// hepsi sağlanır → Long sinyal + ATR-dinamik TP/SL + maxHoldMinutes
    /// kontekst.
    ///
    /// Beklenen geometri (entry = 100):
    ///   rawTpPct = 0.50 × 1.8 / 100 = 0.009 → clamp [0.005, 0.018] = 0.009
    ///   rawSlPct = 0.50 × 0.75 / 100 = 0.00375 → clamp [0.003, 0.008] = 0.00375
    ///   TP = 100 × 1.009 = 100.90 ; SL = 100 × 0.99625 = 99.625
    /// </summary>
    [Fact]
    public async Task KmsEvaluatorEmitsWhenAllConditionsMet()
    {
        var snap = HappySnapshot();
        var (sut, _, _) = Build(snap, TightSpreadTicker());

        var result = await sut.EvaluateAsync(
            StrategyId, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(),
            cancellationToken: CancellationToken.None);

        result.Should().NotBeNull();
        result!.Direction.Should().Be(StrategySignalDirection.Long);
        result.SuggestedPrice.Should().Be(100m);
        result.SuggestedTakeProfit.Should().BeApproximately(100.90m, 0.01m);
        result.SuggestedStopPrice.Should().BeApproximately(99.625m, 0.01m);

        result.ContextJson.Should().Contain("kms-momentum-5m");
        result.ContextJson.Should().Contain("maxHoldMinutes");
        result.ContextJson.Should().Contain("spreadPct");
    }

    /// <summary>
    /// Test 3 — KmsEvaluatorSkipsWhenRsiNotCrossing: önceki RSI da threshold
    /// üstünde (33 → 35), recovery cross sağlanmaz, sinyal yok.
    /// </summary>
    [Fact]
    public async Task KmsEvaluatorSkipsWhenRsiNotCrossing()
    {
        var snap = HappySnapshot(rsi14: 35m, rsi14Prev: 33m);
        var (sut, _, _) = Build(snap, TightSpreadTicker());

        var result = await sut.EvaluateAsync(
            StrategyId, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(),
            cancellationToken: CancellationToken.None);

        result.Should().BeNull();
    }

    /// <summary>
    /// Test 4 — KmsEvaluatorSkipsWhenSpreadTooWide: BookTicker payload
    /// spread = (105 − 100) / 105 ≈ 0.0476 > 0.0015 ⇒ skip.
    /// (Snapshot diğer 4 koşul sağlandığı senaryoda spread tek başına bloke
    /// edebiliyor mu test edilir.)
    /// </summary>
    [Fact]
    public async Task KmsEvaluatorSkipsWhenSpreadTooWide()
    {
        var snap = HappySnapshot();
        var wide = new WsBookTickerPayload(
            Symbol: Symbol, UpdateId: 1,
            BidPrice: 100m, BidQty: 1m,
            AskPrice: 105m, AskQty: 1m,
            ReceivedAt: DateTimeOffset.UtcNow);
        var (sut, _, _) = Build(snap, wide);

        var result = await sut.EvaluateAsync(
            StrategyId, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(),
            cancellationToken: CancellationToken.None);

        result.Should().BeNull();
    }

    /// <summary>
    /// Test 5 — KmsEvaluatorSkipsWhenCooldownActive: ilk emit cooldown
    /// kaydı oluşturur, ikinci çağrı aynı clock'ta cooldown 3 bar × 5dk
    /// = 15dk pencere içinde olduğu için skip.
    /// </summary>
    [Fact]
    public async Task KmsEvaluatorSkipsWhenCooldownActive()
    {
        var snap = HappySnapshot();
        var cd = new CooldownService(NullLogger<CooldownService>.Instance);
        var (sut, _, _) = Build(snap, TightSpreadTicker(), cd);

        // İlk çağrı emit edilmeli ve cooldown kaydı bırakmalı.
        var first = await sut.EvaluateAsync(
            StrategyId, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(),
            cancellationToken: CancellationToken.None);
        first.Should().NotBeNull();

        // İkinci çağrı (aynı clock anı) cooldown 15dk pencere açık.
        var second = await sut.EvaluateAsync(
            StrategyId, DefaultParams, Symbol,
            closedBars: Array.Empty<Kline>(),
            cancellationToken: CancellationToken.None);
        second.Should().BeNull();
    }
}
