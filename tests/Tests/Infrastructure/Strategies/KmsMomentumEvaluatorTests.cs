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
/// Loop 71 KMS — <see cref="KmsMomentumEvaluator"/> skor-tabanlı kontrat:
///   1. Skor 4/6 ⇒ emit (low score path: dar TP, geniş SL, MaxHold 30dk).
///   2. Skor 5/6 ⇒ emit (mid score path: default geometri, MaxHold 45dk).
///   3. Skor 6/6 ⇒ emit (high score path: geniş TP, dar SL, MaxHold 60dk).
///   4. Skor 3/6 ⇒ skip.
///   5. Spread 0 puan (hard-gate) ⇒ skip.
///   6. MinAtr 0 puan (hard-gate) ⇒ skip.
///   7. RSI Zone 0 puan (RSI ≥ NeutralCeiling) ⇒ skip.
///   8. CoinClass "large" ⇒ MinAtrPctLarge eşiği uygulanır (BTC).
///   9. CoinClass "alt" ⇒ MinAtrPctAlt eşiği uygulanır (ADA, daha yüksek bar).
///
/// Loop 77 ek kontratlar:
///  10. EMA200 hard-gate: <c>CurrentClose &lt;= Ema200</c> ⇒ skip.
///  11. EMA200 == 0 (warmup yetersiz) ⇒ gate bypass, normal akış.
///  12. <c>Ema200GateEnabled=false</c> ⇒ downtrend bile olsa gate bypass.
///  13. BBW &gt; threshold ⇒ +1 puan, audit'te <c>bbwScore=1</c>.
///  14. BBW &lt;= threshold ⇒ <c>bbwScore=0</c>, emit yine kalır (nice-to-have).
/// </summary>
public class KmsMomentumEvaluatorTests
{
    private const string Symbol = "BTCUSDT";
    private const long StrategyId = 1L;

    // binance-expert spec defaults — Loop 71 skor-tabanlı parametre seti.
    private const string DefaultParamsLarge =
        "{\"RsiPeriod\":14,\"EmaPeriod\":9,\"AtrPeriod\":14,\"TradeCountWindow\":20," +
        "\"RsiOversoldZone\":40,\"RsiNeutralCeiling\":52,\"MinScoreThreshold\":4," +
        "\"CoinClass\":\"large\"," +
        "\"MinAtrPctLarge\":0.0002,\"MinAtrPctMid\":0.0003,\"MinAtrPctAlt\":0.0004," +
        "\"TradeCountSurgeMultiplier\":0.8," +
        "\"TpAtrMultiplier\":1.8,\"SlAtrMultiplier\":0.75," +
        "\"TpAtrMultiplierLow\":1.5,\"TpAtrMultiplierHigh\":2.2," +
        "\"SlAtrMultiplierLow\":0.85,\"SlAtrMultiplierHigh\":0.65," +
        "\"MinTpPct\":0.005,\"MaxTpPct\":0.018," +
        "\"MinSlPct\":0.003,\"MaxSlPct\":0.008," +
        "\"MaxHoldMinutes\":45,\"MaxHoldMinutesLowScore\":30,\"MaxHoldMinutesHighScore\":60," +
        "\"SpreadThresholdPct\":0.005,\"CooldownBarsAfterSignal\":3," +
        "\"Ema200GateEnabled\":true,\"BbwScoreEnabled\":true," +
        "\"BbwThreshold\":0.008,\"BbwScorePoints\":1," +
        "\"AdxGateEnabled\":true,\"AdxTrendingThreshold\":20}";

