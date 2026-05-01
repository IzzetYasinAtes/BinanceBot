namespace BinanceBot.Infrastructure.Positions;

/// <summary>
/// Loop 76 — trailing-stop global toggles. Bound from <c>appsettings.json</c>
/// "TrailingStop" section. BE move'la zincirli: BE bir kez uygulanır →
/// trailing aktif olur → peak refresh + drop'ta exit dispatch.
///
/// Per-strateji override yok (MVP, BreakEvenOptions ile aynı pattern).
/// 5 KMS strateji aynı default'u paylaşır. Strateji-spesifik override
/// gerekirse Position aggregate'a parametre push edilir (Loop 77+).
/// </summary>
public sealed class TrailingStopOptions
{
    public const string SectionName = "TrailingStop";

    /// <summary>
    /// Master switch. <c>false</c> ise <see cref="MarkToMarketWorker"/>
    /// trailing dalını tamamen atlar (regression isolation için).
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Trail genişliği — <c>trailingStop = peak × (1 − TrailPct)</c>.
    /// Default 0.0015 (=%0.15 — KMS spread + fee + 1×ATR/2 buffer dengesi,
    /// binance-expert Loop 76 quick-win sabitı). Çok dar = whipsaw exit;
    /// çok geniş = peak'ten uzun bleed sonra exit. 0.0015 muhafazakar
    /// başlangıç; Loop 77+ ATR-bazlı dinamik genişlik için yer açık.
    /// </summary>
    public decimal TrailPct { get; set; } = 0.0015m;
}
