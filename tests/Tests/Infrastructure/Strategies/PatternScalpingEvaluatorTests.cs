using System.Text.Json;
using BinanceBot.Application.Strategies.Evaluation;
using BinanceBot.Application.Strategies.Patterns;
using BinanceBot.Domain.MarketData;
using BinanceBot.Domain.Strategies;
using BinanceBot.Domain.ValueObjects;
using BinanceBot.Infrastructure.Strategies.Evaluators;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace BinanceBot.Tests.Infrastructure.Strategies;

/// <summary>
/// ADR-0014 §14.9.2 — PatternScalpingEvaluator orchestrates the 14 detectors via
/// weighted vote (weight × confidence per direction). Tests use stub detectors so
/// the vote logic is isolated from any individual pattern algorithm.
/// </summary>
public class PatternScalpingEvaluatorTests
{
    private const string DefaultParams =
        "{\"EntryThreshold\":0.55,\"StrongThreshold\":0.75,\"OrderSize\":0.001}";

    private sealed class StubDetector : IPatternDetector
    {
        private readonly PatternResult? _result;
        public StubDetector(PatternType type, decimal weight, PatternResult? result)
        {
            Type = type;
            Weight = weight;
            _result = result;
        }
        public PatternType Type { get; }
        public decimal Weight { get; }
        public PatternResult? Detect(IReadOnlyList<Kline> closedBars) => _result;
    }

    private static IReadOnlyList<Kline> EmptyBars() => Array.Empty<Kline>();

    private static PatternResult R(PatternType t, PatternDirection dir, decimal conf, int maxHold = 5,
        decimal entry = 100m, decimal stop = 99m, decimal tp = 102m)
        => new(t, dir, conf, entry, stop, tp, maxHold, "{}");

    [Fact]
    public async Task NoDetections_ReturnsNull()
    {
        var detectors = new IPatternDetector[]
        {
            new StubDetector(PatternType.DoubleBottom, 0.85m, null),
            new StubDetector(PatternType.Hammer, 0.58m, null),
        };
        var sut = new PatternScalpingEvaluator(detectors, NullLogger<PatternScalpingEvaluator>.Instance);

        var r = await sut.EvaluateAsync(1, DefaultParams, "BTCUSDT", EmptyBars(), CancellationToken.None);

        r.Should().BeNull();
    }

    [Fact]
    public async Task SingleStrongLongDetection_AboveThreshold_ReturnsLong()
    {
        // DoubleBottom weight 0.85 × confidence 0.9 = 0.765 > threshold 0.55.
        var detectors = new IPatternDetector[]
        {
            new StubDetector(PatternType.DoubleBottom, 0.85m,
                R(PatternType.DoubleBottom, PatternDirection.Long, 0.9m)),
        };
        var sut = new PatternScalpingEvaluator(detectors, NullLogger<PatternScalpingEvaluator>.Instance);

        var r = await sut.EvaluateAsync(1, DefaultParams, "BTCUSDT", EmptyBars(), CancellationToken.None);

        r.Should().NotBeNull();
        r!.Direction.Should().Be(StrategySignalDirection.Long);
        r.SuggestedTakeProfit.Should().Be(102m);
        r.SuggestedStopPrice.Should().Be(99m);
    }

    [Fact]
    public async Task MixedSignals_LongScoreWins()
    {
        // Long: DoubleBottom 0.85 × 0.9 = 0.765
        // Short: BearishEngulfing 0.55 × 0.7 = 0.385
        // Long wins both on score and as winner direction.
        var detectors = new IPatternDetector[]
        {
            new StubDetector(PatternType.DoubleBottom, 0.85m,
                R(PatternType.DoubleBottom, PatternDirection.Long, 0.9m)),
            new StubDetector(PatternType.BearishEngulfing, 0.55m,
                R(PatternType.BearishEngulfing, PatternDirection.Short, 0.7m)),
        };
        var sut = new PatternScalpingEvaluator(detectors, NullLogger<PatternScalpingEvaluator>.Instance);

        var r = await sut.EvaluateAsync(1, DefaultParams, "BTCUSDT", EmptyBars(), CancellationToken.None);

        r.Should().NotBeNull();
        r!.Direction.Should().Be(StrategySignalDirection.Long);
    }

