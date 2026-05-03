using System.Text.Json;
using BinanceBot.Application.Strategies.Patterns;
using BinanceBot.Domain.Common;
using Microsoft.Extensions.Logging;

namespace BinanceBot.Infrastructure.Strategies.Patterns;

/// <summary>
/// Loop 81 + Loop 92 — Direction-aware ağırlıklı skor composer (ADR-0024 §24.9 + ADR-0025).
///
/// Algoritma (Loop 92 iki-kova):
/// <list type="number">
///   <item>Hard-gate disiplini: VolumeSurgeGate / SpreadGuardGate score=0 ⇒ skip (hard_gate:&lt;name&gt;).
///         Loop 89 hard-gate skip TEKRAR DEVRE DIŞI; ama hard-gate kayıtları ContextJson'da kalır.</item>
///   <item>Soft filter: <c>adx_regime_filter</c> evaluation 0 puan ise tüm
///         pattern skorları <c>options.AdxOutsideRegimeMultiplier</c> ile çarpılır.</item>
///   <item>İki-kova: Long-bias detector'lar Long bucket'a, Short-bias detector'lar Short bucket'a.
///         Neutral detector'lar (Direction=null) iki bucket'a da katkı verir.</item>
///   <item>longTotal &gt;= RequiredScore AND shortTotal &gt;= RequiredScore ⇒ skip
///         (both_directions_qualified). Yön belirsizliği — bar değişene kadar kararsızlık.</item>
///   <item>Sadece bir bucket geçerse o yönde emit. Geometri Direction'a göre asimetrik.</item>
/// </list>
/// </summary>
public sealed class WeightedScorePatternComposer : IPatternSignalComposer
{
    private const string AdxFilterName = "adx_regime_filter";

    private readonly ILogger<WeightedScorePatternComposer> _logger;
    private readonly IPatternRegistry _registry;

    public WeightedScorePatternComposer(
        ILogger<WeightedScorePatternComposer> logger,
        IPatternRegistry registry)
    {
        _logger = logger;
        _registry = registry;
    }

