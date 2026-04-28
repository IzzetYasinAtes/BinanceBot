using BinanceBot.Application.Abstractions;
using BinanceBot.Application.Strategies.Cooldowns;
using BinanceBot.Application.Strategies.Evaluation;
using BinanceBot.Application.Strategies.Indicators;
using BinanceBot.Domain.MarketData;
using BinanceBot.Domain.Strategies;
using Microsoft.Extensions.Logging;

namespace BinanceBot.Infrastructure.Strategies.Evaluators;

/// <summary>
/// Loop 50 AR-GE — Hibrit 1m frekans tetiği + 15m kalite kapısı evaluator.
/// Long-only spot paper.
///
/// AR-GE motivasyonu: Loop 49 EmaScalper1m saf 1m crossover düşük WR
/// teşhisi sonrası iki-katmanlı pivot (binance-expert spec onaylı):
///   - Kalite kapısı (15m): BB lower kapışı (dip yakalama) + RSI oversold
///     yukarı dönüş (momentum tetikleyici).
///   - Frekans tetiği (1m): EMA9/EMA21 crossover + volume surge + ATR aktif.
/// İki katman birden geçtiğinde sinyal — yüksek-frekans 1m bar polled,
/// kalite engeli 15m bar kapanışına bağlı (sinyal sıklığı ~15m bar başına
/// 1-3 koin için 0-1).
///
/// Giriş (<see cref="StrategySignalDirection.Long"/>, kapalı 15m bar +
/// kapalı 1m bar sonrası, yedi koşul AND):
///   1. 15m BB lower: <c>currentClose_15m &lt; BbLower_15m(20, 2σ)</c>.
///   2. 15m RSI yukarı dönüş: <c>Rsi14_15m_curr &lt; RsiOversoldThreshold15m
///      (default 40) AND Rsi14_15m_curr &gt; Rsi14_15m_prev</c>.
///   3. 1m EMA crossover/üstü: <c>Ema9_1m &gt; Ema21_1m</c>
///      (crossover OR sustained — prev koşulu spec'te zorunlu değil; current
///      durum yeter).
///   4. 1m volume surge: <c>currentVolume_1m &gt; VolumeMa20_1m × VolumeMultiplier_1m</c>
///      (default 1.2).
///   5. 1m MinAtr: <c>Atr14_1m / currentClose_1m &gt;= MinAtrPct_1m</c>
///      (default 0.0003).
///   6. 15m bar kapandı: <see cref="HybridMomentum1mIndicatorSnapshot.BarClosed_15m"/>
///      <c>== true</c> (defansif).
///   7. Cooldown: per-coin 3 bar × 1dk = 3dk
///      (<see cref="ICooldownService"/> üzerinden enforce).
///
/// Çıkış (ATR15m-tabanlı adaptif geometri):
///   - Take-Profit: <c>entry × (1 + clamp(Atr14_15m × TpAtrMultiplier / entry,
///     MinTpPct, MaxTpPct))</c>. Default TP %0.40 floor / %1.00 ceiling.
///   - Stop-Loss  : <c>entry × (1 − clamp(Atr14_15m × SlAtrMultiplier / entry,
///     MinSlPct, MaxSlPct))</c>. Default SL %0.20 floor / %0.40 ceiling.
///   - Time-Stop  : <c>MaxHoldMinutes</c> (default 30dk) — ContextJson
///     <c>maxHoldMinutes</c> anahtarına yazılır (ADR-0017 §17.7).
///
/// Geometri TP/SL ATR15m ile ölçeklenir (1m yerine), çünkü kalite kapısı 15m
/// rejim — TP hedefi 15m volatilite boyutunda; 1m crossover yalnız tetik.
/// R:R 1.875:1, BE WR ~%34.8.
///
/// Evaluator hiçbir DB bağlantısı açmaz, rolling buffer yönetmez — tüm
/// primitive verileri <see cref="IMarketIndicatorService.TryGetHybridMomentum1mSnapshot"/>
/// üzerinden alır. Result-flow: skip ⇒ <c>null</c>; signal ⇒
/// <see cref="StrategyEvaluation"/>. Exception kontrol akışı için kullanılmaz —
/// invalid geometry / yetersiz warmup defensive null path.
/// </summary>
public sealed class HybridMomentum1mEvaluator : IStrategyEvaluator
{
    public StrategyType Type => StrategyType.HybridMomentum1m;

    private readonly IMarketIndicatorService _indicators;
    private readonly ICooldownService _cooldown;
    private readonly IClock _clock;
    private readonly ILogger<HybridMomentum1mEvaluator> _logger;

