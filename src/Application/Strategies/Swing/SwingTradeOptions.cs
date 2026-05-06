namespace BinanceBot.Application.Strategies.Swing;

/// <summary>
/// Loop 112 — ADR-0027 Aile A. SwingTrade evaluator parametre seti.
/// <c>Strategy.ParametersJson</c>'dan deserialize edilir; default değerler
/// binance-expert §7 spec'inden gelir (4h MTF swing trading).
///
/// <para>
/// SOLID: tek-sorumluluk parametre taşıyıcı, immutable record. Evaluator
/// pure function olarak okur; aggregate iş kuralı bu DTO'ya değil
/// <see cref="BinanceBot.Domain.Strategies.Strategy"/> aggregate'ine ait.
/// </para>
/// </summary>
/// <param name="EmaShortPeriod">Hızlı trend EMA — default 20.</param>
/// <param name="EmaLongPeriod">Yavaş trend EMA — default 50. Long entry:
/// EmaShort &gt; EmaLong; Short entry: EmaShort &lt; EmaLong.</param>
/// <param name="VolumeSmaPeriod">Volume SMA penceresi — default 20.</param>
/// <param name="VolumeSurgeMultiplier">Volume(bar) &gt; VolumeSma × N
/// — default 1.5 (bar volume %50 fazla = momentum onayı).</param>
/// <param name="RsiPeriod">RSI Wilder periyodu — default 14.</param>
/// <param name="RsiLongMin">Long entry RSI alt sınırı — default 40.</param>
/// <param name="RsiLongMax">Long entry RSI üst sınırı — default 65.
/// 65 üstü "buy at top" riski (binance-expert spec).</param>
/// <param name="RsiShortMin">Short entry RSI alt sınırı — default 35.
/// 35 altı "sell at bottom" riski.</param>
/// <param name="RsiShortMax">Short entry RSI üst sınırı — default 60.</param>
/// <param name="AtrPeriod">ATR Wilder periyodu — default 14.</param>
/// <param name="SlAtrMultiplier">SL = Entry ± ATR × N. Long: entry - ATR × 1.5;
/// Short: entry + ATR × 1.5. Default 1.5.</param>
/// <param name="TpAtrMultiplier">TP = Entry ± ATR × N. R:R 1:2 ⇒ default 3.0.</param>
/// <param name="MaxHoldHours">Time-exit threshold (8h = 2 × 4h bar). Default 8.</param>
/// <param name="BeMoveTriggerPct">Pos UPnl ≥ entry × (1 + N) ⇒ BE stop.
/// SwingTrade için %1 default (PatternComposite %0.10'dan farklı). Aile-spesifik
/// — global <c>BreakEven.TriggerPct</c> ile karışmaz; evaluator pos açarken
/// signal ContextJson'a yazar, MarkToMarketWorker default'u kullanır
/// (Loop 112'de PatternComposite paused olduğu için global default Paper-only
/// safe; ileride per-strategy BE config gerekir).</param>
/// <param name="BeMoveOffsetPct">BE stop entry × (1 + N) (Long) konumu.
/// Default %0.10 buffer (BE üstü).</param>
/// <param name="TimeExitMinProfitPct">Time-exit kapanışı için minimum kar.
/// 8h hold + UPnl ≥ entry × (1 + N) ⇒ close. Default %0.50.</param>
/// <param name="CooldownBarsAfterSignal">Signal sonrası cooldown bar sayısı.
/// 4h timeframe için 1 bar = 4h, default 1 (her 4h bar başına bir emit).</param>
public sealed record SwingTradeOptions(
    int EmaShortPeriod = 20,
    int EmaLongPeriod = 50,
    int VolumeSmaPeriod = 20,
    decimal VolumeSurgeMultiplier = 1.5m,
    int RsiPeriod = 14,
    decimal RsiLongMin = 40m,
    decimal RsiLongMax = 65m,
    decimal RsiShortMin = 35m,
    decimal RsiShortMax = 60m,
    int AtrPeriod = 14,
    decimal SlAtrMultiplier = 1.5m,
    decimal TpAtrMultiplier = 3.0m,
    int MaxHoldHours = 8,
    decimal BeMoveTriggerPct = 0.01m,
    decimal BeMoveOffsetPct = 0.001m,
    decimal TimeExitMinProfitPct = 0.005m,
    int CooldownBarsAfterSignal = 1)
{
    public SwingTradeOptions() : this(
        EmaShortPeriod: 20,
        EmaLongPeriod: 50,
        VolumeSmaPeriod: 20,
        VolumeSurgeMultiplier: 1.5m,
        RsiPeriod: 14,
        RsiLongMin: 40m,
        RsiLongMax: 65m,
        RsiShortMin: 35m,
        RsiShortMax: 60m,
        AtrPeriod: 14,
        SlAtrMultiplier: 1.5m,
        TpAtrMultiplier: 3.0m,
        MaxHoldHours: 8,
        BeMoveTriggerPct: 0.01m,
        BeMoveOffsetPct: 0.001m,
        TimeExitMinProfitPct: 0.005m,
        CooldownBarsAfterSignal: 1)
    { }
}
