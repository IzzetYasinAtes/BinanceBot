using BinanceBot.Application.Strategies.Evaluation;
using BinanceBot.Domain.MarketData;
using BinanceBot.Domain.Strategies;
using BinanceBot.Infrastructure.Strategies;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace BinanceBot.Tests.Infrastructure.Strategies;

/// <summary>
/// Loop 112 — ADR-0027 plug-in mimari audit. PatternComposite + SwingTrade
/// paralel kayıtlı; registry her iki Type'ı çözebilir, kayıtsız Type için
/// <c>null</c> döner (caller sessiz skip).
/// </summary>
public class StrategyEvaluatorRegistryTests
{
    private sealed class FakeEvaluator : IStrategyEvaluator
    {
        public StrategyType Type { get; }
        public FakeEvaluator(StrategyType type) => Type = type;

        public Task<StrategyEvaluation?> EvaluateAsync(
            long strategyId, string parametersJson, string symbol,
            IReadOnlyList<Kline> closedBars, CancellationToken cancellationToken) =>
            Task.FromResult<StrategyEvaluation?>(null);
    }

    [Fact]
    public void Resolve_PatternComposite_ReturnsRegisteredImpl()
    {
        var pattern = new FakeEvaluator(StrategyType.PatternComposite);
        var swing = new FakeEvaluator(StrategyType.SwingTrade);

        var registry = new StrategyEvaluatorRegistry(
            new IStrategyEvaluator[] { pattern, swing },
            NullLogger<StrategyEvaluatorRegistry>.Instance);

        registry.Resolve(StrategyType.PatternComposite).Should().BeSameAs(pattern);
    }

    [Fact]
    public void Resolve_SwingTrade_ReturnsRegisteredImpl()
    {
        var pattern = new FakeEvaluator(StrategyType.PatternComposite);
        var swing = new FakeEvaluator(StrategyType.SwingTrade);

        var registry = new StrategyEvaluatorRegistry(
            new IStrategyEvaluator[] { pattern, swing },
            NullLogger<StrategyEvaluatorRegistry>.Instance);

        registry.Resolve(StrategyType.SwingTrade).Should().BeSameAs(swing);
    }

    [Fact]
    public void Resolve_UnregisteredType_ReturnsNull()
    {
        // Sadece SwingTrade kayıtlı; PatternComposite yoklamada null beklenir.
        var swing = new FakeEvaluator(StrategyType.SwingTrade);

        var registry = new StrategyEvaluatorRegistry(
            new IStrategyEvaluator[] { swing },
            NullLogger<StrategyEvaluatorRegistry>.Instance);

        registry.Resolve(StrategyType.PatternComposite).Should().BeNull();
    }

    [Fact]
    public void Constructor_DuplicateType_Throws()
    {
        // Aynı StrategyType iki impl'le kayıtlı ⇒ ToDictionary anchor collision.
        // Composition root yapılandırma hatası — programmer error, throwing OK.
        var a = new FakeEvaluator(StrategyType.PatternComposite);
        var b = new FakeEvaluator(StrategyType.PatternComposite);

        var act = () => new StrategyEvaluatorRegistry(
            new IStrategyEvaluator[] { a, b },
            NullLogger<StrategyEvaluatorRegistry>.Instance);

        act.Should().Throw<ArgumentException>(
            "ToDictionary aynı key ile iki entry'de exception fırlatır");
    }
}
