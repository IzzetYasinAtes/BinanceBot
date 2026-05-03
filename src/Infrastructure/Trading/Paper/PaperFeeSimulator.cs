namespace BinanceBot.Infrastructure.Trading.Paper;

/// <summary>
/// Loop 92 — Futures taker fee simulator (ADR-0025). Spot %0.10 (NormalFeeRate)
/// kalıntısı silindi; sadece USDT-M Futures VIP0 oranları:
/// <list type="bullet">
///   <item>Maker: %0.02 (0.0002).</item>
///   <item>Taker: %0.05 (0.0005). Bot tüm market emirleri taker tetikler.</item>
///   <item>BNB ile ödeme: %10 indirim → %0.045 (0.00045).</item>
/// </list>
///
/// Static tutulma gerekçesi (ADR-0018 §18.12 + Alt-G reddi):
/// 2 satırlık deterministik hesap; mock'lanması gerekmeyen saf fonksiyon.
/// </summary>
internal static class PaperFeeSimulator
{
    /// <summary>USDT-M Futures VIP 0 taker fee oranı (binance-expert spec §13).</summary>
    public const decimal FuturesTakerFeeRate = 0.0005m; // %0.05

    /// <summary>BNB-discount taker fee oranı (%10 indirim).</summary>
    public const decimal FuturesTakerFeeRateBnb = 0.00045m; // %0.045

    /// <summary>USDT-M Futures VIP 0 maker fee oranı (referans, bot maker emir vermez).</summary>
    public const decimal FuturesMakerFeeRate = 0.0002m; // %0.02

    /// <summary>
    /// Loop 92 — fill notional'ına uygulanan taker commission. Negatif notional sayısı
    /// anlamsız → 0 döner (sessiz guard, exception for control flow yasak).
    /// </summary>
    public static decimal CalculateTakerFee(decimal notional, bool bnbDiscount)
    {
        if (notional <= 0m)
        {
            return 0m;
        }
        var rate = bnbDiscount ? FuturesTakerFeeRateBnb : FuturesTakerFeeRate;
        return notional * rate;
    }

    /// <summary>
    /// Geriye uyumlu shim — eski Spot kodu <c>CalculateCommission</c> diyordu.
    /// Loop 92 itibarıyla taker = quote-asset (USDT) fee.
    /// </summary>
    public static decimal CalculateCommission(decimal notional, bool bnbDiscount)
        => CalculateTakerFee(notional, bnbDiscount);
}
