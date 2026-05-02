using BinanceBot.Application.Strategies.Patterns;
using BinanceBot.Domain.MarketData;
using BinanceBot.Domain.ValueObjects;
using BinanceBot.Infrastructure.Strategies.Patterns.Detectors;
using FluentAssertions;

namespace BinanceBot.Tests.Infrastructure.Strategies.Patterns;

/// <summary>
/// Loop 81 — 10 skor detector + 2 hard-gate + 1 soft-filter unit kontratı.
/// Her detector için minimum 3 senaryo: full trigger / partial / no trigger.
/// </summary>
public class PatternDetectorsTests
{
    private static Kline MakeKline(int seq, decimal open, decimal high, decimal low, decimal close,
        decimal volume = 100m, int tradeCount = 50)
    {
        var t = DateTimeOffset.UnixEpoch.AddMinutes(seq * 5);
        return Kline.Ingest(
            Symbol.From("BTCUSDT"), KlineInterval.FiveMinutes,
            openTime: t, closeTime: t.AddMinutes(5),
            open: open, high: high, low: low, close: close,
            volume: volume, quoteVolume: close * volume, tradeCount: tradeCount,
            takerBuyBase: 0m, takerBuyQuote: 0m, isClosed: true);
    }

    private static IReadOnlyList<Kline> MakeBarsRising(int count, decimal startClose = 100m)
    {
        var list = new List<Kline>(count);
        for (var i = 0; i < count; i++)
        {
            var c = startClose + i;
            list.Add(MakeKline(i, c, c + 0.5m, c - 0.5m, c));
        }
        return list;
    }

    private static BarSnapshot MakeSnapshot(
        decimal open = 100m, decimal high = 101m, decimal low = 99m, decimal close = 100.5m,
        decimal volume = 200m, int tradeCount = 100,
        decimal prevOpen = 100m, decimal prevHigh = 100.5m, decimal prevLow = 99.5m, decimal prevClose = 100m,
        int prevTradeCount = 50,
        decimal rsi14 = 50m, decimal rsi14Prev = 48m,
        decimal ema9 = 100m, decimal ema9Prev = 99.8m,
        decimal ema21 = 100m, decimal ema21Prev5 = 99.5m,
        decimal ema50 = 99m, decimal ema200 = 98m,
        decimal atr14 = 0.5m,
        decimal bbLower = 99m, decimal bbMiddle = 100m, decimal bbUpper = 101m,
        decimal bbw = 0.02m, decimal bbwMin6 = 0.018m,
        decimal adx14 = 22m,
        decimal avgVolume = 100m, decimal avgTradeCount = 50m,
        decimal donchianHigh = 102m, decimal donchianLow = 98m,
        decimal vwap = 99.8m,
        decimal macdLine = 0.05m, decimal macdLinePrev = 0.04m,
        decimal spreadPct = 0.0005m,
        IReadOnlyList<Kline>? recentBars = null)
    {
        return new BarSnapshot(
            Symbol: "BTCUSDT",
            BarOpenTime: DateTimeOffset.UtcNow.AddMinutes(-5),
            BarCloseTime: DateTimeOffset.UtcNow,
            Open: open, High: high, Low: low, Close: close,
            Volume: volume, TradeCount: tradeCount,
            PrevOpen: prevOpen, PrevHigh: prevHigh, PrevLow: prevLow, PrevClose: prevClose,
            PrevTradeCount: prevTradeCount,
            Rsi14: rsi14, Rsi14Prev: rsi14Prev,
            Ema9: ema9, Ema9Prev: ema9Prev,
            Ema21: ema21, Ema21Prev5: ema21Prev5,
            Ema50: ema50, Ema200: ema200,
            Atr14: atr14,
            BollingerLower: bbLower, BollingerMiddle: bbMiddle, BollingerUpper: bbUpper,
            BollingerBandWidth: bbw, BollingerBandWidthMin6: bbwMin6,
            Adx14: adx14,
            AvgVolume20: avgVolume, AvgTradeCount20: avgTradeCount,
            DonchianHigh20: donchianHigh, DonchianLow20: donchianLow,
            Vwap: vwap,
            MacdLine: macdLine, MacdLinePrev: macdLinePrev,
            SpreadPct: spreadPct,
            RecentBars: recentBars ?? MakeBarsRising(30));
    }

