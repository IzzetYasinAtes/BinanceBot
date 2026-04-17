using BinanceBot.Application.Strategies.Evaluation;
using BinanceBot.Domain.Strategies;

namespace BinanceBot.Infrastructure.Strategies;

public sealed class StrategyEvaluatorRegistry
{
    private readonly Dictionary<StrategyType, IStrategyEvaluator> _map;

    public StrategyEvaluatorRegistry(IEnumerable<IStrategyEvaluator> evaluators)
    {
        _map = evaluators.ToDictionary(e => e.Type);
    }

    public IStrategyEvaluator? Resolve(StrategyType type) =>
        _map.TryGetValue(type, out var e) ? e : null;
}
