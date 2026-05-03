namespace BinanceBot.Application.Strategies.Patterns;

/// <summary>
/// Loop 81 — <see cref="IPatternSignalComposer"/> için runtime parametreler
/// (ADR-0024 §24.14). Strategy ParametersJson'unda serialize edilir; per-coin
/// override mümkün (özellikle <see cref="WeightOverrides"/>).
///
/// Default değerler spec-synthesized §3'e göre:
/// <list type="bullet">
///   <item>SL: max(ATR14 × 1.2, 0.6%) → <c>SlAtrMultiplier=1.2</c>, <c>MinSlPct=0.006</c>.</item>
///   <item>TP: SL × 2.0 (R:R 1:2) → <c>TpRiskRewardRatio=2.0m</c>.</item>
///   <item>MaxHold 60dk (12 bar @ 5m).</item>
///   <item>Composer threshold: <c>RequiredScore=5</c> (skor tavanı 24, agresif emit).</item>
/// </list>
/// </summary>
public sealed class PatternComposerOptions
{
    /// <summary>Emit için minimum ağırlıklı toplam skor. Default 5 (spec).</summary>
    public decimal RequiredScore { get; set; } = 5m;

    /// <summary>
    /// Per-detector ağırlık override (key = detector.Name snake_case). Boşsa
    /// her detector'ın <see cref="IPatternDetector.DefaultWeight"/> kullanılır.
    /// </summary>
    public Dictionary<string, decimal> WeightOverrides { get; set; } = new();

    // ── Geometri ──────────────────────────────────────────────────────────

    /// <summary>SL hesabı için ATR çarpanı. Final SL = max(ATR × <c>SlAtrMultiplier</c>, <c>MinSlPct</c>).</summary>
    public decimal SlAtrMultiplier { get; set; } = 1.2m;

    /// <summary>SL alt sınırı (yüzde). spec §3 default 0.006 (%0.6).</summary>
    public decimal MinSlPct { get; set; } = 0.006m;

    /// <summary>SL üst sınırı (yüzde). Default 0.012 (%1.2 — anti-disaster).</summary>
    public decimal MaxSlPct { get; set; } = 0.012m;

    /// <summary>R:R oranı. TP = entry + (entry-SL) × <c>TpRiskRewardRatio</c>. Default 2.0.</summary>
    public decimal TpRiskRewardRatio { get; set; } = 2.0m;

    /// <summary>Pozisyonun maksimum hold süresi dakika. Default 60dk (12 bar × 5m).</summary>
    public int MaxHoldMinutes { get; set; } = 60;

    // ── Cooldown ──────────────────────────────────────────────────────────

    /// <summary>Sinyal sonrası kaç bar cooldown. Default 2 bar (10dk @ 5m).</summary>
    public int CooldownBarsAfterSignal { get; set; } = 2;

    // ── Soft Filter (ADX) ─────────────────────────────────────────────────

    /// <summary>ADX rejim alt sınırı (dahil). Default 15.</summary>
    public decimal AdxRegimeMin { get; set; } = 15m;

    /// <summary>ADX rejim üst sınırı (dahil). Default 35.</summary>
    public decimal AdxRegimeMax { get; set; } = 35m;

    /// <summary>ADX regime dışıysa skor çarpanı. Default 0.7 (skor düşürür, skip yapmaz).</summary>
    public decimal AdxOutsideRegimeMultiplier { get; set; } = 0.7m;

    // ── Break-even / Trailing (audit + future use) ────────────────────────

    /// <summary>BE move tetik UPnL eşiği. Default 0.001 (%0.1).</summary>
    public decimal BeMoveTriggerPct { get; set; } = 0.001m;

    /// <summary>BE move offset. Default 0.0002.</summary>
    public decimal BeMoveOffsetPct { get; set; } = 0.0002m;

    // ── Loop 87: Multi-timeframe + RSI cap gate'leri ──────────────────────

    /// <summary>
    /// Loop 87 — RSI14 üst sınırı (dahil). Snapshot.Rsi14 bu değerin üstündeyse
    /// emit edilmez (skip="rsi_overbought"). Aşırı alım breakout'u eler.
    /// Default 75. RSI 75+ breakout'lar L85/L86 post-mortem'inde yüksek
    /// revert oranı gösterdi.
    /// </summary>
    public decimal RsiMaxEmit { get; set; } = 75m;
}
