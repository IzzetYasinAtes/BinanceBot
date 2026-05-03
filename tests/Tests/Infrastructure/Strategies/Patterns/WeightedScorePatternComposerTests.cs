using BinanceBot.Application.Strategies.Patterns;
using BinanceBot.Domain.MarketData;
using BinanceBot.Domain.ValueObjects;
using BinanceBot.Infrastructure.Strategies.Patterns;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace BinanceBot.Tests.Infrastructure.Strategies.Patterns;

/// <summary>
/// Loop 81 — WeightedScorePatternComposer kontrat testleri (8 senaryo).
/// </summary>
public class WeightedScorePatternComposerTests
{
    private static BarSnapshot MakeSnap(decimal close = 100m, decimal atr = 0.5m, decimal adx = 20m,
        IReadOnlyList<Kline>? bars = null)
    {
        return new BarSnapshot(
            Symbol: "BTCUSDT",
            BarOpenTime: DateTimeOffset.UtcNow.AddMinutes(-5),
            BarCloseTime: DateTimeOffset.UtcNow,
            Open: close - 0.2m, High: close + 0.5m, Low: close - 0.5m, Close: close,
            Volume: 200m, TradeCount: 100,
            PrevOpen: close - 0.5m, PrevHigh: close + 0.2m, PrevLow: close - 0.7m, PrevClose: close - 0.3m,
            PrevTradeCount: 50,
            Rsi14: 50m, Rsi14Prev: 48m,
            Ema9: close, Ema9Prev: close - 0.1m,
            Ema21: close - 0.1m, Ema21Prev5: close - 0.5m,
            Ema50: close - 1m, Ema200: close - 2m,
            Atr14: atr,
            BollingerLower: close - 1m, BollingerMiddle: close, BollingerUpper: close + 1m,
            BollingerBandWidth: 0.02m, BollingerBandWidthMin6: 0.018m,
            Adx14: adx,
            AvgVolume20: 100m, AvgTradeCount20: 50m,
            DonchianHigh20: close + 2m, DonchianLow20: close - 2m,
            Vwap: close - 0.2m,
            MacdLine: 0.05m, MacdLinePrev: 0.04m,
            SpreadPct: 0.0005m,
            Ema21_15m: close - 0.05m,
            Ema21Prev5_15m: close - 0.4m,
            RecentBars: bars ?? Array.Empty<Kline>());
    }

    private static WeightedScorePatternComposer Sut() =>
        new(NullLogger<WeightedScorePatternComposer>.Instance, new StubRegistry());

    /// <summary>
    /// Loop 92 — Direction routing için minimal registry. Test'in içinde isimle
    /// kullanılan detector'ların yön bias'ı production ile aynı olmalı.
    /// </summary>
    private sealed class StubRegistry : IPatternRegistry
    {
        public IReadOnlyList<IPatternDetector> Detectors { get; } = new IPatternDetector[]
        {
            // Long-bias
            new StubDetector("ema_squeeze_break", BinanceBot.Domain.Common.TradeDirection.Long),
            new StubDetector("vwap_bounce", BinanceBot.Domain.Common.TradeDirection.Long),
            new StubDetector("inside_bar_breakout", BinanceBot.Domain.Common.TradeDirection.Long),
            new StubDetector("rsi_oversold_recovery", BinanceBot.Domain.Common.TradeDirection.Long),
            new StubDetector("volume_spike_donchian", BinanceBot.Domain.Common.TradeDirection.Long),
            new StubDetector("higher_low_ema_touch", BinanceBot.Domain.Common.TradeDirection.Long),
            new StubDetector("macd_zero_cross", BinanceBot.Domain.Common.TradeDirection.Long),
            new StubDetector("bullish_engulfing", BinanceBot.Domain.Common.TradeDirection.Long),
            new StubDetector("hammer_reversal", BinanceBot.Domain.Common.TradeDirection.Long),
            new StubDetector("bollinger_lower_reversal", BinanceBot.Domain.Common.TradeDirection.Long),
            // Short-bias
            new StubDetector("bearish_engulfing", BinanceBot.Domain.Common.TradeDirection.Short),
            new StubDetector("shooting_star", BinanceBot.Domain.Common.TradeDirection.Short),
            new StubDetector("bollinger_upper_reversal", BinanceBot.Domain.Common.TradeDirection.Short),
            new StubDetector("bollinger_squeeze_break_down", BinanceBot.Domain.Common.TradeDirection.Short),
            new StubDetector("rsi_overbought_pullback", BinanceBot.Domain.Common.TradeDirection.Short),
            new StubDetector("ema9_slope_down", BinanceBot.Domain.Common.TradeDirection.Short),
            new StubDetector("donchian_breakdown", BinanceBot.Domain.Common.TradeDirection.Short),
            // Neutral
            new StubDetector("volume_surge_gate", null),
            new StubDetector("spread_guard_gate", null),
            new StubDetector("adx_regime_filter", null),
        };