    // ─────────────── EmaSqueezeBreak ───────────────
    [Fact]
    public void EmaSqueezeBreak_FullTrigger_ScoreOne()
    {
        var s = MakeSnapshot(
            close: 101m, ema9: 100.5m, ema21: 100m, bbw: 0.002m,
            volume: 200m, // avgVolume default 100 → ratio 2.0 ≥ 1.3
            avgVolume: 100m);
        var d = new EmaSqueezeBreakDetector();
        d.Evaluate(s).Score.Should().Be(1m);
    }

    [Fact]
    public void EmaSqueezeBreak_NoSqueeze_ScoreZero()
    {
        var s = MakeSnapshot(close: 101m, ema9: 100.5m, ema21: 100m, bbw: 0.01m, volume: 200m);
        new EmaSqueezeBreakDetector().Evaluate(s).Score.Should().Be(0m);
    }

    [Fact]
    public void EmaSqueezeBreak_WeakVolume_ScoreHalf()
    {
        var s = MakeSnapshot(close: 101m, ema9: 100.5m, ema21: 100m, bbw: 0.002m,
            volume: 110m, avgVolume: 100m); // ratio 1.1 → weak
        new EmaSqueezeBreakDetector().Evaluate(s).Score.Should().Be(0.5m);
    }

    // ─────────────── VwapBounce ───────────────
    [Fact]
    public void VwapBounce_FullTrigger_ScoreOne()
    {
        var s = MakeSnapshot(close: 100.5m, prevClose: 99.5m, vwap: 100m,
            rsi14: 55m, ema21: 100m, ema21Prev5: 99m);
        new VwapBounceDetector().Evaluate(s).Score.Should().Be(1m);
    }

    [Fact]
    public void VwapBounce_NoCross_ScoreZero()
    {
        var s = MakeSnapshot(close: 99.5m, prevClose: 100.5m, vwap: 100m, rsi14: 55m);
        new VwapBounceDetector().Evaluate(s).Score.Should().Be(0m);
    }

    [Fact]
    public void VwapBounce_NoSlope_ScoreHalf()
    {
        var s = MakeSnapshot(close: 100.5m, prevClose: 99.5m, vwap: 100m,
            rsi14: 55m, ema21: 100m, ema21Prev5: 100m); // no slope
        new VwapBounceDetector().Evaluate(s).Score.Should().Be(0.5m);
    }

    // ─────────────── InsideBarBreakout ───────────────
    [Fact]
    public void InsideBarBreakout_FullTrigger_ScoreOne()
    {
        var bars = new List<Kline>
        {
            MakeKline(0, 100m, 110m, 90m, 105m), // mother
            MakeKline(1, 102m, 108m, 92m, 100m), // inside (108<110, 92>90) - bu kline yine içinde
        };
        // mother index = ^3, inside = ^2; biz son bar'ı snapshot'a koyacağız
        // RecentBars en az 3 bar olmalı. mother ^3 → son 3 bar arasında index 0
        var bars3 = new List<Kline>
        {
            MakeKline(0, 100m, 110m, 90m, 105m), // mother (bars[^3])
            MakeKline(1, 102m, 108m, 92m, 100m), // inside (bars[^2])
            MakeKline(2, 100m, 109m, 99m, 109m), // current (bars[^1]) — bu sadece doldurma
        };
        var s = MakeSnapshot(close: 109m, volume: 200m, avgVolume: 100m, recentBars: bars3);
        new InsideBarBreakoutDetector().Evaluate(s).Score.Should().Be(1m);
    }

    [Fact]
    public void InsideBarBreakout_NotInsideBar_ScoreZero()
    {
        var bars3 = new List<Kline>
        {
            MakeKline(0, 100m, 105m, 95m, 100m),
            MakeKline(1, 100m, 110m, 90m, 100m), // outside not inside
            MakeKline(2, 100m, 105m, 95m, 100m),
        };
        var s = MakeSnapshot(recentBars: bars3);
        new InsideBarBreakoutDetector().Evaluate(s).Score.Should().Be(0m);
    }

    [Fact]
    public void InsideBarBreakout_WeakVolume_ScoreHalf()
    {
        var bars3 = new List<Kline>
        {
            MakeKline(0, 100m, 110m, 90m, 105m),
            MakeKline(1, 102m, 108m, 92m, 100m),
            MakeKline(2, 100m, 109m, 99m, 109m),
        };
        var s = MakeSnapshot(close: 109m, volume: 110m, avgVolume: 100m, recentBars: bars3);
        new InsideBarBreakoutDetector().Evaluate(s).Score.Should().Be(0.5m);
    }

