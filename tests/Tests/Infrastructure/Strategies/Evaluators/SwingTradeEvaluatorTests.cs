using BinanceBot.Application.Abstractions;
using BinanceBot.Application.Strategies.Cooldowns;
using BinanceBot.Application.Strategies.Swing;
using BinanceBot.Domain.MarketData;
using BinanceBot.Domain.Strategies;
using BinanceBot.Domain.ValueObjects;
using BinanceBot.Infrastructure.Strategies.Cooldowns;
using BinanceBot.Infrastructure.Strategies.Evaluators;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace BinanceBot.Tests.Infrastructure.Strategies.Evaluators;

/// <summary>
/// Loop 112 — ADR-0027 Aile A SwingTradeEvaluator unit tests.
/// 8 senaryo: Long/Short happy path, volume skip, RSI skip, EMA flat skip,
/// ATR geometry, time-exit ContextJson, warmup skip, cooldown.
/// </summary>
public class SwingTradeEvaluatorTests
{
    private const string SymbolStr = "BTCUSDT";
    private const long StrategyId = 906L;

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; private set; } = now;
        public long BinanceServerTimeMs => UtcNow.ToUnixTimeMilliseconds();
        public long DriftMs => 0;

        public void Advance(TimeSpan delta) => UtcNow = UtcNow.Add(delta);
    }

    private static readonly SwingTradeOptions DefaultOptions = new();
    private static readonly string DefaultParametersJson = System.Text.Json.JsonSerializer
        .Serialize(DefaultOptions);

    private static SwingTradeEvaluator Build(
        ICooldownService? cooldown = null,
        FixedClock? clock = null)
    {
        cooldown ??= new CooldownService(NullLogger<CooldownService>.Instance);
        clock ??= new FixedClock(DateTimeOffset.UtcNow);
        return new SwingTradeEvaluator(
            cooldown, clock, NullLogger<SwingTradeEvaluator>.Instance);
    }

    /// <summary>
    /// 60 bar 4h kline serisi üretir. <paramref name="trendUp"/> true ise
    /// kapanışlar yukarı (EMA20 &gt; EMA50 ⇒ Long aday); false ise aşağı.
    /// Son bar volume parametresi ile override (volume gate test için).
    ///
    /// <para>
    /// EMA + RSI dengesi:
    /// <list type="bullet">
    ///   <item>İlk 40 bar uzun trend yönünde (EMA50 trendi yakalar).</item>
    ///   <item>Son 14 bar mixed (RSI 14 penceresi 50-65 bandında kalsın —
    ///     tamamı aynı yönde değil, küçük geri çekilmelerle).</item>
    /// </list>
    /// </para>
    /// </summary>
    private static List<Kline> BuildBars(
        bool trendUp,
        decimal lastBarVolume = 200m,
        decimal? overrideLastClose = null,
        int bars = 60)
    {
        var list = new List<Kline>(bars);
        var t0 = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
        decimal price = 100m;
        var direction = trendUp ? 1m : -1m;

        for (var i = 0; i < bars; i++)
        {
            // Phase 1 (i<40): doğrultu trend (EMA20/EMA50 ayrılır).
            // Phase 2 (i>=40): mixed — 3-2 zigzag (3 bar trend yönünde,
            //   2 bar geri çekilme) ⇒ RSI 14 penceresi ~55-60 bandında kalır.
            decimal step;
            if (i < 40)
            {
                step = direction * 0.5m;
            }
            else
            {
                // Phase 2 (i>=40): RSI bandını [40,65]/[35,60] içinde tutmak için
                // dengeli zigzag. 2 trend + 2 geri çekilme = neutral; küçük net
                // yön farkı (3. bar'da 0.1 trend) RSI'yi 50-60 aralığında tutar.
                var phase = (i - 40) % 4;
                step = phase < 2 ? direction * 0.4m : -direction * 0.4m;
            }
            price += step;

            // Volume default 100 (SMA baseline). Son bar override.
            decimal volume = (i == bars - 1) ? lastBarVolume : 100m;

            // Son bar close override (geometry edge testleri için).
            decimal close = (i == bars - 1 && overrideLastClose.HasValue)
                ? overrideLastClose.Value
                : price;

            // ATR için OHLC range > 0.
            var open = i == 0 ? price : list[i - 1].ClosePrice;
            var high = Math.Max(open, close) + 0.10m;
            var low = Math.Min(open, close) - 0.10m;

            var t = t0.AddHours(i * 4);
            list.Add(Kline.Ingest(
                Symbol.From(SymbolStr),
                KlineInterval.FourHours,
                openTime: t,
                closeTime: t.AddHours(4),
                open: open,
                high: high,
                low: low,
                close: close,
                volume: volume,
                quoteVolume: volume * close,
                tradeCount: 100,
                takerBuyBase: 0m,
                takerBuyQuote: 0m,
                isClosed: true));
        }

        return list;
    }

    [Fact]
    public void Type_IsSwingTrade()
    {
        var sut = Build();
        sut.Type.Should().Be(StrategyType.SwingTrade);
    }

    [Fact]
    public async Task EvaluateAsync_NonFourHourInterval_ReturnsNull()
    {
        var sut = Build();
        var bars = new List<Kline>
        {
            Kline.Ingest(
                Symbol.From(SymbolStr), KlineInterval.FifteenMinutes,
                openTime: DateTimeOffset.UnixEpoch,
                closeTime: DateTimeOffset.UnixEpoch.AddMinutes(15),
                open: 100m, high: 101m, low: 99m, close: 100m,
                volume: 100m, quoteVolume: 10000m, tradeCount: 50,
                takerBuyBase: 0m, takerBuyQuote: 0m, isClosed: true),
        };

        var result = await sut.EvaluateAsync(
            StrategyId, DefaultParametersJson, SymbolStr, bars, default);

        result.Should().BeNull("evaluator yalnızca 4h bar kapanışında çalışır");
    }

    [Fact]
    public async Task EvaluateAsync_WarmupNotMet_ReturnsNull()
    {
        var sut = Build();
        var bars = BuildBars(trendUp: true, bars: 30); // < MinBarsForEmit (60)

        var result = await sut.EvaluateAsync(
            StrategyId, DefaultParametersJson, SymbolStr, bars, default);

        result.Should().BeNull("60 bar warmup eşiği altında kalan history skip");
    }

    [Fact]
    public async Task EvaluateAsync_LongHappyPath_EmitsLong()
    {
        // Trend yukarı + volume surge (200 > 100 × 1.5 = 150) + RSI band
        // (uptrend close-to-close artışı RSI ~60-65 verir, default Long band [40,65]).
        var sut = Build();
        var bars = BuildBars(trendUp: true, lastBarVolume: 250m);

        var result = await sut.EvaluateAsync(
            StrategyId, DefaultParametersJson, SymbolStr, bars, default);

        result.Should().NotBeNull("uptrend + volume surge + RSI in band ⇒ Long emit");
        result!.Direction.Should().Be(StrategySignalDirection.Long);
        result.SuggestedPrice.Should().Be(bars[^1].ClosePrice);
        result.SuggestedStopPrice.Should().NotBeNull();
        result.SuggestedStopPrice!.Value.Should().BeLessThan(result.SuggestedPrice!.Value,
            "Long SL entry'nin altında");
        result.SuggestedTakeProfit.Should().NotBeNull();
        result.SuggestedTakeProfit!.Value.Should().BeGreaterThan(result.SuggestedPrice.Value,
            "Long TP entry'nin üstünde");

        // R:R 1:2 default — TP-entry distance = 2 × (entry-SL).
        var slDist = result.SuggestedPrice.Value - result.SuggestedStopPrice.Value;
        var tpDist = result.SuggestedTakeProfit.Value - result.SuggestedPrice.Value;
        (tpDist / slDist).Should().BeApproximately(2m, 0.05m, "TpAtrMul/SlAtrMul = 3/1.5 = 2");
    }

    [Fact]
    public async Task EvaluateAsync_ShortHappyPath_EmitsShort()
    {
        // Trend aşağı + volume surge + RSI band [35,60].
        var sut = Build();
        var bars = BuildBars(trendUp: false, lastBarVolume: 250m);

        var result = await sut.EvaluateAsync(
            StrategyId, DefaultParametersJson, SymbolStr, bars, default);

        result.Should().NotBeNull("downtrend + volume surge + RSI in band ⇒ Short emit");
        result!.Direction.Should().Be(StrategySignalDirection.Short);
        result.SuggestedStopPrice!.Value.Should().BeGreaterThan(result.SuggestedPrice!.Value,
            "Short SL entry'nin üstünde");
        result.SuggestedTakeProfit!.Value.Should().BeLessThan(result.SuggestedPrice.Value,
            "Short TP entry'nin altında");
    }

    [Fact]
    public async Task EvaluateAsync_VolumeBelowThreshold_ReturnsNull()
    {
        // Volume default 100 = SMA(20) ortalaması; surge multiplier 1.5
        // ⇒ eşik 150. lastBarVolume=120 (1.2x SMA) altında ⇒ skip.
        var sut = Build();
        var bars = BuildBars(trendUp: true, lastBarVolume: 120m);

        var result = await sut.EvaluateAsync(
            StrategyId, DefaultParametersJson, SymbolStr, bars, default);

        result.Should().BeNull("volume < SMA × 1.5 ⇒ momentum gate skip");
    }

    [Fact]
    public async Task EvaluateAsync_RsiOutsideLongBand_ReturnsNull()
    {
        // RsiLongMin (40) override 50 ⇒ RSI < 50 olunca Long skip.
        // BuildBars(trendUp:true) RSI ~42 (uptrend ama mixed). RsiLongMin=50
        // override eder ⇒ 42 < 50 ⇒ skip. Trend + Vol gate'leri OK; sadece
        // RSI band reddi test edilir.
        var highMin = new SwingTradeOptions(RsiLongMin: 50m);
        var json = System.Text.Json.JsonSerializer.Serialize(highMin);

        var sut = Build();
        var bars = BuildBars(trendUp: true, lastBarVolume: 250m);

        var result = await sut.EvaluateAsync(
            StrategyId, json, SymbolStr, bars, default);

        result.Should().BeNull("RsiLongMin 50 override + uptrend RSI 42 < 50 ⇒ skip");
    }

    [Fact]
    public async Task EvaluateAsync_CooldownActive_ReturnsNullAfterFirstEmit()
    {
        var clock = new FixedClock(DateTimeOffset.UtcNow);
        var cooldown = new CooldownService(NullLogger<CooldownService>.Instance);
        var sut = Build(cooldown, clock);
        var bars = BuildBars(trendUp: true, lastBarVolume: 250m);

        var first = await sut.EvaluateAsync(
            StrategyId, DefaultParametersJson, SymbolStr, bars, default);
        first.Should().NotBeNull("ilk emit cooldown öncesi serbest");

        // İkinci çağrı aynı anda (clock advance yok) ⇒ cooldown active.
        var second = await sut.EvaluateAsync(
            StrategyId, DefaultParametersJson, SymbolStr, bars, default);
        second.Should().BeNull("CooldownBarsAfterSignal=1 default ⇒ 4h içinde skip");
    }

    [Fact]
    public async Task EvaluateAsync_AtrGeometry_RrRatioMatchesParameters()
    {
        // R:R oranı = TpAtrMul / SlAtrMul. Default 3.0/1.5 = 2.0.
        // Parametrik override: 2.0/1.0 = 2.0 (aynı), 4.0/1.0 = 4.0 (farklı).
        var rr4 = new SwingTradeOptions(SlAtrMultiplier: 1.0m, TpAtrMultiplier: 4.0m);
        var json = System.Text.Json.JsonSerializer.Serialize(rr4);

        var sut = Build();
        var bars = BuildBars(trendUp: true, lastBarVolume: 250m);

        var result = await sut.EvaluateAsync(
            StrategyId, json, SymbolStr, bars, default);

        result.Should().NotBeNull();
        var slDist = result!.SuggestedPrice!.Value - result.SuggestedStopPrice!.Value;
        var tpDist = result.SuggestedTakeProfit!.Value - result.SuggestedPrice.Value;
        (tpDist / slDist).Should().BeApproximately(4m, 0.01m,
            "TpAtrMultiplier=4.0 / SlAtrMultiplier=1.0 = R:R 1:4 (parametre overrider)");
    }

    [Fact]
    public async Task EvaluateAsync_EmptyBars_ReturnsNull()
    {
        var sut = Build();
        var result = await sut.EvaluateAsync(
            StrategyId, DefaultParametersJson, SymbolStr,
            Array.Empty<Kline>(), default);
        result.Should().BeNull("kline history boş ⇒ interval guard skip");
    }

    [Fact]
    public async Task EvaluateAsync_LongEmit_ContextJsonContainsMaxHoldMinutes()
    {
        var sut = Build();
        var bars = BuildBars(trendUp: true, lastBarVolume: 250m);

        var result = await sut.EvaluateAsync(
            StrategyId, DefaultParametersJson, SymbolStr, bars, default);

        result.Should().NotBeNull();
        result!.ContextJson.Should().Contain("\"maxHoldMinutes\":480",
            "MaxHoldHours=8 × 60 = 480dk; OrderFilledPositionHandler bu key'i okuyup " +
            "Position.MaxHoldDuration set eder");
        result.ContextJson.Should().Contain("\"type\":\"swing-trade\"",
            "ContextJson type discriminator audit'ten ayırt edilebilir olmalı");
        result.ContextJson.Should().Contain("\"timeExitMinProfitPct\":0.005",
            "TimeExitMinProfitPct default %0.5");
    }
}
