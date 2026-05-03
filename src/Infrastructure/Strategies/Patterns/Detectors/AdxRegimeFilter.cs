using BinanceBot.Application.Strategies.Patterns;
using BinanceBot.Domain.Common;

namespace BinanceBot.Infrastructure.Strategies.Patterns.Detectors;

/// <summary>
/// Loop 81 — S1 AdxRegimeFilter (spec §2 soft filter). Hard-gate DEĞİL —
/// composer ADX 15-35 dışındaysa tüm skorları × 0.7 ile çarpar (Loop 80'in
/// "no-man's-land" 0-emit sorununu engellemek için soft).
///
/// Bu detector kendisi skor üretmez ama composer'ın regime karar mekanizması
/// için ayrı bir <c>IPatternDetector</c> instance'ı olarak kayıtlıdır;
/// composer Name == "adx_regime_filter" olan evaluation'ı tanır ve skor
/// çarpanı uygular.
///
/// <c>Score</c> semantiği:
/// <list type="bullet">
///   <item>1.0 ⇒ regime içinde (ADX 15-35) — soft multiplier yok.</item>
///   <item>0.0 ⇒ regime dışında — composer tüm pattern skorlarını × 0.7.</item>
/// </list>
/// </summary>
public sealed class AdxRegimeFilter : IPatternDetector
{
    public string Name => "adx_regime_filter";
    public decimal DefaultWeight => 0m;
    public bool IsHardGate => false;
    public TradeDirection? Direction => null;  // Neutral

    private const decimal AdxMin = 15m;
    private const decimal AdxMax = 35m;

    public PatternEvaluation Evaluate(BarSnapshot snapshot)
    {
        // ADX warmup yetersiz (=0) ⇒ "regime içinde" varsay (defansif, frekansı koru)
        if (snapshot.Adx14 <= 0m)
        {
            return new PatternEvaluation(Name, 1m,
                Reason: "adx_warmup_bypass",
                Payload: new { adx = snapshot.Adx14 });
        }

        var inRegime = snapshot.Adx14 >= AdxMin && snapshot.Adx14 <= AdxMax;
        return new PatternEvaluation(Name,
            Score: inRegime ? 1m : 0m,
            Reason: inRegime ? "adx_in_regime" : "adx_out_of_regime",
            Payload: new { adx = snapshot.Adx14, min = AdxMin, max = AdxMax });
    }
}
