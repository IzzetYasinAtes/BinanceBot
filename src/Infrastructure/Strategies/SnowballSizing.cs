namespace BinanceBot.Infrastructure.Strategies;

/// <summary>
/// ADR-0015 §15.4 + ADR-0018 §18.10 superseded by ADR-0021. Pure helper for the
/// "snowball" minimum notional rule — each entry risks at most
/// <c>max(equity × 20%, $20.10)</c>. The fan-out handler forwards the result as
/// <c>MinNotional</c> into the sizing service so the cap/risk branches clamp
/// against it.
///
/// ADR-0018 → ADR-0021 revize: %1 + $5.10 → %20 + $20.10, Loop 32 fee drag
/// analizi sonrası. Kullanıcı direktifi (2026-04-22): "$5.10 çok az, artır".
/// Önceki formül (%1 + $5.10) Loop 29-32 boyunca net per-win $0.0054 üretti;
/// $0.01 round-trip fee'nin %60'ı gross kârı yedi — saatlik kâr $0.025
/// mertebesinde noise floor altında kaldı. %20 sizing + $20.10 floor net
/// per-win'i $0.020'ye (3.7×) çıkarır, fee drag absolute dolar cinsinden
/// görünür kalır. $100 starting balance **SABİT** (CLAUDE.md altın kural).
///
/// Kartopu eşikleri (ADR-0021 §21.6):
///   equity  $100  → 0.20 × 100 = 20.00  → floor $20.10 devrede
///   equity  $101  → 0.20 × 101 = 20.20  → pct branch başlar
///   equity  $500  → 100.00              → kartopu tam etkili
///   equity $1000  → 200.00              → oransal büyüme
///
/// Concurrent exposure: 3 × $20.10 = $60.30 (MaxOpenPositions = 3, ADR-0021
/// §21.7). 4 coin seed'in 1'i sinyal kuyruğunda bekler.
///
/// Split out for unit-test reach — <c>StrategySignalToOrderHandler</c> is an
/// integration-style orchestrator; the invariant itself is a one-liner.
/// </summary>
internal static class SnowballSizing
{
    /// <summary>
    /// Fixed floor in USDT — ADR-0021 §21.6 user-intent floor $20 + precision buffer $0.10.
    /// Binance minNotional $5.00 hard filter otomatik geçilir (floor 4× üstünde).
    /// </summary>
    public const decimal FloorUsd = 20.10m;

    /// <summary>
    /// User-chosen equity fraction (20%) — ADR-0021 §21.6 fee-drag rescue.
    /// ADR-0018 %1 değeri Loop 32 matematik analizinde yetersiz bulundu
    /// (net per-win $0.0054, gross %60 fee drag). $100 × 20% = $20 target,
    /// floor $20.10 marjinal devrede.
    /// </summary>
    public const decimal EquityFraction = 0.20m;

    /// <summary>
    /// Returns the user-level minimum notional for the current equity snapshot.
    /// Non-positive equity collapses to the <see cref="FloorUsd"/> floor —
    /// callers still apply the exchange NOTIONAL filter on top.
    /// </summary>
    public static decimal CalcMinNotional(decimal equity)
    {
        if (equity <= 0m)
        {
            return FloorUsd;
        }
        var pct = equity * EquityFraction;
        return pct >= FloorUsd ? pct : FloorUsd;
    }
}