    // ─────────────── RsiOversoldRecovery ───────────────
    [Fact]
    public void RsiOversoldRecovery_FullTrigger_ScoreOne()
    {
        var s = MakeSnapshot(rsi14: 38m, rsi14Prev: 32m, close: 101m, prevClose: 100m);
        new RsiOversoldRecoveryDetector().Evaluate(s).Score.Should().Be(1m);
    }

    [Fact]
    public void RsiOversoldRecovery_NotOversold_ScoreZero()
    {
        var s = MakeSnapshot(rsi14: 60m, rsi14Prev: 55m, close: 101m, prevClose: 100m);
        new RsiOversoldRecoveryDetector().Evaluate(s).Score.Should().Be(0m);
    }

    [Fact]
    public void RsiOversoldRecovery_WeakOversold_ScoreHalf()
    {
        var s = MakeSnapshot(rsi14: 41m, rsi14Prev: 38m, close: 101m, prevClose: 100m);
        new RsiOversoldRecoveryDetector().Evaluate(s).Score.Should().Be(0.5m);
    }

    // ─────────────── VolumeSpikeDonchian ───────────────
    [Fact]
    public void VolumeSpikeDonchian_FullTrigger_ScoreOne()
    {
        var s = MakeSnapshot(close: 103m, donchianHigh: 102m, volume: 300m, avgVolume: 100m);
        new VolumeSpikeDonchianDetector().Evaluate(s).Score.Should().Be(1m);
    }

    [Fact]
    public void VolumeSpikeDonchian_NoBreak_ScoreZero()
    {
        var s = MakeSnapshot(close: 101m, donchianHigh: 102m, volume: 300m);
        new VolumeSpikeDonchianDetector().Evaluate(s).Score.Should().Be(0m);
    }

    [Fact]
    public void VolumeSpikeDonchian_WeakVolume_ScoreHalf()
    {
        var s = MakeSnapshot(close: 103m, donchianHigh: 102m, volume: 180m, avgVolume: 100m);
        new VolumeSpikeDonchianDetector().Evaluate(s).Score.Should().Be(0.5m);
    }

    // ─────────────── HigherLowEmaTouch ───────────────
    [Fact]
    public void HigherLowEmaTouch_FullTrigger_ScoreOne()
    {
        // RecentBars son 4 bar lows artıyor (index ^4 < ^3 < ^1)
        var bars = new List<Kline>
        {
            MakeKline(0, 100m, 101m, 95m, 100m), // ^4 low=95
            MakeKline(1, 100m, 101m, 96m, 100m), // ^3 low=96
            MakeKline(2, 100m, 101m, 95.5m, 100m), // ^2 low=95.5
            MakeKline(3, 100m, 101m, 97m, 100m), // ^1 low=97
        };
        var s = MakeSnapshot(close: 100m, ema21: 100m, ema50: 99m, recentBars: bars);
        new HigherLowEmaTouchDetector().Evaluate(s).Score.Should().Be(1m);
    }

    [Fact]
    public void HigherLowEmaTouch_NotUptrend_ScoreZero()
    {
        var s = MakeSnapshot(ema21: 99m, ema50: 100m); // downtrend
        new HigherLowEmaTouchDetector().Evaluate(s).Score.Should().Be(0m);
    }

    [Fact]
    public void HigherLowEmaTouch_NoTouch_ScoreHalf()
    {
        var bars = new List<Kline>
        {
            MakeKline(0, 100m, 101m, 95m, 100m),
            MakeKline(1, 100m, 101m, 96m, 100m),
            MakeKline(2, 100m, 101m, 95.5m, 100m),
            MakeKline(3, 100m, 101m, 97m, 100m),
        };
        var s = MakeSnapshot(close: 110m, ema21: 100m, ema50: 99m, recentBars: bars); // uzak
        new HigherLowEmaTouchDetector().Evaluate(s).Score.Should().Be(0.5m);
    }

    // ─────────────── MacdZeroCross ───────────────
    [Fact]
    public void MacdZeroCross_StrongCross_ScoreOne()
    {
        var s = MakeSnapshot(close: 100m, macdLine: 0.01m, macdLinePrev: -0.01m); // 0.01 > 0.00005×100 = 0.005
        new MacdZeroCrossDetector().Evaluate(s).Score.Should().Be(1m);
    }

