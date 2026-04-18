using BinanceBot.Domain.MarketData;
using BinanceBot.Domain.Strategies;

namespace BinanceBot.Application.Strategies.Evaluation;

public interface IStrategyEvaluator
{
    StrategyType Type { get; }

    Task<StrategyEvaluation?> EvaluateAsync(
        long strategyId,
        string parametersJson,
        string symbol,
        IReadOnlyList<Kline> closedBars,
        CancellationToken cancellationToken);
}

public sealed record StrategyEvaluation(
    StrategySignalDirection Direction,
    decimal SuggestedQuantity,
    decimal? SuggestedPrice,
    decimal? SuggestedStopPrice,
    string ContextJson,
    // Loop 10 take-profit fix — evaluator-suggested profit target. Default null is required
    // for backward compatibility with older evaluators / tests that don't set it.
    decimal? SuggestedTakeProfit = null);
