using BinanceBot.Application.Abstractions;
using BinanceBot.Application.Strategies.Cooldowns;
using BinanceBot.Application.Strategies.Evaluation;
using BinanceBot.Application.Strategies.Swing;
using BinanceBot.Domain.MarketData;
using BinanceBot.Domain.Strategies;
using Microsoft.Extensions.Logging;

namespace BinanceBot.Infrastructure.Strategies.Evaluators;

/// <summary>
/// Loop 112 — ADR-0027 Aile A SwingTrade. <see cref="IStrategyEvaluator"/>
/// plug-in implementasyonu.
///
/// <para>
/// Tetikleme: <see cref="BinanceBot.Domain.MarketData.Events.KlineClosedEvent"/>
/// — handler bar'ın <c>Interval</c>'ini yorumlamadan tüm Active strategy'leri
/// iterate eder; evaluator kendisi 4h interval kontrolü yapar (closedBars
/// içeriği <see cref="KlineInterval.FourHours"/> değilse skip).
/// </para>
///
/// <para>
/// Akış (commit 7-10'da doldurulacak):
/// <list type="number">
///   <item>4h kline window yetersizse skip (warmup).</item>
///   <item>EMA20 vs EMA50 trend yönü.</item>
///   <item>Volume(bar) &gt; VolumeSma(20) × 1.5 — momentum onayı.</item>
///   <item>RSI(14) ∈ [RsiLongMin, RsiLongMax] (Long) / [RsiShortMin, RsiShortMax] (Short).</item>
///   <item>ATR(14) × 1.5 SL, ATR × 3 TP (R:R 1:2).</item>
///   <item>Cooldown — 1 bar = 4h, signal başına bir emit.</item>
/// </list>
/// </para>
///
/// <para>
/// Bu commit (5/14): skeleton + Type + warmup + interval guard. Entry signal
/// logic boş; commit 7-9'da doldurulur. Build temiz, evaluator kayıtlı ama
/// emit etmez (null return).
/// </para>
/// </summary>
public sealed class SwingTradeEvaluator : IStrategyEvaluator
{
    public StrategyType Type => StrategyType.SwingTrade;

    /// <summary>
    /// 4h timeframe sabitleri. Bar = 240 minutes = 4 hours. Cooldown anchor
    /// "1 bar" karşılığı 240dk; <see cref="ICooldownService"/> bu bar-minute
    /// üzerinden çalışır.
    /// </summary>
    private const int BarMinutes4h = 240;

    /// <summary>
    /// Swing 4h evaluator için minimum bar history. EmaLongPeriod (50) + ATR
    /// (14) + RSI (14) + VolumeSma (20) gereksinimleri 50 bar yeterli; emniyet
    /// payı 60 bar.
    /// </summary>
    internal const int MinBarsForEmit = 60;

    private readonly ICooldownService _cooldown;
    private readonly IClock _clock;
    private readonly ILogger<SwingTradeEvaluator> _logger;

    public SwingTradeEvaluator(
        ICooldownService cooldown,
        IClock clock,
        ILogger<SwingTradeEvaluator> logger)
    {
        _cooldown = cooldown;
        _clock = clock;
        _logger = logger;
    }

    public Task<StrategyEvaluation?> EvaluateAsync(
        long strategyId,
        string parametersJson,
        string symbol,
        IReadOnlyList<Kline> closedBars,
        CancellationToken cancellationToken)
    {
        // Skeleton: parametre parse + timeframe guard + warmup. Entry logic
        // commit 7'de eklenecek; bu commit'te evaluator emit etmez.
        var options = EvaluatorParameterHelper.TryParse<SwingTradeOptions>(parametersJson)
            ?? new SwingTradeOptions();

        // 4h interval guard — handler 5m+15m+4h hepsi için event publish ediyor;
        // evaluator yalnızca 4h kapanışında çalışır.
        if (closedBars.Count == 0 || closedBars[^1].Interval != KlineInterval.FourHours)
        {
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        // Warmup yetersizse skip — log "info" değil "debug" (her 4h bar emisyonunda
        // tekrar görünür, gürültüye yol açmasın).
        if (closedBars.Count < MinBarsForEmit)
        {
            _logger.LogDebug(
                "SwingTrade warmup symbol={Symbol} strategyId={StrategyId} bars={Bars}/{Min}",
                symbol, strategyId, closedBars.Count, MinBarsForEmit);
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        // Skeleton — entry signal logic commit 7'de doldurulur.
        // Audit log: 4h bar close detected, warmup OK, henüz emit kararı yok.
        _logger.LogDebug(
            "SwingTrade skeleton symbol={Symbol} strategyId={StrategyId} bars={Bars} optEmaShort={ES} optEmaLong={EL}",
            symbol, strategyId, closedBars.Count, options.EmaShortPeriod, options.EmaLongPeriod);

        // Cooldown service inject edildi (commit 7+'da emit recordAfterSignal),
        // şu an kullanılmıyor; clock da öyle. Suppress unused warning yok — okunur
        // referans yeterli.
        _ = _cooldown;
        _ = _clock;

        return Task.FromResult<StrategyEvaluation?>(null);
    }
}
