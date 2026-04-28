using BinanceBot.Application.Abstractions;
using BinanceBot.Application.Strategies.Cooldowns;
using BinanceBot.Application.Strategies.Evaluation;
using BinanceBot.Application.Strategies.Indicators;
using BinanceBot.Domain.MarketData;
using BinanceBot.Domain.Strategies;
using Microsoft.Extensions.Logging;

namespace BinanceBot.Infrastructure.Strategies.Evaluators;

/// <summary>
/// Loop 46 AR-GE — 1m EMA9/EMA21 crossover scalper evaluator. Long-only spot
/// paper.
///
/// AR-GE motivasyonu: Loop 45 BbMeanReversion15m 6h'da 2 trade düşük frekans
/// teşhisi (kullanıcı talebi 20-30 trade/saat). 1m timeframe + EMA crossover
/// + nötr-RSI + gevşek hacim filtresi yüksek-frekans pozitif beklentili sinyal
/// akışı sağlar (binance-expert spec onaylı). R:R 1.875:1, BE WR ~%34.8.
///
/// Giriş (<see cref="StrategySignalDirection.Long"/>, kapalı 1m bar sonrası,
/// altı koşul AND):
///   1. Trend       — <c>Ema9Now &gt; Ema21Now</c> (kısa-vade trend pozitif).
///   2. Momentum    — <c>currentClose &gt; Ema9Now</c> (fiyat momentum üstünde).
///   3. RSI nötr    — <c>RsiLowerBand &lt;= Rsi14 &lt;= RsiUpperBand</c>
///      (default 40-65 — aşırı alım/satım uçları engelli).
///   4. Volume teyidi (gevşek) — <c>currentVolume &gt; VolumeSma20 × VolumeMultiplier</c>
///      (default 0.8 — DonchianBreakout/BbMeanReversion'dan daha gevşek;
///      trend-following frekans hedefi).
///   5. Bar kapandı — <see cref="EmaScalperIndicatorSnapshot.BarClosed"/>
///      <c>== true</c> (defansif; service zaten yalnızca kapalı bar'ı buffer'a alır).
///   6. MinAtr filtresi — <c>Atr14 / currentClose &gt;= MinAtrPct</c>
///      (default 0.0003 = %0.03; sessiz piyasa engeli — 1m bar volatilitesi
///      15m'in 1/sqrt(15) ≈ %26'sı, eşik DonchianBO'dan yarıdan az).
///
/// Çıkış (ATR14-tabanlı adaptif geometri):
///   - Take-Profit: <c>entry × (1 + clamp(Atr14 × TpAtrMultiplier / entry,
///     MinTpPct, MaxTpPct))</c>. Default TP %0.30 floor / %0.80 ceiling.
///   - Stop-Loss  : <c>entry × (1 − clamp(Atr14 × SlAtrMultiplier / entry,
///     MinSlPct, MaxSlPct))</c>. Default SL %0.20 floor / %0.50 ceiling.
///   - Time-Stop  : <c>MaxHoldMinutes</c> (default 8 = 8 bar × 1m) — 1m hızlı
///     scalp; ContextJson <c>maxHoldMinutes</c> anahtarına yazılır
///     (ADR-0017 §17.7).
///
/// Cooldown: <see cref="ICooldownService"/> singleton üzerinden evaluator-level
/// enforce edilir. Default 2 bar (2dk) — yüksek frekans hedefiyle uyumlu kısa
/// cooldown; aynı (strategyId, symbol) sinyal sonrası 2dk yeni sinyal blok.
///
/// Evaluator hiçbir DB bağlantısı açmaz, rolling buffer yönetmez — tüm
/// primitive verileri <see cref="IMarketIndicatorService.TryGetEmaScalperSnapshot"/>
/// üzerinden alır. Result-flow: skip ⇒ <c>null</c>; signal ⇒
/// <see cref="StrategyEvaluation"/>. Exception kontrol akışı için kullanılmaz —
/// invalid geometry / yetersiz warmup defensive null path.
/// </summary>
public sealed class EmaScalper1mEvaluator : IStrategyEvaluator
{
    public StrategyType Type => StrategyType.EmaScalper1m;

    private readonly IMarketIndicatorService _indicators;
    private readonly ICooldownService _cooldown;
    private readonly IClock _clock;
    private readonly ILogger<EmaScalper1mEvaluator> _logger;

    // Loop 46 strateji 1m bar üzerinde çalışır — cooldown penceresi 1m bar
    // çarpanıyla yorumlanır. Sabit, parametre dışı (interval-bound).
    private const int BarMinutes1m = 1;

    public EmaScalper1mEvaluator(
        IMarketIndicatorService indicators,
        ICooldownService cooldown,
        IClock clock,
        ILogger<EmaScalper1mEvaluator> logger)
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

