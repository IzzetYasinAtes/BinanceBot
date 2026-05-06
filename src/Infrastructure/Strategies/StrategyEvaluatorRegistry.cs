using BinanceBot.Application.Strategies.Evaluation;
using BinanceBot.Domain.Strategies;
using Microsoft.Extensions.Logging;

namespace BinanceBot.Infrastructure.Strategies;

/// <summary>
/// Loop 81 — IStrategyEvaluator registry, plug-in pattern. Constructor IEnumerable
/// injection sayesinde aynı IStrategyEvaluator port'una çoğul implementation
/// kayıtlı olabilir; her impl <see cref="IStrategyEvaluator.Type"/> ile dictionary
/// anchor'ı verir.
///
/// Loop 112 — ADR-0027 strateji ailesi pivot:
/// <list type="bullet">
///   <item>PatternComposite (Type=3) — Paused, kayıtlı kalır, re-aktivasyonda
///     <c>Strategy.Status</c> flip yeterli.</item>
///   <item>SwingTrade (Type=4) — yeni Aile A evaluator, paralel kayıtlı.</item>
/// </list>
///
/// Boot zamanı audit log: registered evaluator sayısı + Type listesi. Çift kayıt
/// (aynı <see cref="StrategyType"/> ile iki impl) DI build'de
/// <see cref="ArgumentException"/> üretir — caller composition root'ta yakalar
/// (yapılandırma hatası, programmer error).
/// </summary>
public sealed class StrategyEvaluatorRegistry
{
    private readonly Dictionary<StrategyType, IStrategyEvaluator> _map;

    public StrategyEvaluatorRegistry(
        IEnumerable<IStrategyEvaluator> evaluators,
        ILogger<StrategyEvaluatorRegistry> logger)
    {
        _map = evaluators.ToDictionary(e => e.Type);

        // Audit: hangi Type'lar kayıtlı, plug-in registry sağlığı görünür olsun.
        // ADR-0027 §27.13 reviewer kontrol: resolve count = 2 (PatternComposite +
        // SwingTrade) — log'tan doğrulanabilir.
        logger.LogInformation(
            "StrategyEvaluatorRegistry initialized: count={Count} types=[{Types}]",
            _map.Count,
            string.Join(",", _map.Keys.Select(k => k.ToString())));
    }

    /// <summary>
    /// Strategy Type'a karşılık gelen evaluator'ı döner. Kayıt yoksa
    /// <c>null</c> — caller (StrategyEvaluationHandler) "missing evaluator"
    /// log'lar ve o strategy'yi skip eder. Throwing for control flow yasak
    /// (CLAUDE.md §3 kural 5).
    /// </summary>
    public IStrategyEvaluator? Resolve(StrategyType type) =>
        _map.TryGetValue(type, out var e) ? e : null;
}