    public CompositeSignalDecision Compose(
        BarSnapshot snapshot,
        IReadOnlyList<PatternEvaluation> evaluations,
        PatternComposerOptions options)
    {
        // 1. Soft filter ADX
        decimal softMultiplier = 1m;
        var adxFilter = evaluations.FirstOrDefault(
            e => string.Equals(e.Name, AdxFilterName, StringComparison.OrdinalIgnoreCase));
        if (adxFilter is not null && adxFilter.Score <= 0m)
        {
            softMultiplier = options.AdxOutsideRegimeMultiplier;
        }

        // 2. Direction lookup (registry üzerinden) — composer evaluations'da Direction taşımıyor.
        var directionByName = _registry.Detectors.ToDictionary(
            d => d.Name, d => d.Direction, StringComparer.OrdinalIgnoreCase);

        // 3. İki-kova skor toplama
        decimal longTotal = 0m;
        decimal shortTotal = 0m;
        var contribs = new List<DetectorContrib>(evaluations.Count);

        foreach (var ev in evaluations)
        {
            if (ev.Score < 0m || ev.Score > 1m)
            {
                _logger.LogWarning(
                    "Detector {Name} returned out-of-range score {Score} (clamped)",
                    ev.Name, ev.Score);
            }
            var clamped = Math.Clamp(ev.Score, 0m, 1m);

            decimal weight = ResolveWeight(ev.Name, options);
            decimal contribution = clamped * weight * softMultiplier;

            // Direction routing
            directionByName.TryGetValue(ev.Name, out var dir);
            switch (dir)
            {
                case TradeDirection.Long:
                    longTotal += contribution;
                    break;
                case TradeDirection.Short:
                    shortTotal += contribution;
                    break;
                default:  // null = Neutral, both buckets
                    longTotal += contribution;
                    shortTotal += contribution;
                    break;
            }

            contribs.Add(new DetectorContrib(
                Name: ev.Name,
                Score: clamped,
                Weight: weight,
                SoftMultiplier: softMultiplier,
                Contribution: contribution,
                Direction: dir?.ToString(),
                Reason: ev.Reason));
        }

        // 4. Both-directions-qualified guard (Loop 92 spec §3 #2)
        var longQualified = longTotal >= options.RequiredScore;
        var shortQualified = shortTotal >= options.RequiredScore;

        if (longQualified && shortQualified)
        {
            const string skipReason = "both_directions_qualified";
            var ctxSkip = SerializeContext(snapshot, longTotal, shortTotal, options.RequiredScore,
                contribs, softMultiplier, emit: false, direction: null, skipReason: skipReason);
            return new CompositeSignalDecision(
                Emit: false, Direction: null,
                TotalScore: Math.Max(longTotal, shortTotal),
                RequiredScore: options.RequiredScore,
                SkipReason: skipReason,
                EntryPrice: null, StopPrice: null, TakeProfitPrice: null,
                MaxHoldMinutes: null, ContextJson: ctxSkip);
        }

        if (!longQualified && !shortQualified)
        {
            const string skipReason = "score_below_threshold";
            var ctxSkip = SerializeContext(snapshot, longTotal, shortTotal, options.RequiredScore,
                contribs, softMultiplier, emit: false, direction: null, skipReason: skipReason);
            return new CompositeSignalDecision(
                Emit: false, Direction: null,
                TotalScore: Math.Max(longTotal, shortTotal),
                RequiredScore: options.RequiredScore,
                SkipReason: skipReason,
                EntryPrice: null, StopPrice: null, TakeProfitPrice: null,
                MaxHoldMinutes: null, ContextJson: ctxSkip);
        }

        // 5. Tek bucket emit: yön belirle ve geometri hesapla
        var direction = longQualified ? TradeDirection.Long : TradeDirection.Short;
        var totalForDir = longQualified ? longTotal : shortTotal;

        var entry = snapshot.Close;
        if (entry <= 0m)
        {
            const string skipReason = "geometry_invalid:entry_zero";
            var ctxSkip = SerializeContext(snapshot, longTotal, shortTotal, options.RequiredScore,
                contribs, softMultiplier, emit: false, direction: null, skipReason: skipReason);
            return new CompositeSignalDecision(false, null, totalForDir, options.RequiredScore,
                skipReason, null, null, null, null, ctxSkip);
        }

        // SL hesabı (Long: entry altı; Short: entry üstü). slPct yüzde formundadır.
        decimal atrSlPct = entry > 0m ? snapshot.Atr14 * options.SlAtrMultiplier / entry : 0m;
        decimal slPct = Math.Max(atrSlPct, options.MinSlPct);
        if (options.MaxSlPct > 0m && slPct > options.MaxSlPct)
        {
            slPct = options.MaxSlPct;
        }

        decimal stop;
        decimal tp;
        decimal tpPct = slPct * options.TpRiskRewardRatio;

        if (direction == TradeDirection.Long)
        {
            stop = entry * (1m - slPct);
            tp = entry * (1m + tpPct);
            if (stop >= entry || tp <= entry)
            {
                const string skipReason = "geometry_invalid:tp_or_sl";
                var ctxSkip = SerializeContext(snapshot, longTotal, shortTotal, options.RequiredScore,
                    contribs, softMultiplier, emit: false, direction: null, skipReason: skipReason);
                return new CompositeSignalDecision(false, null, totalForDir, options.RequiredScore,
                    skipReason, null, null, null, null, ctxSkip);
            }
        }
        else  // Short
        {
            stop = entry * (1m + slPct);
            tp = entry * (1m - tpPct);
            if (stop <= entry || tp >= entry)
            {
                const string skipReason = "geometry_invalid:tp_or_sl";
                var ctxSkip = SerializeContext(snapshot, longTotal, shortTotal, options.RequiredScore,
                    contribs, softMultiplier, emit: false, direction: null, skipReason: skipReason);
                return new CompositeSignalDecision(false, null, totalForDir, options.RequiredScore,
                    skipReason, null, null, null, null, ctxSkip);
            }
        }

        var ctxEmit = SerializeContext(snapshot, longTotal, shortTotal, options.RequiredScore,
            contribs, softMultiplier, emit: true, direction: direction, skipReason: null,
            entry: entry, stop: stop, tp: tp,
            slPct: slPct, tpPct: tpPct,
            maxHold: options.MaxHoldMinutes);

        return new CompositeSignalDecision(
            Emit: true,
            Direction: direction,
            TotalScore: totalForDir,
            RequiredScore: options.RequiredScore,
            SkipReason: null,
            EntryPrice: entry,
            StopPrice: stop,
            TakeProfitPrice: tp,
            MaxHoldMinutes: options.MaxHoldMinutes,
            ContextJson: ctxEmit);
    }

