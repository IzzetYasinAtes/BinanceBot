namespace BinanceBot.Infrastructure.Trading;

/// <summary>
/// Loop 107 / ADR-0026 §A — Pullback Limit Order ayarları. Bar close anında
/// market emit yerine bar_close × (1 - <see cref="OffsetPct"/>) (Long) /
/// × (1 + <see cref="OffsetPct"/>) (Short) fiyatında GTC LIMIT emir oluşturulur.
/// <see cref="TimeoutMinutes"/> dolduğunda <c>PendingLimitTimeoutWorker</c>
/// emir'i <see cref="BinanceBot.Domain.Orders.Order.Expire"/> ile
/// <c>OrderStatus.Expired</c>'a geçirir + LiveTestnet'te
/// <c>IExchangeClient.CancelLiveOrderAsync</c> çağrılır.
///
/// 26 loop -$26.5+ "buy at top" pattern (bar close zirvede market entry, peak
/// reversal -$0.6 SL hit) çözümü. Limit @ pullback fiyatı entry'yi tepe yerine
/// geri çekilme noktasına iter.
/// </summary>
public sealed class PullbackLimitOptions
{
    public const string SectionName = "PullbackLimit";

    /// <summary>Feature toggle. <c>false</c> ise eski Market emir akışı korunur.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Pullback offset (yüzde). Default 0.001 = %0.10. Long için
    /// limit = bar_close × (1 - 0.001), Short için limit = bar_close × (1 + 0.001).
    /// binance-expert spec §2.A: %0.07-0.12 önerir, başlangıç %0.10.
    /// </summary>
    public decimal OffsetPct { get; set; } = 0.001m;

    /// <summary>
    /// Pending limit timeout (dakika). Default 5. Bu süre içinde fill olmayan
    /// emir Expire edilir. binance-expert spec §2.A: 2-10dk öneri, ortada 5dk.
    /// </summary>
    public int TimeoutMinutes { get; set; } = 5;
}