    [Fact]
    public void MacdZeroCross_NoCross_ScoreZero()
    {
        var s = MakeSnapshot(macdLine: 0.05m, macdLinePrev: 0.04m); // both positive, no cross
        new MacdZeroCrossDetector().Evaluate(s).Score.Should().Be(0m);
    }

    [Fact]
    public void MacdZeroCross_WeakCross_ScoreHalf()
    {
        var s = MakeSnapshot(close: 100m, macdLine: 0.001m, macdLinePrev: -0.01m); // 0.001 < 0.005
        new MacdZeroCrossDetector().Evaluate(s).Score.Should().Be(0.5m);
    }

    // ─────────────── BullishEngulfing ───────────────
    [Fact]
    public void BullishEngulfing_FullEngulf_ScoreOne()
    {
        // prev bear: open 102, close 100 (body=2)
        // curr bull: open 99, close 103 (body=4 > 2)
        var s = MakeSnapshot(open: 99m, close: 103m, prevOpen: 102m, prevClose: 100m);
        new BullishEngulfingDetector().Evaluate(s).Score.Should().Be(1m);
    }

    [Fact]
    public void BullishEngulfing_NoEngulf_ScoreZero()
    {
        var s = MakeSnapshot(open: 100m, close: 99m, prevOpen: 100m, prevClose: 100.5m); // both bear-ish
        new BullishEngulfingDetector().Evaluate(s).Score.Should().Be(0m);
    }

    [Fact]
    public void BullishEngulfing_WeakBody_ScoreHalf()
    {
        // prev bear body=2, curr bull body=1 (engulf yes but weak)
        var s = MakeSnapshot(open: 99.9m, close: 102.1m, prevOpen: 102m, prevClose: 100m);
        // curr body = 2.2 > prev body 2 → full
        // weak için: curr body < prev body
        var s2 = MakeSnapshot(open: 100.5m, close: 102m, prevOpen: 102m, prevClose: 100m);
        // open 100.5 ≤ prev close 100? YANLIŞ — fix: open <= prevClose şart, biz open=99 koyalım
        var s3 = MakeSnapshot(open: 99.5m, close: 102.5m, prevOpen: 103m, prevClose: 100m); // prev body=3, curr body=3 → tam
        // Daha basit: prev body=3, curr body=2 → weak
        var sWeak = MakeSnapshot(open: 99.5m, close: 101.5m, prevOpen: 103m, prevClose: 100m);
        // engulf check: open 99.5 <= prev close 100 ✓, close 101.5 >= prev open 103 ✗ → no engulf
        // Yarı engulf path: close >= prevOpen → değil ise
        // weak body için: full engulf + body küçük lazım. Open <= prevClose AND Close >= prevOpen, AND currBody < prevBody
        var sw = MakeSnapshot(open: 99m, close: 103.5m, prevOpen: 103m, prevClose: 100m);
        // prev body=3, curr body=4.5 → tam değil, weak için body daha küçük
        var sWeakReal = MakeSnapshot(open: 99m, close: 103.1m, prevOpen: 103m, prevClose: 100m);
        // prev body=3, curr body=4.1 → still tam, halt
        // Doğru weak: open <= prevClose AND close >= prevOpen AND curr < prev (zor — open düşük olunca body büyür)
        // Öyleyse weak path: close >= prevOpen ama open > prevClose (yarı engulf)
        var sHalf = MakeSnapshot(open: 100.5m, close: 103m, prevOpen: 103m, prevClose: 100m);
        // open 100.5 > prev close 100 → fullEngulf yok
        // Then "Sadece close prev open üstünde (yarı engulf)" → close >= prevOpen ✓ → score 0.5
        new BullishEngulfingDetector().Evaluate(sHalf).Score.Should().Be(0.5m);
    }

    // ─────────────── HammerReversal ───────────────
    [Fact]
    public void HammerReversal_FullTrigger_ScoreOne()
    {
        // body = close-open = 1, lowerWick = open-low = 3 (3:1 = 3 ≥ 2.5)
        var s = MakeSnapshot(open: 100m, close: 101m, low: 97m, high: 101.2m);
        new HammerReversalDetector().Evaluate(s).Score.Should().Be(1m);
    }