    private static bool IsHardGateName(string name) =>
        string.Equals(name, "volume_surge_gate", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "spread_guard_gate", StringComparison.OrdinalIgnoreCase);

    private static decimal ResolveWeight(string name, PatternComposerOptions options)
    {
        if (options.WeightOverrides.TryGetValue(name, out var w))
        {
            return w;
        }
        if (IsHardGateName(name) || string.Equals(name, AdxFilterName, StringComparison.OrdinalIgnoreCase))
        {
            return 0m;
        }
        return name switch
        {
            // Long-bias defaults (spec §2 sabitleri)
            "ema_squeeze_break" => 3m,
            "vwap_bounce" => 2m,
            "inside_bar_breakout" => 3m,
            "rsi_oversold_recovery" => 2m,
            "volume_spike_donchian" => 4m,
            "higher_low_ema_touch" => 2m,
            "macd_zero_cross" => 2m,
            "bullish_engulfing" => 2m,
            "hammer_reversal" => 2m,
            "bollinger_lower_reversal" => 2m,
            // Loop 92 — Short-bias defaults
            "bearish_engulfing" => 2m,
            "shooting_star" => 2m,
            "bollinger_upper_reversal" => 1.5m,
            "bollinger_squeeze_break_down" => 1.5m,
            "rsi_overbought_pullback" => 1.5m,
            "ema9_slope_down" => 1m,
            "donchian_breakdown" => 1.5m,
            _ => 1m,
        };
    }

    private static string SerializeContext(
        BarSnapshot snapshot,
        decimal longTotal,
        decimal shortTotal,
        decimal requiredScore,
        IReadOnlyList<DetectorContrib> contribs,
        decimal softMultiplier,
        bool emit,
        TradeDirection? direction,
        string? skipReason,
        decimal? entry = null,
        decimal? stop = null,
        decimal? tp = null,
        decimal? slPct = null,
        decimal? tpPct = null,
        int? maxHold = null)
    {
        var payload = new
        {
            type = "pattern-composite",
            emit,
            direction = direction?.ToString(),
            skipReason,
            longTotal,
            shortTotal,
            requiredScore,
            softMultiplier,
            patterns = contribs,
            close = snapshot.Close,
            atr14 = snapshot.Atr14,
            adx14 = snapshot.Adx14,
            bbw = snapshot.BollingerBandWidth,
            spreadPct = snapshot.SpreadPct,
            geometry = new
            {
                entry,
                stop,
                tp,
                slPct,
                tpPct,
                maxHold,
            },
            barOpenTime = snapshot.BarOpenTime,
        };
        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = false });
    }

    private sealed record DetectorContrib(
        string Name,
        decimal Score,
        decimal Weight,
        decimal SoftMultiplier,
        decimal Contribution,
        string? Direction,
        string? Reason);
}