    private const string DefaultParamsAlt =
        "{\"RsiPeriod\":14,\"EmaPeriod\":9,\"AtrPeriod\":14,\"TradeCountWindow\":20," +
        "\"RsiOversoldZone\":40,\"RsiNeutralCeiling\":52,\"MinScoreThreshold\":4," +
        "\"CoinClass\":\"alt\"," +
        "\"MinAtrPctLarge\":0.0002,\"MinAtrPctMid\":0.0003,\"MinAtrPctAlt\":0.0004," +
        "\"TradeCountSurgeMultiplier\":0.8," +
        "\"TpAtrMultiplier\":1.8,\"SlAtrMultiplier\":0.75," +
        "\"TpAtrMultiplierLow\":1.5,\"TpAtrMultiplierHigh\":2.2," +
        "\"SlAtrMultiplierLow\":0.85,\"SlAtrMultiplierHigh\":0.65," +
        "\"MinTpPct\":0.005,\"MaxTpPct\":0.018," +
        "\"MinSlPct\":0.003,\"MaxSlPct\":0.008," +
        "\"MaxHoldMinutes\":45,\"MaxHoldMinutesLowScore\":30,\"MaxHoldMinutesHighScore\":60," +
        "\"SpreadThresholdPct\":0.005,\"CooldownBarsAfterSignal\":3," +
        "\"Ema200GateEnabled\":true,\"BbwScoreEnabled\":true," +
        "\"BbwThreshold\":0.008,\"BbwScorePoints\":1," +
        "\"AdxGateEnabled\":true,\"AdxTrendingThreshold\":20}";

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
    /// Snapshot factory — default değerler 6/7 skor için optimize:
    ///   - Rsi14 = 35 < OversoldZone(40) AND > Rsi14Prev(33) ⇒ RSI Zone 2 puan
    ///   - Ema9Now(100.20) > Ema9Prev(100.10) ⇒ Slope 1 puan
    ///   - CurrentTradeCount(160) > AvgTradeCount20(100) × 0.8 = 80 ⇒ Surge 1 puan
    ///   - Atr14(0.50) ; CurrentClose(100) ⇒ atrPct 0.005 ≥ MinAtr eşiği (large=0.0002) ⇒ ATR 1 puan
    /// Spread (BookTicker mock) Bid 99.99 / Ask 100 ⇒ spreadPct 0.0001 < 0.005 ⇒ Spread 1 puan
    /// Loop 77 yeni alanlar:
    ///   - Ema200 = 0 default ⇒ EMA200 trend gate bypass (warmup yetersiz semantik).
    ///   - BollingerBandWidth = 0 default ⇒ BBW skoru 0 puan (toplam 6/7).
    /// Test'lerde tek tek parametre düşürülerek skor azaltılır veya gate aktive edilir.
    /// </summary>
    private static KmsMomentumSnapshot MakeSnapshot(
        decimal currentClose = 100m,
        decimal rsi14 = 35m,
        decimal rsi14Prev = 33m,
        decimal ema9Now = 100.20m,
        decimal ema9Prev = 100.10m,
        decimal atr14 = 0.50m,
        decimal avgTradeCount20 = 100m,
        int currentTradeCount = 160,
        decimal ema200 = 0m,
        decimal bollingerBandWidth = 0m,
        // Loop 80 default: Adx14 = 0 ⇒ warmup bypass semantik, ADX gate
        // açık. Mevcut testler ADX gate semantiği eklenmeden önceki davranışı
        // korur. ADX-spesifik testler değeri override eder.
        decimal adx14 = 0m)
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
            Ema200: ema200,
            BollingerBandWidth: bollingerBandWidth,
            Adx14: adx14,
            LastBarOpenTime: DateTimeOffset.UtcNow.AddMinutes(-5),
            AsOf: DateTimeOffset.UtcNow);
    }

    private static (KmsMomentumEvaluator Sut, FakeBookTickerReader Bt, ICooldownService Cd)
        Build(KmsMomentumSnapshot? snapshot, WsBookTickerPayload? booktick = null,
              ICooldownService? cooldown = null)
    {
        var indicators = new Mock<IMarketIndicatorService>();
        indicators.Setup(i => i.TryGetKmsMomentumSnapshot(
                It.IsAny<string>(),
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

    private static WsBookTickerPayload TightSpreadTicker(
        string symbol = Symbol, decimal bid = 99.99m, decimal ask = 100.00m) =>
        new(Symbol: symbol, UpdateId: 1, BidPrice: bid, BidQty: 1m,
            AskPrice: ask, AskQty: 1m, ReceivedAt: DateTimeOffset.UtcNow);

    [Fact]
    public void Type_IsKlineMomentumSpread5m()
    {
        var (sut, _, _) = Build(snapshot: null);
        sut.Type.Should().Be(StrategyType.KlineMomentumSpread5m);
    }

    /// <summary>
    /// Test 1 — Score 4/6 emit (low score path):
    /// RSI Zone 1 (Rsi14=45 < NeutralCeiling 52, > Prev 43, ama OversoldZone 40 üstü)
    /// + Slope 1 + Surge 1 + Spread 1 + ATR 1 = 5? Hayır — 4 lazım.
    /// 4 puan için: RSI Zone 1, Slope 0, Surge 1, Spread 1, ATR 1 = 4.
    /// Slope sıfırlamak için Ema9Now == Ema9Prev (slope 0).
    /// Beklenen geometri: TpMul=1.5 (Low), SlMul=0.85 (Low), MaxHold=30.
    ///   rawTpPct = 0.50 × 1.5 / 100 = 0.0075 → clamp [0.005, 0.018] = 0.0075
    ///   rawSlPct = 0.50 × 0.85 / 100 = 0.00425 → clamp [0.003, 0.008] = 0.00425
    ///   TP = 100 × 1.0075 = 100.75 ; SL = 100 × 0.99575 = 99.575
    /// </summary>
    [Fact]
    public async Task Score4_LowPath_Emit()
    {
        var snap = MakeSnapshot(
            rsi14: 45m, rsi14Prev: 43m,           // RSI Zone 1 puan (neutral, momentum+)
            ema9Now: 100.10m, ema9Prev: 100.10m); // Slope 0 puan (eşit)
        var (sut, _, _) = Build(snap, TightSpreadTicker());

        var result = await sut.EvaluateAsync(
            StrategyId, DefaultParamsLarge, Symbol,
            closedBars: Array.Empty<Kline>(),
            cancellationToken: CancellationToken.None);

        result.Should().NotBeNull();
        result!.Direction.Should().Be(StrategySignalDirection.Long);
        result.SuggestedPrice.Should().Be(100m);
        result.SuggestedTakeProfit.Should().BeApproximately(100.75m, 0.01m);
        result.SuggestedStopPrice.Should().BeApproximately(99.575m, 0.01m);

        result.ContextJson.Should().Contain("\"score\":4");
        result.ContextJson.Should().Contain("\"maxHoldMinutes\":30");
    }

    /// <summary>
    /// Test 2 — Score 5/6 emit (mid score path):
    /// RSI Zone 1 + Slope 1 + Surge 1 + Spread 1 + ATR 1 = 5.
    /// Beklenen geometri: TpMul=1.8 (Mid), SlMul=0.75 (Mid), MaxHold=45.
    ///   rawTpPct = 0.50 × 1.8 / 100 = 0.009  → 0.009
    ///   rawSlPct = 0.50 × 0.75 / 100 = 0.00375 → 0.00375
    ///   TP = 100.90 ; SL = 99.625
    /// </summary>
    [Fact]
    public async Task Score5_MidPath_Emit()
    {
        var snap = MakeSnapshot(
            rsi14: 45m, rsi14Prev: 43m); // RSI Zone 1 puan; geri kalanı default (slope+surge+spread+atr = 4)
        var (sut, _, _) = Build(snap, TightSpreadTicker());

        var result = await sut.EvaluateAsync(
            StrategyId, DefaultParamsLarge, Symbol,
            closedBars: Array.Empty<Kline>(),
            cancellationToken: CancellationToken.None);

        result.Should().NotBeNull();
        result!.SuggestedTakeProfit.Should().BeApproximately(100.90m, 0.01m);
        result.SuggestedStopPrice.Should().BeApproximately(99.625m, 0.01m);

        result.ContextJson.Should().Contain("\"score\":5");
        result.ContextJson.Should().Contain("\"maxHoldMinutes\":45");
    }

    /// <summary>
    /// Test 3 — Score 6/6 emit (high score path):
    /// RSI Zone 2 (Rsi14=35 < OversoldZone 40, > Prev) + Slope 1 + Surge 1 +
    /// Spread 1 + ATR 1 = 6.
    /// Beklenen geometri: TpMul=2.2 (High), SlMul=0.65 (High), MaxHold=60.
    ///   rawTpPct = 0.50 × 2.2 / 100 = 0.011 → 0.011
    ///   rawSlPct = 0.50 × 0.65 / 100 = 0.00325 → 0.00325
    ///   TP = 101.10 ; SL = 99.675
    /// </summary>
    [Fact]
    public async Task Score6_HighPath_Emit()
    {
        var snap = MakeSnapshot(); // default: 6/6
        var (sut, _, _) = Build(snap, TightSpreadTicker());

        var result = await sut.EvaluateAsync(
            StrategyId, DefaultParamsLarge, Symbol,
            closedBars: Array.Empty<Kline>(),
            cancellationToken: CancellationToken.None);

        result.Should().NotBeNull();
        result!.SuggestedTakeProfit.Should().BeApproximately(101.10m, 0.01m);
        result.SuggestedStopPrice.Should().BeApproximately(99.675m, 0.01m);

        result.ContextJson.Should().Contain("\"score\":6");
        result.ContextJson.Should().Contain("\"maxHoldMinutes\":60");
        result.ContextJson.Should().Contain("\"coinClass\":\"large\"");
    }

    /// <summary>
    /// Test 4 — Score 3/6 skip:
    /// RSI Zone 1 + Slope 0 + Surge 0 + Spread 1 + ATR 1 = 3 (4 altı).
    /// Slope 0: Ema9Now == Ema9Prev. Surge 0: tradeCount avg × 0.8 altında.
    /// </summary>
    [Fact]
    public async Task Score3_BelowMin_Skip()
    {
        var snap = MakeSnapshot(
            rsi14: 45m, rsi14Prev: 43m,            // RSI Zone 1
            ema9Now: 100.10m, ema9Prev: 100.10m,   // Slope 0
            currentTradeCount: 70);                // 70 < 100*0.8=80 → Surge 0
        var (sut, _, _) = Build(snap, TightSpreadTicker());

        var result = await sut.EvaluateAsync(
            StrategyId, DefaultParamsLarge, Symbol,
            closedBars: Array.Empty<Kline>(),
            cancellationToken: CancellationToken.None);

        result.Should().BeNull();
    }

    /// <summary>
    /// Test 5 — Spread 0 puan (hard-gate skip):
    /// Spread eşiği üstünde (0.05 > 0.005). Diğer 5 puan dolu olsa da emit yok.
    /// </summary>
    [Fact]
    public async Task SpreadHardGate_Skip()
    {
        var snap = MakeSnapshot(); // 6/6 score koşulu sağlar
        var wide = new WsBookTickerPayload(
            Symbol: Symbol, UpdateId: 1,
            BidPrice: 95m, BidQty: 1m,
            AskPrice: 100m, AskQty: 1m,
            ReceivedAt: DateTimeOffset.UtcNow); // spread = 0.05
        var (sut, _, _) = Build(snap, wide);

        var result = await sut.EvaluateAsync(
            StrategyId, DefaultParamsLarge, Symbol,
            closedBars: Array.Empty<Kline>(),
            cancellationToken: CancellationToken.None);

        result.Should().BeNull();
    }

    /// <summary>
    /// Test 6 — MinAtr 0 puan (hard-gate skip):
    /// Atr14 0 → atrPct 0 < MinAtrPctLarge 0.0002. Diğer 5 puan dolu — yine skip.
    /// </summary>
    [Fact]
    public async Task MinAtrHardGate_Skip()
    {
        var snap = MakeSnapshot(atr14: 0m); // ATR 0 puan
        var (sut, _, _) = Build(snap, TightSpreadTicker());

        var result = await sut.EvaluateAsync(
            StrategyId, DefaultParamsLarge, Symbol,
            closedBars: Array.Empty<Kline>(),
            cancellationToken: CancellationToken.None);

        result.Should().BeNull();
    }

    /// <summary>
    /// Test 7 — RSI Zone 0 puan (hard-gate skip):
    /// Rsi14 = 60 ≥ NeutralCeiling(52) → RSI Zone 0 puan. Diğer 4 (Slope/Surge/Spread/ATR)
    /// dolu = 4 puan, ama RSI ≥1 hard-gate fail.
    /// </summary>
    [Fact]
    public async Task RsiZoneHardGate_Skip()
    {
        var snap = MakeSnapshot(rsi14: 60m, rsi14Prev: 58m); // momentum+ ama zone yok
        var (sut, _, _) = Build(snap, TightSpreadTicker());

        var result = await sut.EvaluateAsync(
            StrategyId, DefaultParamsLarge, Symbol,
            closedBars: Array.Empty<Kline>(),
            cancellationToken: CancellationToken.None);

        result.Should().BeNull();
    }

    /// <summary>
    /// Test 8 — CoinClass "large" MinAtrPctLarge eşiğini kullanır:
    /// BTC senaryo, atrPct = 0.00025 (Atr14=0.025, Close=100). MinAtrPctLarge=0.0002 ≤ 0.00025 ⇒ ATR 1 puan.
    /// Aynı atrPct alt eşiğinde olsaydı (0.0004) skip olurdu. Burada emit beklenir.
    /// </summary>
    [Fact]
    public async Task CoinClassLarge_LowAtr_StillEmits()
    {
        var snap = MakeSnapshot(atr14: 0.025m); // atrPct = 0.025/100 = 0.00025
        var (sut, _, _) = Build(snap, TightSpreadTicker());

        var result = await sut.EvaluateAsync(
            StrategyId, DefaultParamsLarge, Symbol,
            closedBars: Array.Empty<Kline>(),
            cancellationToken: CancellationToken.None);

        result.Should().NotBeNull();
        result!.ContextJson.Should().Contain("\"coinClass\":\"large\"");
        result.ContextJson.Should().Contain("\"atrScore\":1");
    }

    /// <summary>
    /// Test 9 — CoinClass "alt" MinAtrPctAlt eşiğini kullanır:
    /// ADA senaryo, atrPct = 0.00025. MinAtrPctAlt=0.0004 > 0.00025 ⇒ ATR 0 puan ⇒
    /// hard-gate skip. Aynı senaryo (large) emit ederken alt skip eder — asimetri çözümü.
    /// </summary>
    [Fact]
    public async Task CoinClassAlt_LowAtr_HardGateSkip()
    {
        var snap = MakeSnapshot(atr14: 0.025m); // atrPct = 0.00025 < 0.0004
        var (sut, _, _) = Build(snap, TightSpreadTicker());

        var result = await sut.EvaluateAsync(
            StrategyId, DefaultParamsAlt, Symbol,
            closedBars: Array.Empty<Kline>(),
            cancellationToken: CancellationToken.None);

        result.Should().BeNull();
    }

    /// <summary>
    /// Test 10 — Snapshot null (warmup eksik) skip.
    /// </summary>
    [Fact]
    public async Task SnapshotNull_Skip()
    {
        var (sut, _, _) = Build(snapshot: null);

        var result = await sut.EvaluateAsync(
            StrategyId, DefaultParamsLarge, Symbol,
            closedBars: Array.Empty<Kline>(),
            cancellationToken: CancellationToken.None);

        result.Should().BeNull();
    }

    /// <summary>
    /// Test 11 — Cooldown skip: emit sonrası aynı clock'ta ikinci çağrı 15dk
    /// pencere içinde, skip beklenir.
    /// </summary>
    [Fact]
    public async Task Cooldown_AfterEmit_Skip()
    {
        var snap = MakeSnapshot();
        var cd = new CooldownService(NullLogger<CooldownService>.Instance);
        var (sut, _, _) = Build(snap, TightSpreadTicker(), cd);

        var first = await sut.EvaluateAsync(
            StrategyId, DefaultParamsLarge, Symbol,
            closedBars: Array.Empty<Kline>(),
            cancellationToken: CancellationToken.None);
        first.Should().NotBeNull();

        var second = await sut.EvaluateAsync(
            StrategyId, DefaultParamsLarge, Symbol,
            closedBars: Array.Empty<Kline>(),
            cancellationToken: CancellationToken.None);
        second.Should().BeNull();
    }

    /// <summary>
    /// Loop 77 Test 12 — EMA200 trend hard-gate: <c>CurrentClose &lt;= Ema200</c>
    /// (downtrend) ⇒ skip, skor toplama bile bakılmaz. Ema200 = 105 > Close 100,
    /// gate aktif (default true).
    /// </summary>
    [Fact]
    public async Task Ema200Gate_ClosingBelowEma200_Skip()
    {
        var snap = MakeSnapshot(currentClose: 100m, ema200: 105m); // close < ema200 ⇒ downtrend
        var (sut, _, _) = Build(snap, TightSpreadTicker());

        var result = await sut.EvaluateAsync(
            StrategyId, DefaultParamsLarge, Symbol,
            closedBars: Array.Empty<Kline>(),
            cancellationToken: CancellationToken.None);

        result.Should().BeNull();
    }

    /// <summary>
    /// Loop 77 Test 13 — EMA200 hard-gate uptrend bypass: Close 110 > Ema200 100 ⇒
    /// gate açık, normal akış (6/7 emit, BBW 0).
    /// </summary>
    [Fact]
    public async Task Ema200Gate_ClosingAboveEma200_Emit()
    {
        var snap = MakeSnapshot(currentClose: 110m, ema200: 100m, atr14: 0.55m); // 0.55/110≈0.005
        var (sut, _, _) = Build(snap, TightSpreadTicker(bid: 109.99m, ask: 110m));

        var result = await sut.EvaluateAsync(
            StrategyId, DefaultParamsLarge, Symbol,
            closedBars: Array.Empty<Kline>(),
            cancellationToken: CancellationToken.None);

        result.Should().NotBeNull();
        result!.ContextJson.Should().Contain("\"score\":6"); // BBW 0, total 6/7
        result.ContextJson.Should().Contain("\"ema200\":100");
    }

    /// <summary>
    /// Loop 77 Test 14 — Ema200GateEnabled=false ⇒ downtrend bile olsa gate
    /// bypass (0-emit acil sigortası). Close 100 ≤ Ema200 105 ama toggle off
    /// ⇒ emit beklenir.
    /// </summary>
    [Fact]
    public async Task Ema200Gate_Disabled_Bypass_Emit()
    {
        const string ParamsGateOff =
            "{\"RsiPeriod\":14,\"EmaPeriod\":9,\"AtrPeriod\":14,\"TradeCountWindow\":20," +
            "\"RsiOversoldZone\":40,\"RsiNeutralCeiling\":52,\"MinScoreThreshold\":4," +
            "\"CoinClass\":\"large\"," +
            "\"MinAtrPctLarge\":0.0002,\"MinAtrPctMid\":0.0003,\"MinAtrPctAlt\":0.0004," +
            "\"TradeCountSurgeMultiplier\":0.8," +
            "\"TpAtrMultiplier\":1.8,\"SlAtrMultiplier\":0.75," +
            "\"TpAtrMultiplierLow\":1.5,\"TpAtrMultiplierHigh\":2.2," +
            "\"SlAtrMultiplierLow\":0.85,\"SlAtrMultiplierHigh\":0.65," +
            "\"MinTpPct\":0.005,\"MaxTpPct\":0.018," +
            "\"MinSlPct\":0.003,\"MaxSlPct\":0.008," +
            "\"MaxHoldMinutes\":45,\"MaxHoldMinutesLowScore\":30,\"MaxHoldMinutesHighScore\":60," +
            "\"SpreadThresholdPct\":0.005,\"CooldownBarsAfterSignal\":3," +
            "\"Ema200GateEnabled\":false,\"BbwScoreEnabled\":true," +
            "\"BbwThreshold\":0.008,\"BbwScorePoints\":1," +
        "\"AdxGateEnabled\":true,\"AdxTrendingThreshold\":20}";

        var snap = MakeSnapshot(currentClose: 100m, ema200: 105m); // downtrend ama gate off
        var (sut, _, _) = Build(snap, TightSpreadTicker());

        var result = await sut.EvaluateAsync(
            StrategyId, ParamsGateOff, Symbol,
            closedBars: Array.Empty<Kline>(),
            cancellationToken: CancellationToken.None);

        result.Should().NotBeNull();
    }

    /// <summary>
    /// Loop 77 Test 15 — BBW > threshold (0.008) ⇒ +1 puan.
    /// Default snapshot 6/7 (BBW 0). BBW 0.012 > 0.008 ⇒ totalScore 7.
    /// Audit: <c>"bbwScore":1</c> ve <c>"score":7</c>.
    /// </summary>
    [Fact]
    public async Task BbwScore_AboveThreshold_AddsBonusPoint()
    {
        var snap = MakeSnapshot(bollingerBandWidth: 0.012m); // > 0.008
        var (sut, _, _) = Build(snap, TightSpreadTicker());

        var result = await sut.EvaluateAsync(
            StrategyId, DefaultParamsLarge, Symbol,
            closedBars: Array.Empty<Kline>(),
            cancellationToken: CancellationToken.None);

        result.Should().NotBeNull();
        result!.ContextJson.Should().Contain("\"bbwScore\":1");
        result.ContextJson.Should().Contain("\"score\":7");
    }

    /// <summary>
    /// Loop 77 Test 16 — BBW &lt;= threshold ⇒ <c>bbwScore=0</c>, ama emit
    /// kalır (nice-to-have, hard-gate değil). BBW 0.005 &lt; 0.008 ⇒ 0 puan;
    /// totalScore 6 yine MinScoreThreshold 4 üstünde.
    /// </summary>
    [Fact]
    public async Task BbwScore_BelowThreshold_NoBonus_StillEmits()
    {
        var snap = MakeSnapshot(bollingerBandWidth: 0.005m); // < 0.008
        var (sut, _, _) = Build(snap, TightSpreadTicker());

        var result = await sut.EvaluateAsync(
            StrategyId, DefaultParamsLarge, Symbol,
            closedBars: Array.Empty<Kline>(),
            cancellationToken: CancellationToken.None);

        result.Should().NotBeNull();
        result!.ContextJson.Should().Contain("\"bbwScore\":0");
        result.ContextJson.Should().Contain("\"score\":6");
    }

    /// <summary>
    /// Loop 78 Test 17 — BBW hard-gate aktif + BBW &lt; threshold ⇒ skip.
    /// Loop 77 t120 post-mortem: BBW=0/zayıf bant emit'leri 4 ardışık big SL ile
    /// CB'yi tripletti. Hard-gate <c>BbwHardGate=true</c> + bant 0.003 &lt; 0.008
    /// ⇒ EMA200 sonrası skip; skor toplama bakılmaz.
    /// </summary>
    [Fact]
    public async Task BbwHardGate_BelowThreshold_Skip()
    {
        const string ParamsHardGateOn =
            "{\"RsiPeriod\":14,\"EmaPeriod\":9,\"AtrPeriod\":14,\"TradeCountWindow\":20," +
            "\"RsiOversoldZone\":40,\"RsiNeutralCeiling\":52,\"MinScoreThreshold\":4," +
            "\"CoinClass\":\"large\"," +
            "\"MinAtrPctLarge\":0.0002,\"MinAtrPctMid\":0.0003,\"MinAtrPctAlt\":0.0004," +
            "\"TradeCountSurgeMultiplier\":0.8," +
            "\"TpAtrMultiplier\":1.8,\"SlAtrMultiplier\":0.75," +
            "\"TpAtrMultiplierLow\":1.5,\"TpAtrMultiplierHigh\":2.2," +
            "\"SlAtrMultiplierLow\":0.85,\"SlAtrMultiplierHigh\":0.65," +
            "\"MinTpPct\":0.005,\"MaxTpPct\":0.018," +
            "\"MinSlPct\":0.003,\"MaxSlPct\":0.008," +
            "\"MaxHoldMinutes\":45,\"MaxHoldMinutesLowScore\":30,\"MaxHoldMinutesHighScore\":60," +
            "\"SpreadThresholdPct\":0.005,\"CooldownBarsAfterSignal\":3," +
            "\"Ema200GateEnabled\":true,\"BbwScoreEnabled\":true," +
            "\"BbwThreshold\":0.008,\"BbwScorePoints\":1,\"BbwHardGate\":true," +
            "\"AdxGateEnabled\":true,\"AdxTrendingThreshold\":20}";

        var snap = MakeSnapshot(bollingerBandWidth: 0.003m); // 0.003 < 0.008 threshold
        var (sut, _, _) = Build(snap, TightSpreadTicker());

        var result = await sut.EvaluateAsync(
            StrategyId, ParamsHardGateOn, Symbol,
            closedBars: Array.Empty<Kline>(),
            cancellationToken: CancellationToken.None);

        result.Should().BeNull();
    }

    /// <summary>
    /// Loop 78 Test 18 — BBW hard-gate aktif + BBW &gt; threshold ⇒ emit OK
    /// (bypass). Hard-gate sadece zayıf bant'ta tetiklenir; sağlıklı volatilite
    /// rejiminde normal akış. BBW 0.012 &gt; 0.008 ⇒ +1 puan, totalScore 7.
    /// </summary>
    [Fact]
    public async Task BbwHardGate_AboveThreshold_Emit()
    {
        const string ParamsHardGateOn =
            "{\"RsiPeriod\":14,\"EmaPeriod\":9,\"AtrPeriod\":14,\"TradeCountWindow\":20," +
            "\"RsiOversoldZone\":40,\"RsiNeutralCeiling\":52,\"MinScoreThreshold\":4," +
            "\"CoinClass\":\"large\"," +
            "\"MinAtrPctLarge\":0.0002,\"MinAtrPctMid\":0.0003,\"MinAtrPctAlt\":0.0004," +
            "\"TradeCountSurgeMultiplier\":0.8," +
            "\"TpAtrMultiplier\":1.8,\"SlAtrMultiplier\":0.75," +
            "\"TpAtrMultiplierLow\":1.5,\"TpAtrMultiplierHigh\":2.2," +
            "\"SlAtrMultiplierLow\":0.85,\"SlAtrMultiplierHigh\":0.65," +
            "\"MinTpPct\":0.005,\"MaxTpPct\":0.018," +
            "\"MinSlPct\":0.003,\"MaxSlPct\":0.008," +
            "\"MaxHoldMinutes\":45,\"MaxHoldMinutesLowScore\":30,\"MaxHoldMinutesHighScore\":60," +
            "\"SpreadThresholdPct\":0.005,\"CooldownBarsAfterSignal\":3," +
            "\"Ema200GateEnabled\":true,\"BbwScoreEnabled\":true," +
            "\"BbwThreshold\":0.008,\"BbwScorePoints\":1,\"BbwHardGate\":true," +
            "\"AdxGateEnabled\":true,\"AdxTrendingThreshold\":20}";

        var snap = MakeSnapshot(bollingerBandWidth: 0.012m); // 0.012 > 0.008 threshold
        var (sut, _, _) = Build(snap, TightSpreadTicker());

        var result = await sut.EvaluateAsync(
            StrategyId, ParamsHardGateOn, Symbol,
            closedBars: Array.Empty<Kline>(),
            cancellationToken: CancellationToken.None);

        result.Should().NotBeNull();
        result!.ContextJson.Should().Contain("\"bbwScore\":1");
        result.ContextJson.Should().Contain("\"score\":7");
        result.ContextJson.Should().Contain("\"bbwHardGate\":true");
    }

    /// <summary>
    /// Loop 80 — KMS ADX hard-gate: <c>Adx14 &lt; AdxTrendingThreshold (20)</c>
    /// (zayıf trend) ⇒ skip. EMA200 + BBW gate'lerinin hemen sonrası, skor
    /// öncesi sıralanır.
    /// </summary>
    [Fact]
    public async Task KmsAdxGate_BelowThreshold_Skip()
    {
        var snap = MakeSnapshot(adx14: 15m); // < 20
        var (sut, _, _) = Build(snap, TightSpreadTicker());

        var result = await sut.EvaluateAsync(
            StrategyId, DefaultParamsLarge, Symbol,
            closedBars: Array.Empty<Kline>(),
            cancellationToken: CancellationToken.None);

        result.Should().BeNull();
    }

    /// <summary>
    /// Loop 80 — KMS ADX hard-gate warmup bypass: Adx14 == 0 (warmup
    /// yetersiz) ⇒ gate açık, normal akış. Default snapshot zaten Adx14=0.
    /// </summary>
    [Fact]
    public async Task KmsAdxGate_WarmupBypass_AdxZero_Emits()
    {
        var snap = MakeSnapshot(adx14: 0m);
        var (sut, _, _) = Build(snap, TightSpreadTicker());

        var result = await sut.EvaluateAsync(
            StrategyId, DefaultParamsLarge, Symbol,
            closedBars: Array.Empty<Kline>(),
            cancellationToken: CancellationToken.None);

        result.Should().NotBeNull();
    }

    /// <summary>
    /// Loop 80 — KMS ADX hard-gate eşik üstü ⇒ emit (trending). 25 ≥ 20
    /// trend yeterli, normal akış.
    /// </summary>
    [Fact]
    public async Task KmsAdxGate_AboveThreshold_Emits()
    {
        var snap = MakeSnapshot(adx14: 25m);
        var (sut, _, _) = Build(snap, TightSpreadTicker());

        var result = await sut.EvaluateAsync(
            StrategyId, DefaultParamsLarge, Symbol,
            closedBars: Array.Empty<Kline>(),
            cancellationToken: CancellationToken.None);

        result.Should().NotBeNull();
        result!.ContextJson.Should().Contain("\"adx14\":25");
    }
}
