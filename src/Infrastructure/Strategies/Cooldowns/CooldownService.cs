using System.Collections.Concurrent;
using BinanceBot.Application.Strategies.Cooldowns;
using Microsoft.Extensions.Logging;

namespace BinanceBot.Infrastructure.Strategies.Cooldowns;

/// <summary>
/// Loop 42 P0 fix — in-memory thread-safe cooldown enforcement.
///
/// Protokol: evaluator sinyal koşullarını sağladığı an <see cref="IsCooldown"/>
/// kontrolünü yapar; <c>true</c> ise <c>null</c> evaluation döner.
/// Sinyal yayınlandığında (skip değil) <see cref="RecordSignal"/> çağrılır.
///
/// Storage: <see cref="ConcurrentDictionary{TKey,TValue}"/> singleton; key
/// (StrategyId, NormalizedSymbol) tuple, value son sinyal zamanı. Process restart
/// sonrası kayıp kabul edilir — boot warmup zaten ilk birkaç barı sinyalsiz
/// bekletir, gerçek dünya etkisi minimal.
///
/// Thread safety: <c>AddOrUpdate</c> + atomic <c>TryGetValue</c> kullanılır;
/// concurrent evaluator çağrıları (parallel symbol kline events) için yeterli.
/// </summary>
public sealed class CooldownService : ICooldownService
{
    private readonly ConcurrentDictionary<(long StrategyId, string Symbol), DateTimeOffset> _lastSignals = new();
    private readonly ILogger<CooldownService> _logger;

    public CooldownService(ILogger<CooldownService> logger)
    {
        _logger = logger;
    }

    public bool IsCooldown(
        long strategyId,
        string symbol,
        int cooldownBars,
        int barMinutes,
        DateTimeOffset now)
    {
        if (cooldownBars <= 0 || barMinutes <= 0)
        {
            return false;
        }

        var key = (strategyId, Normalize(symbol));
        if (!_lastSignals.TryGetValue(key, out var lastAt))
        {
            return false;
        }

        var window = TimeSpan.FromMinutes((double)cooldownBars * barMinutes);
        var elapsed = now - lastAt;
        var inCooldown = elapsed < window;

        if (inCooldown)
        {
            _logger.LogInformation(
                "Cooldown active strategyId={StrategyId} symbol={Symbol} elapsedSec={Elapsed} windowSec={Window} lastAt={LastAt}",
                strategyId, key.Item2, (long)elapsed.TotalSeconds, (long)window.TotalSeconds, lastAt);
        }

        return inCooldown;
    }

    public void RecordSignal(long strategyId, string symbol, DateTimeOffset now)
    {
        var key = (strategyId, Normalize(symbol));
        _lastSignals.AddOrUpdate(key, now, (_, _) => now);
        _logger.LogDebug(
            "Cooldown record strategyId={StrategyId} symbol={Symbol} at={At}",
            strategyId, key.Item2, now);
    }

    /// <summary>
    /// Loop 71 KMS — şimdilik stub: tüm (strategyId, symbol) çiftleri için
    /// sabit 4 döner (KMS evaluator min skor eşiği). Loop 72: streak guard
    /// implementasyonu burada — ardışık SL sonrası eşik 5/6'ya bump edilir,
    /// kazançtan sonra 4'e döner.
    /// </summary>
    public int GetCurrentScoreThreshold(long strategyId, string symbol) => 4;

    private static string Normalize(string symbol) =>
        string.IsNullOrEmpty(symbol) ? string.Empty : symbol.ToUpperInvariant();
}