    [Fact]
    public void HammerReversal_NotBullBar_ScoreZero()
    {
        var s = MakeSnapshot(open: 101m, close: 100m); // bear bar
        new HammerReversalDetector().Evaluate(s).Score.Should().Be(0m);
    }

    [Fact]
    public void HammerReversal_WeakRatio_ScoreHalf()
    {
        // body=1, lowerWick=2.2 (2:1 < 2.5)
        var s = MakeSnapshot(open: 100m, close: 101m, low: 97.8m, high: 101.2m);
        new HammerReversalDetector().Evaluate(s).Score.Should().Be(0.5m);
    }

    // ─────────────── BollingerLowerReversal ───────────────
    [Fact]
    public void BollingerLowerReversal_FullTrigger_ScoreOne()
    {
        var s = MakeSnapshot(close: 100.5m, prevClose: 99.5m, prevLow: 99m,
            bbLower: 99m, rsi14: 35m, rsi14Prev: 30m);
        new BollingerLowerReversalDetector().Evaluate(s).Score.Should().Be(1m);
    }

    [Fact]
    public void BollingerLowerReversal_NoTouch_ScoreZero()
    {
        var s = MakeSnapshot(close: 100.5m, prevLow: 100m, bbLower: 99m); // touch yok
        new BollingerLowerReversalDetector().Evaluate(s).Score.Should().Be(0m);
    }

    [Fact]
    public void BollingerLowerReversal_NoRsiRising_ScoreHalf()
    {
        var s = MakeSnapshot(close: 100.5m, prevClose: 99.5m, prevLow: 99m,
            bbLower: 99m, rsi14: 30m, rsi14Prev: 35m); // RSI azalıyor
        new BollingerLowerReversalDetector().Evaluate(s).Score.Should().Be(0.5m);
    }

    // ─────────────── VolumeSurgeGate (hard) ───────────────
    [Fact]
    public void VolumeSurgeGate_VolumeAboveAvg_ScoreOne()
    {
        var s = MakeSnapshot(volume: 110m, avgVolume: 100m);
        new VolumeSurgeGate().Evaluate(s).Score.Should().Be(1m);
    }

    [Fact]
    public void VolumeSurgeGate_VolumeBelowAvg_ScoreZero()
    {
        var s = MakeSnapshot(volume: 50m, avgVolume: 100m);
        new VolumeSurgeGate().Evaluate(s).Score.Should().Be(0m);
    }

    [Fact]
    public void VolumeSurgeGate_WarmupBypass_ScoreOne()
    {
        var s = MakeSnapshot(volume: 50m, avgVolume: 0m);
        new VolumeSurgeGate().Evaluate(s).Score.Should().Be(1m);
    }

    // ─────────────── SpreadGuardGate (hard) ───────────────
    [Fact]
    public void SpreadGuardGate_LowSpread_ScoreOne()
    {
        var s = MakeSnapshot(spreadPct: 0.0005m);
        new SpreadGuardGate().Evaluate(s).Score.Should().Be(1m);
    }

    [Fact]
    public void SpreadGuardGate_HighSpread_ScoreZero()
    {
        var s = MakeSnapshot(spreadPct: 0.005m);
        new SpreadGuardGate().Evaluate(s).Score.Should().Be(0m);
    }

    [Fact]
    public void SpreadGuardGate_FallbackBookTickerMissing_ScoreZero()
    {
        var s = MakeSnapshot(spreadPct: 1m); // missing-fallback
        new SpreadGuardGate().Evaluate(s).Score.Should().Be(0m);
    }

    // ─────────────── AdxRegimeFilter (soft) ───────────────
    [Fact]
    public void AdxRegimeFilter_InRegime_ScoreOne()
    {
        var s = MakeSnapshot(adx14: 25m);
        new AdxRegimeFilter().Evaluate(s).Score.Should().Be(1m);
    }

    [Fact]
    public void AdxRegimeFilter_OutOfRegime_ScoreZero()
    {
        var s = MakeSnapshot(adx14: 10m);
        new AdxRegimeFilter().Evaluate(s).Score.Should().Be(0m);
    }

    [Fact]
    public void AdxRegimeFilter_WarmupBypass_ScoreOne()
    {
        var s = MakeSnapshot(adx14: 0m);
        new AdxRegimeFilter().Evaluate(s).Score.Should().Be(1m);
    }
}
