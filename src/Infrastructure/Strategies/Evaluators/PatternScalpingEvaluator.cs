using BinanceBot.Application.Strategies.Evaluation;
using BinanceBot.Application.Strategies.Patterns;
using BinanceBot.Domain.MarketData;
using BinanceBot.Domain.Strategies;
using Microsoft.Extensions.Logging;

namespace BinanceBot.Infrastructure.Strategies.Evaluators;

/// <summary>
/// ADR-0014 §14.4: orchestrates the 14 <see cref="IPatternDetector"/> implementations,
/// scores them via weighted vote (per-direction), and emits a signal carrying the
/// leader pattern's stop / TP / max-hold proposal.
///
/// Mode-agnostic — runs once per bar at signal layer; the fan-out handler
/// (<c>StrategySignalToOrderHandler</c>) replicates per trading mode and the sizing
/// service (ADR-0011) overrides the suggested quantity per equity & risk profile.
/// </summary>
public sealed class PatternScalpingEvaluator : IStrategyEvaluator
{
    public StrategyType Type => StrategyType.PatternScalping;

    private readonly IReadOnlyList<IPatternDetector> _detectors;
    private readonly ILogger<PatternScalpingEvaluator> _logger;

    public PatternScalpingEvaluator(
        IEnumerable<IPatternDetector> detectors,
        ILogger<PatternScalpingEvaluator> logger)
    {
        _detectors = detectors.ToList();
        _logger = logger;
    }

    public Task<StrategyEvaluation?> EvaluateAsync(
        long strategyId,
        string parametersJson,
        string symbol,
        IReadOnlyList<Kline> closedBars,
        CancellationToken cancellationToken)
    {
        var p = EvaluatorParameterHelper.TryParse<Parameters>(parametersJson) ?? new Parameters();

        // 1. Fire every detector, keep non-null results (with their weight).
        var detections = new List<(IPatternDetector Detector, PatternResult Result)>();
        foreach (var detector in _detectors)
        {
            var r = detector.Detect(closedBars);
            if (r is not null)
            {
                detections.Add((detector, r));
            }
        }

        if (detections.Count == 0)
        {
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        // 2. Direction-bucketed weighted vote (weight × confidence).
        decimal longScore = 0m, shortScore = 0m;
        foreach (var (det, res) in detections)
        {
            var contribution = det.Weight * res.Confidence;
            if (res.Direction == PatternDirection.Long)
            {
                longScore += contribution;
            }
            else
            {
                shortScore += contribution;
            }
        }

        var winnerScore = Math.Max(longScore, shortScore);
        if (winnerScore < p.EntryThreshold)
        {
            _logger.LogDebug(
                "PatternScalping weak signal symbol={Symbol} long={Long} short={Short} threshold={Th}",
                symbol, longScore, shortScore, p.EntryThreshold);
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        var winnerDir = longScore > shortScore ? PatternDirection.Long : PatternDirection.Short;

        // 3. Leader = highest contribution among winning-direction detections.
        var leader = detections
            .Where(t => t.Result.Direction == winnerDir)
            .OrderByDescending(t => t.Detector.Weight * t.Result.Confidence)
            .First()
            .Result;

        var direction = winnerDir == PatternDirection.Long
            ? StrategySignalDirection.Long
            : StrategySignalDirection.Short;

        // 4. ContextJson — debug + audit + UI tooltip + maxHoldBars carrier for the
        // OrderFilledPositionHandler to wire into Position.MaxHoldDuration.
        var ctx = EvaluatorParameterHelper.SerializeContext(new
        {
            type = "patternscalping",
            leaderPattern = leader.Type.ToString(),
            leaderConfidence = leader.Confidence,
            longScore,
            shortScore,
            winnerScore,
            detectionCount = detections.Count,
            allPatterns = detections
                .Select(t => new
                {
                    pattern = t.Result.Type.ToString(),
                    direction = t.Result.Direction.ToString(),
                    weight = t.Detector.Weight,
                    confidence = t.Result.Confidence,
                })
                .ToArray(),
            maxHoldBars = leader.MaxHoldBars,
            isStrong = winnerScore >= p.StrongThreshold,
        });

        return Task.FromResult<StrategyEvaluation?>(new StrategyEvaluation(
            direction,
            p.OrderSize,
            leader.EntryPrice,
            leader.StopPrice,
            ctx,
            SuggestedTakeProfit: leader.TakeProfit));
    }

    /// <summary>
    /// Strategy parameters consumed from <c>Strategies.Seed[].ParametersJson</c>.
    /// Per-symbol tuning lives in appsettings (XRP threshold tighter, vol multiplier higher).
    /// </summary>
    private sealed class Parameters
    {
        public decimal EntryThreshold { get; set; } = 0.55m;
        public decimal StrongThreshold { get; set; } = 0.75m;
        public decimal OrderSize { get; set; } = 0.001m;
        public decimal VolumeMultiplier { get; set; } = 1.5m;
        public int LookbackBars { get; set; } = 20;
    }
}