    [Fact]
    public async Task WeakSignal_BelowThreshold_ReturnsNull()
    {
        // Hammer 0.58 × 0.4 = 0.232 < threshold 0.55.
        var detectors = new IPatternDetector[]
        {
            new StubDetector(PatternType.Hammer, 0.58m,
                R(PatternType.Hammer, PatternDirection.Long, 0.4m)),
        };
        var sut = new PatternScalpingEvaluator(detectors, NullLogger<PatternScalpingEvaluator>.Instance);

        var r = await sut.EvaluateAsync(1, DefaultParams, "BTCUSDT", EmptyBars(), CancellationToken.None);

        r.Should().BeNull();
    }

    [Fact]
    public async Task ContextJson_IncludesAllPatternsAndMaxHoldBars()
    {
        var detectors = new IPatternDetector[]
        {
            new StubDetector(PatternType.DoubleBottom, 0.85m,
                R(PatternType.DoubleBottom, PatternDirection.Long, 0.9m, maxHold: 10)),
            new StubDetector(PatternType.MorningStar, 0.67m,
                R(PatternType.MorningStar, PatternDirection.Long, 0.8m, maxHold: 7)),
            new StubDetector(PatternType.BullishEngulfing, 0.55m,
                R(PatternType.BullishEngulfing, PatternDirection.Long, 0.7m, maxHold: 5)),
        };
        var sut = new PatternScalpingEvaluator(detectors, NullLogger<PatternScalpingEvaluator>.Instance);

        var r = await sut.EvaluateAsync(1, DefaultParams, "BTCUSDT", EmptyBars(), CancellationToken.None);

        r.Should().NotBeNull();
        using var doc = JsonDocument.Parse(r!.ContextJson);
        doc.RootElement.GetProperty("type").GetString().Should().Be("patternscalping");
        doc.RootElement.GetProperty("detectionCount").GetInt32().Should().Be(3);
        doc.RootElement.GetProperty("allPatterns").GetArrayLength().Should().Be(3);
        // Leader = highest contribution = DoubleBottom (0.85 × 0.9 = 0.765).
        doc.RootElement.GetProperty("leaderPattern").GetString().Should().Be("DoubleBottom");
        // maxHoldBars carries the leader's value, not the average.
        doc.RootElement.GetProperty("maxHoldBars").GetInt32().Should().Be(10);
    }

    [Fact]
    public async Task LeaderSelection_HighestContribution_PicksLeader()
    {
        // Two long detections; MorningStar's weight × confidence is higher than DoubleBottom's.
        // 0.67 × 0.95 = 0.6365 vs 0.85 × 0.7 = 0.595 → MorningStar leads.
        var detectors = new IPatternDetector[]
        {
            new StubDetector(PatternType.DoubleBottom, 0.85m,
                R(PatternType.DoubleBottom, PatternDirection.Long, 0.7m, maxHold: 10,
                    entry: 100m, stop: 99m, tp: 102m)),
            new StubDetector(PatternType.MorningStar, 0.67m,
                R(PatternType.MorningStar, PatternDirection.Long, 0.95m, maxHold: 7,
                    entry: 101m, stop: 99.5m, tp: 103.5m)),
        };
        var sut = new PatternScalpingEvaluator(detectors, NullLogger<PatternScalpingEvaluator>.Instance);

        var r = await sut.EvaluateAsync(1, DefaultParams, "BTCUSDT", EmptyBars(), CancellationToken.None);

        r.Should().NotBeNull();
        r!.SuggestedPrice.Should().Be(101m);
        r.SuggestedStopPrice.Should().Be(99.5m);
        r.SuggestedTakeProfit.Should().Be(103.5m);
    }
}
