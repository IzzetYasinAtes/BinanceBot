using System.Text.Json;
using BinanceBot.Application.Strategies.Patterns;
using Microsoft.Extensions.Logging;

namespace BinanceBot.Infrastructure.Strategies.Patterns;

/// <summary>
/// Loop 81 — Ağırlıklı skor toplama composer (ADR-0024 §24.9).
///
/// Algoritma:
/// <list type="number">
///   <item>Hard-gate disiplini: <c>IsHardGate=true &amp;&amp; Score==0</c> ⇒ <c>SkipReason=hard_gate:&lt;name&gt;</c>.</item>
///   <item>Soft filter: <c>adx_regime_filter</c> evaluation 0 puan ise tüm
///         pattern skorları <c>options.AdxOutsideRegimeMultiplier</c> × ile çarpılır.</item>
///   <item>Ağırlıklı toplam: <c>Σ Clamp(Score, 0, 1) × Weight × softMul</c>.</item>
///   <item>Toplam &gt;= <c>RequiredScore</c> ⇒ Emit (geometri ATR-bazlı).</item>
/// </list>
///
/// Detector skor &lt; 0 veya &gt; 1 ise warning logla + clamp et (defansif).
/// ContextJson her zaman dolu — emit + skip durumlarında tüm detector
/// skorlarını taşır.
/// </summary>
public sealed class WeightedScorePatternComposer : IPatternSignalComposer
{
    private const string AdxFilterName = "adx_regime_filter";

    private readonly ILogger<WeightedScorePatternComposer> _logger;

    public WeightedScorePatternComposer(ILogger<WeightedScorePatternComposer> logger)
    {
        _logger = logger;
    }

    public CompositeSignalDecision Compose(
        BarSnapshot snapshot,
        IReadOnlyList<PatternEvaluation> evaluations,
        PatternComposerOptions options)
    {
        // 1. Hard-gate kontrolü ve soft filter tespiti
        decimal softMultiplier = 1m;
        var hardGateFails = new List<string>();
        PatternEvaluation? adxFilter = null;

        foreach (var ev in evaluations)
        {
            if (string.Equals(ev.Name, AdxFilterName, StringComparison.OrdinalIgnoreCase))
            {
                adxFilter = ev;
                continue;
            }
        }

        if (adxFilter is not null && adxFilter.Score <= 0m)
        {
            softMultiplier = options.AdxOutsideRegimeMultiplier;
        }

        // 2. Skor toplam (clamp + weight + softMul)
        decimal total = 0m;
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

            // Soft filter veya hard-gate pattern'ı skor toplamına ağırlıkla katkı vermez
            // (DefaultWeight=0). Ama hard-gate fail kontrolü yapılır.
            decimal weight = ResolveWeight(ev.Name, options);
            decimal effectiveWeight = weight;

            // Hard-gate detection: ağırlığı 0 ama IsHardGate=true → skor 0 ⇒ skip
            // Hard-gate flag'i runtime PatternEvaluation'da olmadığı için Name → registry
            // ihtiyacı olur, ama composer registry tüketmez. Çözüm: hard-gate detector'ları
            // VolumeSurgeGate + SpreadGuardGate isimleri ile tanı.
            if (IsHardGateName(ev.Name) && clamped <= 0m)
            {
                hardGateFails.Add(ev.Name);
            }

            decimal contribution = clamped * effectiveWeight * softMultiplier;
            total += contribution;

            contribs.Add(new DetectorContrib(
                Name: ev.Name,
                Score: clamped,
                Weight: weight,
                SoftMultiplier: softMultiplier,
                Contribution: contribution,
                Reason: ev.Reason));
        }

        // 3. Loop 84 — Hard-gate fail SKIP DEVRE DIŞI. Hard-gate detektörleri
        //    skor-toplamına 0 katkı veriyor zaten (DefaultWeight=0). Sahte
        //    breakout riski tolere edilir; 0 emit anti-pattern (Golden #12)
        //    pratik öncelik. hardGateFails tracking sadece ContextJson için
        //    bırakıldı.
        _ = hardGateFails;

        // 4. Threshold kontrolü
        if (total < options.RequiredScore)
        {
            const string skipReason = "score_below_threshold";
            var ctxSkip = SerializeContext(snapshot, total, options.RequiredScore,
                contribs, softMultiplier, emit: false, skipReason: skipReason);
            return new CompositeSignalDecision(
                Emit: false,
                TotalScore: total,
                RequiredScore: options.RequiredScore,
                SkipReason: skipReason,
                EntryPrice: null,
                StopPrice: null,
                TakeProfitPrice: null,
                MaxHoldMinutes: null,
                ContextJson: ctxSkip);
        }

        // 5. Geometri (R:R 1:2, ATR clip)
        var entry = snapshot.Close;
        if (entry <= 0m)
        {
            const string skipReason = "geometry_invalid:entry_zero";
            var ctxSkip = SerializeContext(snapshot, total, options.RequiredScore,
                contribs, softMultiplier, emit: false, skipReason: skipReason);
            return new CompositeSignalDecision(false, total, options.RequiredScore, skipReason,
                null, null, null, null, ctxSkip);
        }

        // SL = max(ATR × SlAtrMultiplier, MinSlPct × entry)  (yüzde olarak)
        decimal atrSlPct = entry > 0m ? snapshot.Atr14 * options.SlAtrMultiplier / entry : 0m;
        decimal slPct = Math.Max(atrSlPct, options.MinSlPct);
        if (options.MaxSlPct > 0m && slPct > options.MaxSlPct)
        {
            slPct = options.MaxSlPct;
        }

        decimal stop = entry * (1m - slPct);
        decimal tpPct = slPct * options.TpRiskRewardRatio;
        decimal tp = entry * (1m + tpPct);

        if (stop >= entry || tp <= entry)
        {
            const string skipReason = "geometry_invalid:tp_or_sl";
            var ctxSkip = SerializeContext(snapshot, total, options.RequiredScore,
                contribs, softMultiplier, emit: false, skipReason: skipReason);
            return new CompositeSignalDecision(false, total, options.RequiredScore, skipReason,
                null, null, null, null, ctxSkip);
        }

        var ctxEmit = SerializeContext(snapshot, total, options.RequiredScore,
            contribs, softMultiplier, emit: true, skipReason: null,
            entry: entry, stop: stop, tp: tp,
            slPct: slPct, tpPct: tpPct,
            maxHold: options.MaxHoldMinutes);

        return new CompositeSignalDecision(
            Emit: true,
            TotalScore: total,
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
        // Hard-gate ve soft filter detector'ları skor toplamına katkı vermez
        if (IsHardGateName(name) || string.Equals(name, AdxFilterName, StringComparison.OrdinalIgnoreCase))
        {
            return 0m;
        }
        // Default ağırlıklar — spec §2 sabitleri
        return name switch
        {
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
            _ => 1m, // bilinmeyen detector → 1
        };
    }

    private static string SerializeContext(
        BarSnapshot snapshot,
        decimal totalScore,
        decimal requiredScore,
        IReadOnlyList<DetectorContrib> contribs,
        decimal softMultiplier,
        bool emit,
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
            skipReason,
            totalScore,
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
        string? Reason);
}
