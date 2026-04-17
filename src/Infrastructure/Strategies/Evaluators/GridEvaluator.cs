using BinanceBot.Application.Strategies.Evaluation;
using BinanceBot.Domain.MarketData;
using BinanceBot.Domain.Strategies;

namespace BinanceBot.Infrastructure.Strategies.Evaluators;

public sealed class GridEvaluator : IStrategyEvaluator
{
    public StrategyType Type => StrategyType.Grid;

    private sealed class Parameters
    {
        public decimal LowerPrice { get; set; }
        public decimal UpperPrice { get; set; }
        public int GridCount { get; set; } = 10;
        public decimal OrderSize { get; set; } = 0.001m;
    }

    public Task<StrategyEvaluation?> EvaluateAsync(
        long strategyId, string parametersJson, string symbol,
        IReadOnlyList<Kline> closedBars, CancellationToken cancellationToken)
    {
        if (closedBars.Count == 0)
        {
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        var p = EvaluatorParameterHelper.TryParse<Parameters>(parametersJson);
        if (p is null || p.UpperPrice <= p.LowerPrice || p.GridCount <= 1)
        {
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        var latest = closedBars[^1];
        if (latest.ClosePrice < p.LowerPrice || latest.ClosePrice > p.UpperPrice)
        {
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        if (closedBars.Count < 2)
        {
            return Task.FromResult<StrategyEvaluation?>(null);
        }
        var prior = closedBars[^2];

        var step = (p.UpperPrice - p.LowerPrice) / p.GridCount;
        var priorBucket = (int)((prior.ClosePrice - p.LowerPrice) / step);
        var latestBucket = (int)((latest.ClosePrice - p.LowerPrice) / step);

        if (priorBucket == latestBucket)
        {
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        var direction = latestBucket < priorBucket
            ? StrategySignalDirection.Long
            : StrategySignalDirection.Short;

        var ctx = EvaluatorParameterHelper.SerializeContext(new
        {
            type = "grid",
            priorBucket,
            latestBucket,
            step,
            lower = p.LowerPrice,
            upper = p.UpperPrice,
        });

        return Task.FromResult<StrategyEvaluation?>(new StrategyEvaluation(
            direction,
            p.OrderSize,
            latest.ClosePrice,
            null,
            ctx));
    }
}
