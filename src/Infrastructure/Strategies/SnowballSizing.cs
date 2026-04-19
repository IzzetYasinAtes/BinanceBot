namespace BinanceBot.Infrastructure.Strategies;

/// <summary>
/// ADR-0015 §15.4 + ADR-0018 §18.10. Pure helper for the "snowball" minimum
/// notional rule — each entry risks at most <c>max(equity × 1%, $5.10)</c>.
/// The fan-out handler forwards the result as <c>MinNotional</c> into the
/// sizing service so the cap/risk branches clamp against it.
///
/// Loop 23 ADR-0018 — micro-scalping %1 sizing, Binance <c>minNotional = $5.00</c>
/// hard requirement + <c>$0.10</c> precision/slippage/fee buffer. $1 user intent
/// rejected by Binance API (-1013 MIN_NOTIONAL). Floor drops from $20 → $5.10
/// and equity fraction from 20% → 1% so 150 trade/saat hedefinin alt-end bacağı
/// paper ve live testnet için fizibil olur.
///
/// Kartopu eşikleri (ADR-0018 §18.10):
///   equity  $100  → 0.01 × 100 = 1.00   → floor $5.10 devrede
///   equity  $510  → 0.01 × 510 = 5.10   → geçiş noktası, kartopu başlıyor
///   equity $1000  → 10.00
///   equity $5000  → 50.00
///
/// Split out for unit-test reach — <c>StrategySignalToOrderHandler</c> is an
/// integration-style orchestrator; the invariant itself is a one-liner.
/// </summary>
internal static class SnowballSizing
{
    /// <summary>
    /// Fixed floor in USDT — ADR-0018 §18.10 Binance minNotional $5.00 + precision buffer $0.10.
    /// </summary>
    public const decimal FloorUsd = 5.10m;

    /// <summary>
    /// User-chosen equity fraction (1%) — ADR-0018 §18.10 micro-scalping reform.
    /// Eski %20 Loop 23 öncesi hedefiydi; 150 trade/saat için %1 + $5.10 floor'a indirildi.
    /// </summary>
    public const decimal EquityFraction = 0.01m;

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