        private sealed class StubDetector : IPatternDetector
        {
            public StubDetector(string name, BinanceBot.Domain.Common.TradeDirection? dir)
            {
                Name = name;
                Direction = dir;
            }
            public string Name { get; }
            public decimal DefaultWeight => 1m;
            public bool IsHardGate => false;
            public BinanceBot.Domain.Common.TradeDirection? Direction { get; }
            public PatternEvaluation Evaluate(BarSnapshot snapshot) =>
                new(Name, 0m);
        }
    }





    [Fact]
    public void Compose_BelowThreshold_SkipsScoreBelowThreshold()
    {
        var snap = MakeSnap();
        // 1×3 = 3 (ema_squeeze_break), 5'in altı
        var evals = new List<PatternEvaluation>
        {
            new("ema_squeeze_break", 1m),
            new("volume_surge_gate", 1m),
            new("spread_guard_gate", 1m),
            new("adx_regime_filter", 1m),
        };
        var d = Sut().Compose(snap, evals, new PatternComposerOptions { RequiredScore = 5m });
        d.Emit.Should().BeFalse();
        d.SkipReason.Should().Be("score_below_threshold");
        d.TotalScore.Should().Be(3m);
    }

    [Fact]
    public void Compose_AboveThreshold_EmitsWithGeometry()
    {
        var snap = MakeSnap(close: 100m, atr: 0.5m);
        // 1×3 + 1×4 = 7 → ≥5
        var evals = new List<PatternEvaluation>
        {
            new("ema_squeeze_break", 1m),
            new("volume_spike_donchian", 1m),
            new("volume_surge_gate", 1m),
            new("spread_guard_gate", 1m),
            new("adx_regime_filter", 1m),
        };
        var opts = new PatternComposerOptions
        {
            RequiredScore = 5m,
            SlAtrMultiplier = 1.2m,
            MinSlPct = 0.006m,
            TpRiskRewardRatio = 2m,
        };
        var d = Sut().Compose(snap, evals, opts);
        d.Emit.Should().BeTrue();
        d.TotalScore.Should().Be(7m);
        d.EntryPrice.Should().Be(100m);
        // SL: max(0.5×1.2/100=0.006, MinSlPct=0.006) → 0.006 → SL = 99.4
        d.StopPrice.Should().Be(99.4m);
        // TP: entry × (1 + 0.006 × 2) = 100 × 1.012 = 101.2
        d.TakeProfitPrice.Should().Be(101.2m);
    }

    [Fact]
    public void Compose_AdxOutOfRegime_AppliesSoftMultiplier()
    {
        var snap = MakeSnap();
        // 1×3 + 1×4 = 7 → 7 × 0.7 = 4.9 → < 5, skip
        var evals = new List<PatternEvaluation>
        {
            new("ema_squeeze_break", 1m),
            new("volume_spike_donchian", 1m),
            new("volume_surge_gate", 1m),
            new("spread_guard_gate", 1m),
            new("adx_regime_filter", 0m), // out of regime
        };
        var opts = new PatternComposerOptions
        {
            RequiredScore = 5m,
            AdxOutsideRegimeMultiplier = 0.7m,
        };
        var d = Sut().Compose(snap, evals, opts);
        d.Emit.Should().BeFalse();
        d.TotalScore.Should().Be(4.9m);
    }

    [Fact]
    public void Compose_WeightOverrides_AppliesPerName()
    {
        var snap = MakeSnap();
        var evals = new List<PatternEvaluation>
        {
            new("ema_squeeze_break", 1m),
            new("volume_surge_gate", 1m),
            new("spread_guard_gate", 1m),
            new("adx_regime_filter", 1m),
        };
        var opts = new PatternComposerOptions
        {
            RequiredScore = 5m,
            WeightOverrides = new Dictionary<string, decimal>
            {
                ["ema_squeeze_break"] = 6m, // override 3 → 6
            },
        };
        var d = Sut().Compose(snap, evals, opts);
        d.Emit.Should().BeTrue();
        d.TotalScore.Should().Be(6m);
    }

    [Fact]
    public void Compose_ScoreClamping_OutOfRangeClamped()
    {
        var snap = MakeSnap(close: 100m, atr: 0.5m);
        var evals = new List<PatternEvaluation>
        {
            new("ema_squeeze_break", 5m), // out of range, clamp to 1
            new("volume_surge_gate", 1m),
            new("spread_guard_gate", 1m),
            new("adx_regime_filter", 1m),
        };
        var d = Sut().Compose(snap, evals, new PatternComposerOptions { RequiredScore = 5m });
        // 1×3 = 3 < 5 → skip (clamped)
        d.TotalScore.Should().Be(3m);
        d.Emit.Should().BeFalse();
    }

    [Fact]
    public void Compose_GeometryInvalid_EntryZero_Skips()
    {
        var snap = MakeSnap(close: 0m); // entry = 0
        var evals = new List<PatternEvaluation>
        {
            new("ema_squeeze_break", 1m),
            new("volume_spike_donchian", 1m),
            new("volume_surge_gate", 1m),
            new("spread_guard_gate", 1m),
            new("adx_regime_filter", 1m),
        };
        var d = Sut().Compose(snap, evals, new PatternComposerOptions { RequiredScore = 5m });
        d.Emit.Should().BeFalse();
        d.SkipReason.Should().Contain("geometry_invalid");
    }
}