        // KlineInterval şu an yalnızca "1m" desteklenir — service tarafı
        // OneMinute buffer üzerinden hesaplar. Diğer değerler defensive
        // null path (loglama amaçlı bilgi).
        var interval = ParseIntervalOrDefault(p.KlineInterval);
        if (interval != KlineInterval.OneMinute)
        {
            _logger.LogDebug(
                "EmaScalper1m unsupported interval symbol={Symbol} strategyId={StrategyId} interval={Interval}",
                symbol, strategyId, p.KlineInterval);
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        var snapshot = _indicators.TryGetEmaScalperSnapshot(
            symbol,
            emaFastPeriod: p.EmaFastPeriod,
            emaSlowPeriod: p.EmaSlowPeriod,
            rsiPeriod: p.RsiPeriod,
            volumeWindow: p.VolumeWindow,
            atrPeriod: p.AtrPeriod);

        if (snapshot is null)
        {
            _logger.LogDebug(
                "EmaScalper1m snapshot not ready symbol={Symbol} strategyId={StrategyId}",
                symbol, strategyId);
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        // Pre-warmup safety — degenerate zero values (flat market / not warm).
        if (snapshot.Ema9Now <= 0m
            || snapshot.Ema21Now <= 0m
            || snapshot.CurrentClose <= 0m)
        {
            _logger.LogDebug(
                "EmaScalper1m snapshot incomplete symbol={Symbol} ema9={Ema9} ema21={Ema21} close={Close}",
                symbol, snapshot.Ema9Now, snapshot.Ema21Now, snapshot.CurrentClose);
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        // 5) Bar kapandı (defansif).
        if (!snapshot.BarClosed)
        {
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        // 1) Trend: EMA9 > EMA21.
        var trendOk = snapshot.Ema9Now > snapshot.Ema21Now;

        // 2) Momentum: currentClose > EMA9.
        var momentumOk = snapshot.CurrentClose > snapshot.Ema9Now;

        // 3) RSI nötr aralık.
        var rsiOk = snapshot.Rsi14 >= p.RsiLowerBand && snapshot.Rsi14 <= p.RsiUpperBand;

        // 4) Volume teyidi (gevşek). VolumeSma20 = 0 ⇒ no-signal (warmup eksik
        // olarak değerlendirilir, snapshot zaten warmup eşiğinde null dönmeli).
        var volumeOk = snapshot.VolumeSma20 > 0m
            && snapshot.CurrentVolume > snapshot.VolumeSma20 * p.VolumeMultiplier;

        // 6) MinAtr filtresi (sessiz piyasa engeli).
        var atrPct = snapshot.CurrentClose > 0m
            ? snapshot.Atr14 / snapshot.CurrentClose
            : 0m;
        var atrOk = snapshot.Atr14 > 0m && atrPct >= p.MinAtrPct;

        if (!trendOk || !momentumOk || !rsiOk || !volumeOk || !atrOk)
        {
            _logger.LogDebug(
                "EmaScalper1m skip symbol={Symbol} close={Close} ema9={Ema9} ema21={Ema21} " +
                "rsi={Rsi} rsiLo={RsiLo} rsiHi={RsiHi} vol={Vol} volSma={VolSma} mult={Mult} " +
                "atrPct={AtrPct} minAtr={MinAtr} decision=Skip",
                symbol, snapshot.CurrentClose, snapshot.Ema9Now, snapshot.Ema21Now,
                snapshot.Rsi14, p.RsiLowerBand, p.RsiUpperBand,
                snapshot.CurrentVolume, snapshot.VolumeSma20, p.VolumeMultiplier,
                atrPct, p.MinAtrPct);
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        // Cooldown gate — sinyal koşulları sağlandıktan SONRA. Maliyet sırası
        // kasıtlı: önce ucuz indicator filtreleri, sonra cooldown (state lookup).
        var now = _clock.UtcNow;
        if (_cooldown.IsCooldown(strategyId, symbol, p.CooldownBarsAfterSignal, BarMinutes1m, now))
        {
            _logger.LogInformation(
                "EmaScalper1m cooldown skip symbol={Symbol} strategyId={StrategyId} " +
                "cooldownBars={Bars} barMinutes={BarMinutes} decision=CooldownSkip",
                symbol, strategyId, p.CooldownBarsAfterSignal, BarMinutes1m);
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
                "EmaScalper1m geometry invalid symbol={Symbol} entry={Entry} stop={Stop} tp={Tp}",
                symbol, entryPrice, stopPrice, takeProfit);
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        // ContextJson — TimeStop handler (ADR-0017 §17.7) `maxHoldMinutes` key
        // şartı korunur. EMA/RSI/volume/ATR değerleri loglama amaçlı taşınır.
        var ctx = EvaluatorParameterHelper.SerializeContext(new
        {
            type = "ema-scalper-1m",
            entryReason = "ema_crossover",
            ema9 = snapshot.Ema9Now,
            ema9Prev = snapshot.Ema9Prev,
            ema21 = snapshot.Ema21Now,
            ema21Prev = snapshot.Ema21Prev,
            rsi14 = snapshot.Rsi14,
            currentClose = snapshot.CurrentClose,
            currentVolume = snapshot.CurrentVolume,
            volumeSma20 = snapshot.VolumeSma20,
            volumeMultiplier = p.VolumeMultiplier,
            atr14 = snapshot.Atr14,
            atrPct,
            tpAtrMultiplier = p.TpAtrMultiplier,
            slAtrMultiplier = p.SlAtrMultiplier,
            tpPct,
            slPct,
            maxHoldMinutes = p.MaxHoldMinutes,
            cooldownBarsAfterSignal = p.CooldownBarsAfterSignal,
            lastBarOpenTime = snapshot.LastBarOpenTime,
        });

        _logger.LogInformation(
            "EmaScalper1m emit symbol={Symbol} entry={Entry} stop={Stop} tp={Tp} " +
            "ema9={Ema9} ema21={Ema21} rsi={Rsi} atr14={Atr} tpPct={TpPct} slPct={SlPct} decision=Emit",
            symbol, entryPrice, stopPrice, takeProfit,
            snapshot.Ema9Now, snapshot.Ema21Now, snapshot.Rsi14, snapshot.Atr14, tpPct, slPct);

        // Sinyal yayını öncesi cooldown timestamp kaydı. Geometri valid (Emit
        // path); skip path'lerinde kayıt yapılmaz, böylece reddedilmiş sinyaller
        // sonraki bar'ı bloke etmez.
        _cooldown.RecordSignal(strategyId, symbol, now);

        // SuggestedQuantity placeholder — fan-out handler (StrategySignalToOrderHandler)
        // sizing service üzerinden ADR-0021 %20 equity / $20.10 floor rule ile
        // gerçek miktarı hesaplar.
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
    /// Loop 46 — yalnızca <c>"1m"</c> desteklenir. Diğer değerler
    /// <see cref="KlineInterval.FifteenMinutes"/> fallback (defensive — evaluator
    /// uyumsuz interval'da no-signal döner ve loglar). Fallback değerinin
    /// 1m'den FARKLI olması zorunlu, böylece "yanlış config + sessiz no-op"
    /// toplaması engellenir.
    /// </summary>
    private static KlineInterval ParseIntervalOrDefault(string? raw) =>
        raw switch
        {
            "1m" => KlineInterval.OneMinute,
            _ => KlineInterval.FifteenMinutes,
        };

    /// <summary>
    /// binance-expert spec kontratı — parametre tipi. Serialised as
    /// <c>Strategies.Seed[].ParametersJson</c>. Default'lar 1m EMA9/EMA21
    /// scalper baseline (TP %0.30-0.80, SL %0.20-0.50, R:R 1.875:1, MaxHold 8dk,
    /// Cooldown 2 bar).
    /// </summary>
    private sealed class Parameters
    {
        public string KlineInterval { get; set; } = "1m";

        // EMA fast / slow periyotları.
        public int EmaFastPeriod { get; set; } = 9;
        public int EmaSlowPeriod { get; set; } = 21;

        // RSI Wilder nötr aralık.
        public int RsiPeriod { get; set; } = 14;
        public decimal RsiLowerBand { get; set; } = 40m;
        public decimal RsiUpperBand { get; set; } = 65m;

        // Volume teyidi (gevşek): currentVolume > VolumeSma20 × VolumeMultiplier.
        public int VolumeWindow { get; set; } = 20;
        public decimal VolumeMultiplier { get; set; } = 0.8m;

        // ATR ve ATR-tabanlı TP/SL geometrisi.
        public int AtrPeriod { get; set; } = 14;
        public decimal TpAtrMultiplier { get; set; } = 1.5m;
        public decimal SlAtrMultiplier { get; set; } = 0.8m;

        // 1m bar volatilitesi 15m'in ~%26'sı — clip aralıkları daha dar.
        // TP %0.30-0.80, SL %0.20-0.50 (R:R 1.875:1, BE WR ~%34.8).
        public decimal MinTpPct { get; set; } = 0.003m;
        public decimal MaxTpPct { get; set; } = 0.008m;
        public decimal MinSlPct { get; set; } = 0.002m;
        public decimal MaxSlPct { get; set; } = 0.005m;

        // Time-Stop — 8 bar × 1m = 8dk hızlı scalp.
        public int MaxHoldMinutes { get; set; } = 8;

        // Sessiz piyasa filtresi: ATR/Close < %0.03 ⇒ sinyal yok (1m bar için
        // 15m'den daha gevşek — short-window noise floor).
        public decimal MinAtrPct { get; set; } = 0.0003m;

        // Cooldown — sinyal yayınlandıktan sonra aynı (strategy, symbol) için
        // 2 bar × 1dk = 2dk yeni sinyal bloklanır (yüksek-frekans uyumlu kısa
        // cooldown; whipsaw için BB/Donchian'dan kısa, frekans hedefi 20-30/saat).
        public int CooldownBarsAfterSignal { get; set; } = 2;
    }
}
