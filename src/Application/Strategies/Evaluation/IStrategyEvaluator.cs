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
    string ContextJson);
