namespace BinanceBot.Infrastructure.Trading.Paper;

/// <summary>
/// ADR-0018 §18.12 — paper-mode commission simulation. Binance testnet environment
/// returns <c>commission = 0</c> on fill responses (bkz. dev.binance.vision topic
/// 16810), ama mainnet spot VIP 0 oranları <c>0.10%</c> taker, <c>0.075%</c>
/// BNB-discount taker şeklinde fatura olur. Testnet'in gerçekliği gizlemesini
/// önlemek için paper-fill pipeline <see cref="PaperFillSimulator"/> bu saf static
/// helper'dan simulated commission alır ve hem <c>OrderFill.Commission</c>'a
/// yazar hem de <c>VirtualBalance</c>'tan kesinti yapar (round-trip open + close).
///
/// Static tutulma gerekçesi (ADR-0018 §18.12 + Alt-G reddi):
///   - Domain invariantı değil — sadece paper mode'a özgü persistence yan etkisi.
///   - 2 satırlık deterministik hesap; mock'lanması gerekmeyen saf fonksiyon.
///   - VIP tier per-user farklılaşırsa <c>IPaperFeeCalculator</c> abstraction'ı
///     geriye uyumlu eklenebilir (YAGNI şimdilik).
/// </summary>
internal static class PaperFeeSimulator
{
    /// <summary>Spot VIP 0 taker fee oranı (research §2.1 Binance fee schedule).</summary>
    public const decimal NormalFeeRate = 0.001m; // %0.10

    /// <summary>Spot VIP 0 BNB-discount taker fee oranı (research §2.1).</summary>
    public const decimal BnbDiscountFeeRate = 0.00075m; // %0.075

    /// <summary>
    /// ADR-0018 §18.12 — fill notional'ına uygulanan commission. Negatif notional
    /// sayısı anlamsız (round-trip açık/kapanış her ikisi de pozitif notional),
    /// dolayısıyla <c>notional &lt;= 0</c> halinde <c>0</c> döner (sessiz guard,
    /// exception for control flow yasak — ardalis/Result pattern ile tutarlı).
    /// </summary>
    public static decimal CalculateCommission(decimal notional, bool bnbDiscount)
    {
        if (notional <= 0m)
        {
            return 0m;
        }
        var rate = bnbDiscount ? BnbDiscountFeeRate : NormalFeeRate;
        return notional * rate;
    }
}
