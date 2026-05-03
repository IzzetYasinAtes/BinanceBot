using BinanceBot.Application.Abstractions;
using BinanceBot.Application.Strategies.Cooldowns;
using BinanceBot.Application.Strategies.Evaluation;
using BinanceBot.Application.Strategies.Indicators;
using BinanceBot.Application.Strategies.Patterns;
using BinanceBot.Domain.Common;
using BinanceBot.Domain.MarketData;
using BinanceBot.Domain.Strategies;
using Microsoft.Extensions.Logging;

namespace BinanceBot.Infrastructure.Strategies.Evaluators;

/// <summary>
/// Loop 81 — Pattern-composite tek <see cref="IStrategyEvaluator"/>
/// implementasyonu (ADR-0024 §24.5). Akış:
/// <list type="number">
///   <item>BarSnapshot iste (warmup yetersiz ⇒ skip).</item>
///   <item>PatternRegistry'deki tüm detector'ları snapshot üzerinde değerlendir.</item>
///   <item>Composer ağırlıklı toplam skoru ve emit/skip kararını üretsin.</item>
///   <item>Emit ⇒ cooldown kontrol; geçerse RecordSignal + StrategyEvaluation.</item>
/// </list>
/// </summary>
public sealed class PatternCompositeEvaluator : IStrategyEvaluator
{
    public StrategyType Type => StrategyType.PatternComposite;

    private const int BarMinutes5m = 5;

    private readonly IMarketIndicatorService _indicators;
    private readonly IPatternRegistry _registry;
    private readonly IPatternSignalComposer _composer;
    private readonly ICooldownService _cooldown;
    private readonly IClock _clock;
    private readonly ILogger<PatternCompositeEvaluator> _logger;

