using BinanceBot.Application.Abstractions;
using BinanceBot.Application.Strategies.Cooldowns;
using BinanceBot.Application.Strategies.Evaluation;
using BinanceBot.Application.Strategies.Indicators;
using BinanceBot.Domain.MarketData;
using BinanceBot.Domain.Strategies;
using Microsoft.Extensions.Logging;

namespace BinanceBot.Infrastructure.Strategies.Evaluators;

/// <summary>
/// Loop 44 AR-GE — 15m Bollinger Bands counter-trend mean reversion evaluator.
/// Long-only spot paper.
///
/// AR-GE motivasyonu: Loop 41-42-43 DonchianBreakout 15m kümülatif %0 WR
/// (testnet 12 sembol, 4 bar cooldown + V2 stateful) — breakout-rejimi
/// testnet hacim profilinde sürdürülemez TP hit verdi. Pivot: counter-trend
/// BB lower kapışı + RSI oversold + panik hacim teyidi (binance-expert spec
/// onaylı). Hedef: kısa vadeli toparlanma yakalamak (mean reversion to BB
/// middle), R:R asimetrik (TP 1.5×ATR / SL 1.0×ATR ≈ 1.5:1).
///
/// Giriş (<see cref="StrategySignalDirection.Long"/>, kapalı 15m bar sonrası,
/// beş koşul AND):
///   1. BB lower kapışı  — <c>currentClose &lt; BbLower</c> (BB(20, 2σ)).
///   2. RSI oversold     — <c>Rsi14 &lt; RsiOversoldThreshold</c> (default 30).
///   3. Volume Z-Score   — <c>(currentVolume − VolumeAvg20) / VolumeStd20
///      &gt; VolumeZScoreThreshold</c> (default 1.0 — Donchian'daki 1.5'tan
///      düşük, çünkü oversold koşulunda hacim zaten yüksektir).
///      VolumeStd20 = 0 ise (flat hacim) sinyal yok.
///   4. MinAtr filtresi  — <c>Atr14 / currentClose &gt;= MinAtrPct</c>
///      (default 0.0007 = %0.07). Sessiz piyasada sinyal üretmez.
///   5. Bar kapandı      — <see cref="BbMeanReversionIndicatorSnapshot.BarClosed"/>
///      <c>== true</c> — intra-bar gürültü engeli (defansif; service zaten
///      yalnızca kapalı bar'ı buffer'a alır).
///
/// Çıkış (ATR14-tabanlı adaptif geometri):
///   - Take-Profit: <c>entry × (1 + clamp(Atr14 × TpAtrMultiplier / entry,
///     MinTpPct, MaxTpPct))</c>. Default TP %0.4 floor / %1.0 ceiling
///     (mean-reversion hedefi BB middle olduğundan TP daha agresif değil).
///   - Stop-Loss  : <c>entry × (1 − clamp(Atr14 × SlAtrMultiplier / entry,
///     MinSlPct, MaxSlPct))</c>. Default SL %0.3 floor / %0.6 ceiling
///     (counter-trend riski → SL daha geniş, BB lower altı serbest düşüş
///     korunması).
///   - Time-Stop  : <c>MaxHoldMinutes</c> (default 90 = 6 bar × 15m) —
///     ContextJson <c>maxHoldMinutes</c> anahtarına yazılır (ADR-0017 §17.7).
///
/// Cooldown: <see cref="ICooldownService"/> singleton üzerinden evaluator-level
/// enforce edilir. Default 4 bar (60dk) — sinyal yayınlandıktan sonra aynı
/// (strategyId, symbol) için yeni sinyal bloklanır. Whipsaw koruması
/// (Loop 41 t=210 LTC root cause analizi).
///
/// Evaluator hiçbir DB bağlantısı açmaz, rolling buffer yönetmez — tüm
/// primitive verileri <see cref="IMarketIndicatorService.TryGetBbMeanReversionSnapshot"/>
/// üzerinden alır. Result-flow: skip ⇒ <c>null</c> döner; signal ⇒
/// <see cref="StrategyEvaluation"/> döner. Exception kontrol akışı için
/// kullanılmaz — invalid geometry / yetersiz warmup defensive null path.
/// </summary>
public sealed class BbMeanReversionEvaluator : IStrategyEvaluator
{
    public StrategyType Type => StrategyType.BbMeanReversion15m;