    // Loop 50 strateji 1m bar tetikli — cooldown penceresi 1m bar çarpanıyla
    // yorumlanır (3 bar × 1dk = 3dk per coin). Sabit, parametre dışı.
    private const int BarMinutes1m = 1;

    public HybridMomentum1mEvaluator(
        IMarketIndicatorService indicators,
        ICooldownService cooldown,
        IClock clock,
        ILogger<HybridMomentum1mEvaluator> logger)
    {
        _indicators = indicators;
        _cooldown = cooldown;
        _clock = clock;
        _logger = logger;
    }

    public Task<StrategyEvaluation?> EvaluateAsync(
        long strategyId,
        string parametersJson,
        string symbol,
        IReadOnlyList<Kline> closedBars,
        CancellationToken cancellationToken)
    {
        var p = EvaluatorParameterHelper.TryParse<Parameters>(parametersJson) ?? new Parameters();

        var snapshot = _indicators.TryGetHybridMomentum1mSnapshot(
            symbol,
            emaFastPeriod: p.EmaFastPeriod,
            emaSlowPeriod: p.EmaSlowPeriod,
            volumeWindow_1m: p.VolumeWindow1m,
            atrPeriod_1m: p.AtrPeriod1m,
            bbPeriod_15m: p.BbPeriod15m,
            bbStdMultiplier_15m: p.BbStdMultiplier15m,
            rsiPeriod_15m: p.RsiPeriod15m,
            atrPeriod_15m: p.AtrPeriod15m);

        if (snapshot is null)
        {
            _logger.LogDebug(
                "HybridMomentum1m snapshot not ready symbol={Symbol} strategyId={StrategyId}",
                symbol, strategyId);
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        // Pre-warmup safety — degenerate zero values (flat market / not warm).
        if (snapshot.Ema9_1m <= 0m
            || snapshot.Ema21_1m <= 0m
            || snapshot.BbLower_15m <= 0m
            || snapshot.CurrentClose_1m <= 0m
            || snapshot.CurrentClose_15m <= 0m)
        {
            _logger.LogDebug(
                "HybridMomentum1m snapshot incomplete symbol={Symbol} ema9_1m={Ema9} bbLower_15m={BbLower} close_1m={Close1m} close_15m={Close15m}",
                symbol, snapshot.Ema9_1m, snapshot.BbLower_15m,
                snapshot.CurrentClose_1m, snapshot.CurrentClose_15m);
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        // 6) 15m bar kapandı (defansif).
        if (!snapshot.BarClosed_15m)
        {
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        // 1) 15m BB lower kapışı (kalite kapısı — dip yakalama).
        var bbBreachOk = snapshot.CurrentClose_15m < snapshot.BbLower_15m;

        // 2) 15m RSI yukarı dönüş (oversold + curr > prev — momentum
        //    tetikleyici).
        var rsiOk = snapshot.Rsi14_15m < p.RsiOversoldThreshold15m
            && snapshot.Rsi14_15m > snapshot.Rsi14Prev_15m;

        // 3) 1m EMA crossover/üstü (frekans tetiği).
        var trendOk = snapshot.Ema9_1m > snapshot.Ema21_1m;

        // 4) 1m volume surge. VolumeMa20_1m = 0 ⇒ no-signal (warmup eksik;
        //    snapshot zaten warmup eşiğinde null dönmeli).
        var volumeOk = snapshot.VolumeMa20_1m > 0m
            && snapshot.CurrentVolume_1m > snapshot.VolumeMa20_1m * p.VolumeMultiplier1m;

        // 5) 1m MinAtr filtresi (sessiz piyasa engeli).
        var atrPct_1m = snapshot.CurrentClose_1m > 0m
            ? snapshot.Atr14_1m / snapshot.CurrentClose_1m
            : 0m;
        var atrOk = snapshot.Atr14_1m > 0m && atrPct_1m >= p.MinAtrPct1m;

        if (!bbBreachOk || !rsiOk || !trendOk || !volumeOk || !atrOk)
        {
            _logger.LogDebug(
                "HybridMomentum1m skip symbol={Symbol} close15m={Close15m} bbLower15m={BbLower} " +
                "rsi15m={Rsi} rsi15mPrev={RsiPrev} rsiThr={RsiThr} ema9_1m={Ema9} ema21_1m={Ema21} " +
                "vol1m={Vol} volMa20={VolMa} mult={Mult} atrPct1m={AtrPct} minAtr1m={MinAtr} decision=Skip",
                symbol, snapshot.CurrentClose_15m, snapshot.BbLower_15m,
                snapshot.Rsi14_15m, snapshot.Rsi14Prev_15m, p.RsiOversoldThreshold15m,
                snapshot.Ema9_1m, snapshot.Ema21_1m,
                snapshot.CurrentVolume_1m, snapshot.VolumeMa20_1m, p.VolumeMultiplier1m,
                atrPct_1m, p.MinAtrPct1m);
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        // 7) Cooldown gate — sinyal koşulları sağlandıktan SONRA. Maliyet
        //    sırası kasıtlı: önce ucuz indicator filtreleri, sonra cooldown
        //    (state lookup).
        var now = _clock.UtcNow;
        if (_cooldown.IsCooldown(strategyId, symbol, p.CooldownBarsAfterSignal, BarMinutes1m, now))
        {
            _logger.LogInformation(
                "HybridMomentum1m cooldown skip symbol={Symbol} strategyId={StrategyId} " +
                "cooldownBars={Bars} barMinutes={BarMinutes} decision=CooldownSkip",
                symbol, strategyId, p.CooldownBarsAfterSignal, BarMinutes1m);
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        // Entry referansı 1m current close (frekans tetiği fiyatı). TP/SL
        // ATR15m ile ölçeklenir (kalite kapısı timeframe — 15m rejim
        // potansiyeli sürer).
        var entryPrice = snapshot.CurrentClose_1m;

        // ATR15m-tabanlı TP/SL geometrisi + Min/Max clip. Atr14_15m == 0 ⇒
        // raw* = 0 → clamp() Min'i uygular (Min > 0). Defansif olarak yine de
        // entry * (1+0) durumuna düşmemek için snapshot.Atr14_15m > 0
        // garantisini de eklerdik; ancak BB lower kapışı + RSI oversold
        // koşulları zaten 15m hareket var demektir — Atr14_15m pratikte > 0.
        // MinTpPct/MinSlPct floor güvencesi yeterli.
        var rawTpPct = entryPrice > 0m ? snapshot.Atr14_15m * p.TpAtrMultiplier / entryPrice : 0m;
        var rawSlPct = entryPrice > 0m ? snapshot.Atr14_15m * p.SlAtrMultiplier / entryPrice : 0m;
        var tpPct = Clamp(rawTpPct, p.MinTpPct, p.MaxTpPct);
        var slPct = Clamp(rawSlPct, p.MinSlPct, p.MaxSlPct);

        var takeProfit = entryPrice * (1m + tpPct);
        var stopPrice = entryPrice * (1m - slPct);

        if (takeProfit <= entryPrice || stopPrice >= entryPrice)
        {
            _logger.LogDebug(
                "HybridMomentum1m geometry invalid symbol={Symbol} entry={Entry} stop={Stop} tp={Tp}",
                symbol, entryPrice, stopPrice, takeProfit);
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        // ContextJson — TimeStop handler (ADR-0017 §17.7) `maxHoldMinutes`
        // key şartı korunur. EntryReason "bb_dip+ema_cross+rsi_turn" hibrit
        // kalite + tetik özetini taşır.
        var ctx = EvaluatorParameterHelper.SerializeContext(new
        {
            type = "hybrid-momentum-1m",
            entryReason = "bb_dip+ema_cross+rsi_turn",
            // 15m kalite kapısı alanları
            bbUpper15m = snapshot.BbUpper_15m,
            bbMiddle15m = snapshot.BbMiddle_15m,
            bbLower15m = snapshot.BbLower_15m,
            rsi14_15m = snapshot.Rsi14_15m,
            rsi14_15mPrev = snapshot.Rsi14Prev_15m,
            currentClose_15m = snapshot.CurrentClose_15m,
            atr14_15m = snapshot.Atr14_15m,
            // 1m frekans tetiği alanları
            ema9_1m = snapshot.Ema9_1m,
            ema21_1m = snapshot.Ema21_1m,
            ema9_1mPrev = snapshot.Ema9Prev_1m,
            ema21_1mPrev = snapshot.Ema21Prev_1m,
            atr14_1m = snapshot.Atr14_1m,
            atrPct_1m,
            currentClose = snapshot.CurrentClose_1m,
            currentVolume_1m = snapshot.CurrentVolume_1m,
            volumeMa20_1m = snapshot.VolumeMa20_1m,
            volumeRatio = snapshot.VolumeMa20_1m > 0m
                ? snapshot.CurrentVolume_1m / snapshot.VolumeMa20_1m
                : 0m,
            volumeMultiplier_1m = p.VolumeMultiplier1m,
            // Geometri
            tpAtrMultiplier = p.TpAtrMultiplier,
            slAtrMultiplier = p.SlAtrMultiplier,
            tpPct,
            slPct,
            maxHoldMinutes = p.MaxHoldMinutes,
            cooldownBarsAfterSignal = p.CooldownBarsAfterSignal,
            lastBarOpenTime_1m = snapshot.LastBarOpenTime_1m,
            lastBarOpenTime_15m = snapshot.LastBarOpenTime_15m,
        });

        _logger.LogInformation(
            "HybridMomentum1m emit symbol={Symbol} entry={Entry} stop={Stop} tp={Tp} " +
            "bbLower15m={BbLower} rsi15m={Rsi} ema9_1m={Ema9} ema21_1m={Ema21} " +
            "atr15m={Atr15} tpPct={TpPct} slPct={SlPct} decision=Emit",
            symbol, entryPrice, stopPrice, takeProfit,
            snapshot.BbLower_15m, snapshot.Rsi14_15m,
            snapshot.Ema9_1m, snapshot.Ema21_1m,
            snapshot.Atr14_15m, tpPct, slPct);

        // Sinyal yayını öncesi cooldown timestamp kaydı. Geometri valid
        // (Emit path); skip path'lerinde kayıt yapılmaz, böylece reddedilmiş
        // sinyaller sonraki bar'ı bloke etmez.
        _cooldown.RecordSignal(strategyId, symbol, now);

        // SuggestedQuantity placeholder — fan-out handler
        // (StrategySignalToOrderHandler) sizing service üzerinden ADR-0021
        // %20 equity / $20.10 floor rule ile gerçek miktarı hesaplar.
        // Pozitif-ama-küçük değer StrategySignal.Emit domain invariant'ını
        // (quantity > 0) karşılar.
        return Task.FromResult<StrategyEvaluation?>(new StrategyEvaluation(
            Direction: StrategySignalDirection.Long,
            SuggestedQuantity: 0.00000001m,
            SuggestedPrice: entryPrice,
            SuggestedStopPrice: stopPrice,
            ContextJson: ctx,
            SuggestedTakeProfit: takeProfit));
    }

    private static decimal Clamp(decimal value, decimal min, decimal max)
    {
        if (min > 0m && value < min) return min;
        if (max > 0m && value > max) return max;
        return value;
    }

    /// <summary>
    /// binance-expert spec kontratı — parametre tipi. Serialised as
    /// <c>Strategies.Seed[].ParametersJson</c>. Default'lar Loop 50 spec
    /// (1m EMA9/EMA21 + 15m BB(20,2) + 15m RSI14 oversold (&lt;40) +
    /// 1m volume×1.2 + 1m MinAtr 0.0003 + ATR15m TP/SL clip [0.40-1.00%] /
    /// [0.20-0.40%], MaxHold 30dk, Cooldown 3 bar × 1dk).
    /// </summary>
    private sealed class Parameters
    {
        // 1m frekans tetiği parametreleri.
        public int EmaFastPeriod { get; set; } = 9;
        public int EmaSlowPeriod { get; set; } = 21;
        public int VolumeWindow1m { get; set; } = 20;
        public int AtrPeriod1m { get; set; } = 14;
        public decimal VolumeMultiplier1m { get; set; } = 1.2m;
        public decimal MinAtrPct1m { get; set; } = 0.0003m;

        // 15m kalite kapısı parametreleri.
        public int BbPeriod15m { get; set; } = 20;
        public decimal BbStdMultiplier15m { get; set; } = 2.0m;
        public int RsiPeriod15m { get; set; } = 14;
        public decimal RsiOversoldThreshold15m { get; set; } = 40m;
        public int AtrPeriod15m { get; set; } = 14;

        // ATR15m-tabanlı TP/SL geometrisi. R:R 1.875:1, BE WR ~%34.8.
        public decimal TpAtrMultiplier { get; set; } = 1.5m;
        public decimal SlAtrMultiplier { get; set; } = 0.8m;
        public decimal MinTpPct { get; set; } = 0.004m;
        public decimal MaxTpPct { get; set; } = 0.010m;
        public decimal MinSlPct { get; set; } = 0.002m;
        public decimal MaxSlPct { get; set; } = 0.004m;

        // Time-Stop — 30dk (1m frekans tetiği + 15m rejim hedefi).
        public int MaxHoldMinutes { get; set; } = 30;

        // Cooldown — sinyal sonrası per coin 3 bar × 1dk = 3dk yeni sinyal
        // bloklanır (whipsaw + oversold yeniden tetik koruması; 15m kalite
        // kapısı zaten 15m bar bazında değiştiği için 3dk yeterli).
        public int CooldownBarsAfterSignal { get; set; } = 3;
    }
}
