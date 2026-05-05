namespace BinanceBot.Infrastructure.Positions;

/// <summary>
/// Loop 109 — defansif "safety net" eşikleri. Birincil exit yolakları
/// (<c>StopLossMonitorService</c>, <c>TakeProfitMonitorService</c>,
/// <c>MarkToMarketWorker</c> trailing/liquidation) bir nedenle (signal ContextJson
/// eksik, BookTicker stale, race) tetiklenmediğinde <see cref="MarkToMarketWorker"/>
/// içindeki ikinci hat kapanış akışına başvurur.
///
/// Bu opsiyonlar primary monitor'ların yerini almaz — tetiklenme sırası:
/// 1) StopLossMonitorService (SL hit), 2) TakeProfitMonitorService (TP hit),
/// 3) StopLossMonitorService time-stop (MaxHoldDuration set ise),
/// 4) MarkToMarketWorker safety net (bu options) — primary kayıp/eksikse.
///
/// <see cref="Application.Positions.Commands.ClosePosition.CloseSignalPositionCommand"/>
/// idempotenttir (NotFound döner pos kapalıysa); çift dispatch zararsız.
/// </summary>
public sealed class PositionSafetyOptions
{
    public const string SectionName = "PositionSafety";

    /// <summary>
    /// Master switch. <c>false</c> ise <see cref="MarkToMarketWorker"/> safety
    /// net dalını tamamen atlar (regression isolation için).
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Stop-loss hit redundancy. <c>StopPrice</c> set ise her tick mark fiyatı
    /// karşılaştırılır:
    /// <list type="bullet">
    ///   <item>Long: <c>mark &lt;= StopPrice</c> → exit.</item>
    ///   <item>Short: <c>mark &gt;= StopPrice</c> → exit.</item>
    /// </list>
    /// Default <c>true</c> — primary <c>StopLossMonitorService</c> ile aynı
    /// koşulu kontrol eder, ikinci hat olarak. Çift trigger zararsız (NotFound).
    /// </summary>
    public bool StopLossRedundancyEnabled { get; set; } = true;

    /// <summary>
    /// Take-profit hit redundancy. <c>TakeProfit</c> set ise:
    /// <list type="bullet">
    ///   <item>Long: <c>mark &gt;= TakeProfit</c> → exit.</item>
    ///   <item>Short: <c>mark &lt;= TakeProfit</c> → exit.</item>
    /// </list>
    /// Default <c>true</c>.
    /// </summary>
    public bool TakeProfitRedundancyEnabled { get; set; } = true;

    /// <summary>
    /// Hard pozisyon yaş tavanı (dakika). <c>Position.MaxHoldDuration</c> null
    /// olsa bile (örn. <c>OrderFilledPositionHandler</c> signal ContextJson 5dk
    /// freshness penceresi dışında doldu) bu değer eski pozisyonları zorla
    /// kapatır. Default 120 dk = strateji <c>MaxHoldMinutes=60</c>'ın 2x'i,
    /// büyük güvenlik payı. <c>0</c> veya negatif → devre dışı.
    /// </summary>
    public int HardMaxHoldMinutes { get; set; } = 120;
}