    public PatternCompositeEvaluator(
        IMarketIndicatorService indicators,
        IPatternRegistry registry,
        IPatternSignalComposer composer,
        ICooldownService cooldown,
        IClock clock,
        ILogger<PatternCompositeEvaluator> logger)
    {
        _indicators = indicators;
        _registry = registry;
        _composer = composer;
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
        var options = EvaluatorParameterHelper.TryParse<PatternComposerOptions>(parametersJson)
            ?? new PatternComposerOptions();

        var snapshot = _indicators.TryGetBarSnapshot(symbol);
        if (snapshot is null)
        {
            _logger.LogDebug(
                "PatternComposite snapshot not ready symbol={Symbol} strategyId={StrategyId}",
                symbol, strategyId);
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        // Loop 92 — MTF gate Direction'a göre composer kararı sonrası uygulanır.
        // Önce composer evaluations + iki-kova skor topla; emit yön belirledikten sonra
        // MTF gate Long: slope > +%0.1, Short: slope < -%0.1; RSI cap Long: > 75,
        // Short: < 25 (simetrik). Önceki Loop 91 sadece downtrend'de skip yapıyordu.

        // Detector'ları sırayla değerlendir
        var detectors = _registry.Detectors;
        var evaluations = new List<PatternEvaluation>(detectors.Count);
        foreach (var d in detectors)
        {
            try
            {
                evaluations.Add(d.Evaluate(snapshot));
            }
            catch (Exception ex)
            {
                // Detector exception programmer error; defansif olarak skip
                _logger.LogError(ex,
                    "Pattern detector {Detector} threw on symbol={Symbol}", d.Name, symbol);
                evaluations.Add(new PatternEvaluation(d.Name, 0m, Reason: "detector_threw"));
            }
        }

        // Composer kararı
        var decision = _composer.Compose(snapshot, evaluations, options);

        if (!decision.Emit)
        {
            _logger.LogDebug(
                "PatternComposite skip symbol={Symbol} strategyId={StrategyId} " +
                "totalScore={Total} required={Req} reason={Reason}",
                symbol, strategyId, decision.TotalScore, decision.RequiredScore, decision.SkipReason);
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        // Loop 92 — MTF gate Direction-aware (ADR-0025 §4 yön asimetrisi).
        // 15m warmup yetersiz (Ema21_15m == 0) ⇒ skip (güvenli taraf, eski davranış korunuyor).
        var direction = decision.Direction ?? TradeDirection.Long;
        if (snapshot.Ema21_15m <= 0m)
        {
            _logger.LogDebug(
                "PatternComposite skip symbol={Symbol} ema21_15m={Ema} reason=mtf_15m_warmup",
                symbol, snapshot.Ema21_15m);
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        var slope15m = snapshot.Ema21_15m - snapshot.Ema21Prev5_15m;
        var mtfThreshold = snapshot.Ema21_15m * 0.002m;  // %0.2 kademe (Loop 96: gerçek gevşetme — Loop 95 yanlış yön 0.0005 sıkmıştı)

        if (direction == TradeDirection.Long && slope15m < -mtfThreshold)
        {
            _logger.LogDebug(
                "PatternComposite skip Long symbol={Symbol} ema21_15m={Ema} slope15m={Slope} reason=mtf_15m_slope_down",
                symbol, snapshot.Ema21_15m, slope15m);
            return Task.FromResult<StrategyEvaluation?>(null);
        }
        if (direction == TradeDirection.Short && slope15m > mtfThreshold)
        {
            _logger.LogDebug(
                "PatternComposite skip Short symbol={Symbol} ema21_15m={Ema} slope15m={Slope} reason=mtf_15m_slope_up",
                symbol, snapshot.Ema21_15m, slope15m);
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        // Loop 92 — RSI cap Direction-aware. Long: RSI > MaxEmit (örn. 85) skip.
        // Short: RSI < (100 - MaxEmit) (örn. 15) skip — aşırı satılmışta short açma.
        var rsiMin = 100m - options.RsiMaxEmit;
        if (direction == TradeDirection.Long && snapshot.Rsi14 > options.RsiMaxEmit)
        {
            _logger.LogDebug(
                "PatternComposite skip Long symbol={Symbol} rsi14={Rsi} cap={Cap} reason=rsi_overbought",
                symbol, snapshot.Rsi14, options.RsiMaxEmit);
            return Task.FromResult<StrategyEvaluation?>(null);
        }
        if (direction == TradeDirection.Short && snapshot.Rsi14 < rsiMin)
        {
            _logger.LogDebug(
                "PatternComposite skip Short symbol={Symbol} rsi14={Rsi} cap={Cap} reason=rsi_oversold",
                symbol, snapshot.Rsi14, rsiMin);
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        // Cooldown kontrolü — emit kararından sonra (KMS pattern aynısı)
        var now = _clock.UtcNow;
        if (_cooldown.IsCooldown(strategyId, symbol, options.CooldownBarsAfterSignal, BarMinutes5m, now))
        {
            _logger.LogInformation(
                "PatternComposite cooldown skip symbol={Symbol} strategyId={StrategyId} " +
                "cooldownBars={Bars} barMinutes={BarMinutes}",
                symbol, strategyId, options.CooldownBarsAfterSignal, BarMinutes5m);
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        if (decision.EntryPrice is null || decision.StopPrice is null
            || decision.TakeProfitPrice is null)
        {
            _logger.LogWarning(
                "PatternComposite emit decision missing geometry symbol={Symbol} strategyId={StrategyId}",
                symbol, strategyId);
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        _logger.LogInformation(
            "PatternComposite emit symbol={Symbol} strategyId={StrategyId} " +
            "totalScore={Total}/{Req} entry={Entry} stop={Stop} tp={Tp} maxHold={MaxHold} decision=Emit",
            symbol, strategyId, decision.TotalScore, decision.RequiredScore,
            decision.EntryPrice, decision.StopPrice, decision.TakeProfitPrice, decision.MaxHoldMinutes);

        _cooldown.RecordSignal(strategyId, symbol, now);

        // Loop 92 — Direction propagation. Composer Long ise StrategySignalDirection.Long,
        // Short ise StrategySignalDirection.Short. StrategySignal aggregate aynı yapı.
        var sigDirection = direction == TradeDirection.Long
            ? StrategySignalDirection.Long
            : StrategySignalDirection.Short;

        return Task.FromResult<StrategyEvaluation?>(new StrategyEvaluation(
            Direction: sigDirection,
            SuggestedQuantity: 0.00000001m,
            SuggestedPrice: decision.EntryPrice,
            SuggestedStopPrice: decision.StopPrice,
            ContextJson: decision.ContextJson,
            SuggestedTakeProfit: decision.TakeProfitPrice));
    }
}