    private readonly IMarketIndicatorService _indicators;
    private readonly ICooldownService _cooldown;
    private readonly IClock _clock;
    private readonly ILogger<BbMeanReversionEvaluator> _logger;

    // Loop 44 strateji 15m bar üzerinde çalışır — cooldown penceresi de 15m bar
    // çarpanıyla yorumlanır. Sabit, parametre dışı (interval-bound).
    private const int BarMinutes15m = 15;

    public BbMeanReversionEvaluator(
        IMarketIndicatorService indicators,
        ICooldownService cooldown,
        IClock clock,
        ILogger<BbMeanReversionEvaluator> logger)
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

        // KlineInterval şu an yalnızca "15m" desteklenir — service tarafı
        // FifteenMinute buffer üzerinden hesaplar. Diğer değerler defensive
        // null path (loglama amaçlı bilgi).
        var interval = ParseIntervalOrDefault(p.KlineInterval);
        if (interval != KlineInterval.FifteenMinutes)
        {
            _logger.LogDebug(
                "BbMeanReversion unsupported interval symbol={Symbol} strategyId={StrategyId} interval={Interval}",
                symbol, strategyId, p.KlineInterval);
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        var snapshot = _indicators.TryGetBbMeanReversionSnapshot(
            symbol,
            bbPeriod: p.BbPeriod,
            bbStdMultiplier: p.BbStdMultiplier,
            rsiPeriod: p.RsiPeriod,
            volumeWindow: p.VolumeWindow,
            atrPeriod: p.AtrPeriod);

        if (snapshot is null)
        {
            _logger.LogDebug(
                "BbMeanReversion snapshot not ready symbol={Symbol} strategyId={StrategyId}",
                symbol, strategyId);
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        // Pre-warmup safety — degenerate zero values (flat market / not warm).
        if (snapshot.BbLower <= 0m
            || snapshot.VolumeAvg20 <= 0m
            || snapshot.CurrentClose <= 0m)
        {
            _logger.LogDebug(
                "BbMeanReversion snapshot incomplete symbol={Symbol} bbLower={BbLower} volAvg={VolAvg}",
                symbol, snapshot.BbLower, snapshot.VolumeAvg20);
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        // 5) Bar kapandı (defansif).
        if (!snapshot.BarClosed)
        {
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        // 0) Loop 58 disaster recovery — EMA200 trend filtresi. Long-only
        // counter-trend mean reversion sadece BÜYÜK trend yönü ile uyumlu
        // ise giriş yap (currentClose > Ema200_15m ⇒ uptrend). Downtrend'de
        // BB lower kapışı yakalama denemeleri "düşen bıçağı tutmak" anti-
        // pattern'i — Loop 56-57 backtest sonuçları (binance-expert spec).
        // Snapshot 200-bar warmup eşiği geçmeden null döner; bu noktaya
        // gelinmişse Ema200_15m anlamlıdır.
        // Loop 67 — config flag p.UseEma200TrendFilter ile bypass edilebilir
        // (sürekli downtrend rejiminde 0 emit'e takılma → "5 coin sürekli
        // işlem" garantisi). Default true ⇒ geriye uyumlu davranış.
        if (p.UseEma200TrendFilter
            && snapshot.Ema200_15m > 0m
            && snapshot.CurrentClose <= snapshot.Ema200_15m)
        {
            _logger.LogInformation(
                "BbMeanReversion downtrend_skip symbol={Symbol} strategyId={StrategyId} " +
                "close={Close} ema200_15m={Ema200} decision=Skip reason=downtrend_skip",
                symbol, strategyId, snapshot.CurrentClose, snapshot.Ema200_15m);
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        // 1) BB lower kapışı.
        var bbBreachOk = snapshot.CurrentClose < snapshot.BbLower;

        // 2) RSI oversold.
        var rsiOk = snapshot.Rsi14 < p.RsiOversoldThreshold;

        // 3) Volume Z-Score > threshold. Std == 0 ⇒ no-signal.
        var volumeZScore = snapshot.VolumeStd20 > 0m
            ? (snapshot.CurrentVolume - snapshot.VolumeAvg20) / snapshot.VolumeStd20
            : 0m;
        var volumeOk = p.VolumeZScoreThreshold <= 0m
            || (snapshot.VolumeStd20 > 0m && volumeZScore > p.VolumeZScoreThreshold);

        // 4) MinAtr filtresi (sessiz piyasa engeli).
        var atrPct = snapshot.CurrentClose > 0m
            ? snapshot.Atr14 / snapshot.CurrentClose
            : 0m;
        var atrOk = snapshot.Atr14 > 0m && atrPct >= p.MinAtrPct;

        if (!bbBreachOk || !rsiOk || !volumeOk || !atrOk)
        {
            _logger.LogDebug(
                "BbMeanReversion skip symbol={Symbol} close={Close} bbLower={BbLower} " +
                "rsi={Rsi} rsiThr={RsiThr} z={Z} zThr={ZThr} atrPct={AtrPct} minAtr={MinAtr} decision=Skip",
                symbol, snapshot.CurrentClose, snapshot.BbLower,
                snapshot.Rsi14, p.RsiOversoldThreshold,
                volumeZScore, p.VolumeZScoreThreshold,
                atrPct, p.MinAtrPct);
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        // Cooldown gate — sinyal koşulları sağlandıktan SONRA. Maliyet sırası
        // kasıtlı: önce ucuz indicator filtreleri, sonra cooldown (state lookup).
        var now = _clock.UtcNow;
        if (_cooldown.IsCooldown(strategyId, symbol, p.CooldownBarsAfterSignal, BarMinutes15m, now))
        {
            _logger.LogInformation(
                "BbMeanReversion cooldown skip symbol={Symbol} strategyId={StrategyId} " +
                "cooldownBars={Bars} barMinutes={BarMinutes} decision=CooldownSkip",
                symbol, strategyId, p.CooldownBarsAfterSignal, BarMinutes15m);
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        var entryPrice = snapshot.CurrentClose;

        // ATR-tabanlı TP/SL geometrisi + Min/Max clip. Atr14 > 0 zaten yukarıda
        // garanti (atrOk true).
        var rawTpPct = snapshot.Atr14 * p.TpAtrMultiplier / entryPrice;
        var rawSlPct = snapshot.Atr14 * p.SlAtrMultiplier / entryPrice;
        var tpPct = Clamp(rawTpPct, p.MinTpPct, p.MaxTpPct);
        var slPct = Clamp(rawSlPct, p.MinSlPct, p.MaxSlPct);

        var takeProfit = entryPrice * (1m + tpPct);
        var stopPrice = entryPrice * (1m - slPct);

        if (takeProfit <= entryPrice || stopPrice >= entryPrice)
        {
            _logger.LogDebug(
                "BbMeanReversion geometry invalid symbol={Symbol} entry={Entry} stop={Stop} tp={Tp}",
                symbol, entryPrice, stopPrice, takeProfit);
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        // ContextJson — TimeStop handler (ADR-0017 §17.7) `maxHoldMinutes` key
        // şartı korunur. BB orta band hedef referansı ve filtre değerleri loglama
        // amacıyla taşınır.
        var ctx = EvaluatorParameterHelper.SerializeContext(new
        {
            type = "bb-mean-reversion-15m",
            bbUpper = snapshot.BbUpper,
            bbMiddle = snapshot.BbMiddle,
            bbLower = snapshot.BbLower,
            rsi14 = snapshot.Rsi14,
            currentClose = snapshot.CurrentClose,
            currentVolume = snapshot.CurrentVolume,
            volumeAvg20 = snapshot.VolumeAvg20,
            volumeStd20 = snapshot.VolumeStd20,
            volumeZScore,
            atr14 = snapshot.Atr14,
            atrPct,
            // Loop 58 — EMA200 trend filtre değeri (uptrend doğrulaması).
            ema200_15m = snapshot.Ema200_15m,
            tpAtrMultiplier = p.TpAtrMultiplier,
            slAtrMultiplier = p.SlAtrMultiplier,
            tpPct,
            slPct,
            maxHoldMinutes = p.MaxHoldMinutes,
            cooldownBarsAfterSignal = p.CooldownBarsAfterSignal,
            lastBarOpenTime = snapshot.LastBarOpenTime,
        });

        _logger.LogInformation(
            "BbMeanReversion emit symbol={Symbol} entry={Entry} stop={Stop} tp={Tp} " +
            "rsi={Rsi} z={Z} atr14={Atr} tpPct={TpPct} slPct={SlPct} decision=Emit",
            symbol, entryPrice, stopPrice, takeProfit,
            snapshot.Rsi14, volumeZScore, snapshot.Atr14, tpPct, slPct);

        // Sinyal yayını öncesi cooldown timestamp kaydı. Geometri valid (Emit
        // path); skip path'lerinde kayıt yapılmaz, böylece reddedilmiş sinyaller
        // sonraki bar'ı bloke etmez.
        _cooldown.RecordSignal(strategyId, symbol, now);

        // SuggestedQuantity placeholder — fan-out handler (StrategySignalToOrderHandler)
        // sizing service üzerinden ADR-0021 %20 equity / $20.10 floor rule ile
        // gerçek miktarı hesaplar. Pozitif-ama-küçük değer
        // StrategySignal.Emit domain invariant'ını (quantity > 0) karşılar.
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
    /// Loop 44 — yalnızca <c>"15m"</c> desteklenir. Diğer değerler
    /// <see cref="KlineInterval.OneMinute"/> fallback (defensive — evaluator
    /// uyumsuz interval'da no-signal döner ve loglar).
    /// </summary>
    private static KlineInterval ParseIntervalOrDefault(string? raw) =>
        raw switch
        {
            "15m" => KlineInterval.FifteenMinutes,
            _ => KlineInterval.OneMinute,
        };

    /// <summary>
    /// binance-expert spec kontratı — parametre tipi. Serialised as
    /// <c>Strategies.Seed[].ParametersJson</c>. Default'lar BTCUSDT bazlı
    /// counter-trend BB(20,2) + RSI14 + Vol Z 1.0 değerleri.
    /// </summary>
    private sealed class Parameters
    {
        public string KlineInterval { get; set; } = "15m";

        // Bollinger Bands: period + std multiplier.
        public int BbPeriod { get; set; } = 20;
        public decimal BbStdMultiplier { get; set; } = 2.0m;

        // RSI Wilder oversold filtresi.
        public int RsiPeriod { get; set; } = 14;
        public decimal RsiOversoldThreshold { get; set; } = 30m;

        // Volume mean+std penceresi (current bar dahil).
        public int VolumeWindow { get; set; } = 20;
        public decimal VolumeZScoreThreshold { get; set; } = 1.0m;

        // ATR ve ATR-tabanlı TP/SL geometrisi.
        public int AtrPeriod { get; set; } = 14;
        public decimal TpAtrMultiplier { get; set; } = 1.5m;
        public decimal SlAtrMultiplier { get; set; } = 1.0m;

        // Counter-trend hedef BB middle; clip aralıkları (TP %0.4–1.0,
        // SL %0.3–0.6 — counter-trend SL biraz daha geniş).
        public decimal MinTpPct { get; set; } = 0.004m;
        public decimal MaxTpPct { get; set; } = 0.010m;
        public decimal MinSlPct { get; set; } = 0.003m;
        public decimal MaxSlPct { get; set; } = 0.006m;

        // Time-Stop — 6 bar × 15m = 90dk.
        public int MaxHoldMinutes { get; set; } = 90;

        // Sessiz piyasa filtresi: ATR/Close < %0.07 ⇒ sinyal yok
        // (Donchian'dan biraz daha katı, BB rejimde hareket beklentisi).
        public decimal MinAtrPct { get; set; } = 0.0007m;

        // Cooldown — sinyal yayınlandıktan sonra aynı (strategy, symbol) için
        // 4 bar × 15dk = 60dk yeni sinyal bloklanır (whipsaw koruması).
        public int CooldownBarsAfterSignal { get; set; } = 4;

        // Loop 67 — EMA200 trend filtresi config flag. Default true (Loop 58
        // disaster recovery davranışı geriye uyumlu). Long-only mean-reversion
        // sürekli downtrend rejiminde 0 emit'e takılırsa (BTC 6h+ skip) seed'de
        // false ile bypass edilebilir; "5 coin sürekli işlem" garantisi için.
        public bool UseEma200TrendFilter { get; set; } = true;
    }
}
