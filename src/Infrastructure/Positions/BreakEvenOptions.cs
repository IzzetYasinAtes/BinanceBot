namespace BinanceBot.Infrastructure.Positions;

/// <summary>
/// Loop 75 — break-even SL move global toggles + thresholds. Bound from
/// <c>appsettings.json</c> "BreakEven" section. Per-strategy override yok
/// (MVP); 5 KMS strateji aynı default'u paylaşır. Strateji-spesifik
/// override gerekirse Loop 76+ Position aggregate'a parametre push edilir.
/// </summary>
public sealed class BreakEvenOptions
{
    public const string SectionName = "BreakEven";

    /// <summary>
    /// Master switch. <c>false</c> ise <see cref="MarkToMarketWorker"/>
    /// BE move dalını tamamen atlar (regression isolation için).
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Trigger eşiği — <c>markPrice &gt;= entry × (1 + TriggerPct)</c> olunca
    /// BE move tetiklenir. Default 0.0010 (=%0.10 — fee + slippage karşılayan
    /// ilk meşru kar dilimi).
    /// </summary>
    public decimal TriggerPct { get; set; } = 0.0010m;

    /// <summary>
    /// Yeni stop offset — <c>stopPrice = entry × (1 + OffsetPct)</c>. Default
    /// 0.0002 (=%0.02 — round-trip fee'yi just-cover, "garantili breakeven+ε").
    /// </summary>
    public decimal OffsetPct { get; set; } = 0.0002m;
}
