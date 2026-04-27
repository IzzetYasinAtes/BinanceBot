using BinanceBot.Application.Abstractions;
using BinanceBot.Application.Strategies.Cooldowns;
using BinanceBot.Application.Strategies.Evaluation;
using BinanceBot.Application.Strategies.Indicators;
using BinanceBot.Domain.MarketData;
using BinanceBot.Domain.Strategies;
using Microsoft.Extensions.Logging;

namespace BinanceBot.Infrastructure.Strategies.Evaluators;

/// <summary>
/// Loop 41 AR-GE — 15m Donchian Channel Breakout (20-bar) + Volume Z-Score &gt; 1.5
/// + ATR14 tabanlı dinamik TP/SL evaluator. Long-only spot paper.
///
/// AR-GE motivasyonu (<c>loops/loop_41/strategy-arge-v2.md</c> §3 + §5,
/// senaryo 2): R:R 2.67:1, BE WR ~%36.5, beklenen +$5.16/gün orta senaryo.
/// AtrScalperVwapEma1m'in 4 loop kümülatif düşük TP-hit rate teşhisi sonrası
/// breakout-rejimine geçiş — Donchian üst kırılım high-momentum filtresi +
/// hacim teyidi spam azaltma.
///
/// Giriş (<see cref="StrategySignalDirection.Long"/>, kapalı 15m bar sonrası,
/// dört koşul AND):
///   1. Donchian breakout — <c>currentClose &gt; DonchianHigh20</c> (son 20
///      KAPANMIŞ bar üst sınırı; current bar pencereye dahil değil).
///   2. Volume Z-Score    — <c>(currentVolume − VolumeAvg20) / VolumeStd20
///      &gt; VolumeZScoreThreshold</c> (default 1.5). VolumeStd20 = 0 ise
///      (flat hacim) sinyal yok.
///   3. Bar kapandı       — <see cref="DonchianBreakoutIndicatorSnapshot.BarClosed"/>
///      <c>== true</c> — intra-bar gürültü engeli (defansif; service zaten
///      yalnızca kapalı bar'ı buffer'a alır).
///   4. MinAtr filtresi   — <c>Atr14 / currentClose &gt;= MinAtrPct</c>
///      (default 0.0006 = %0.06). Sessiz piyasada sinyal üretmez.
///
/// Çıkış (ATR14-tabanlı adaptif geometri):
///   - Take-Profit: <c>entry × (1 + clamp(Atr14 × TpAtrMultiplier / entry,
///     MinTpPct, MaxTpPct))</c>. Default TP %0.5 floor / %1.2 ceiling.
///   - Stop-Loss  : <c>entry × (1 − clamp(Atr14 × SlAtrMultiplier / entry,
///     MinSlPct, MaxSlPct))</c>. Default SL %0.2 floor / %0.5 ceiling.
///   - Time-Stop  : <c>MaxHoldMinutes</c> (default 90 = 6 bar × 15m) —
///     ContextJson <c>maxHoldMinutes</c> anahtarına yazılır (ADR-0017 §17.7).
///
/// Cooldown (Loop 42 V2): <see cref="ICooldownService"/> singleton üzerinden
/// evaluator-level enforce edilir. Sinyal koşulları sağlandığı an
/// <c>IsCooldown(strategyId, symbol, CooldownBarsAfterSignal, 15, now)</c>
/// kontrolü yapılır; <c>true</c> ise evaluator <c>null</c> döner ve
/// StrategyEvaluationHandler "cooldown_active" reason ile SignalSkipped
/// publish eder. Sinyal yayınlandığında <c>RecordSignal</c> çağrılır.
/// Loop 41 t=210 HALT gerekçesi: V1 stateless tasarım LTC whipsaw'a açık
/// kaldı (7 ardışık SL).
///
/// Evaluator hiçbir DB bağlantısı açmaz, rolling buffer yönetmez — tüm
/// primitive verileri <see cref="IMarketIndicatorService.TryGetDonchianBreakoutSnapshot"/>
/// üzerinden alır. Result-flow: skip ⇒ <c>null</c> döner; signal ⇒
/// <see cref="StrategyEvaluation"/> döner. Exception kontrol akışı için
/// kullanılmaz — invalid geometry / yetersiz warmup defensive null path.
/// </summary>
public sealed class DonchianBreakoutEvaluator : IStrategyEvaluator
{
    public StrategyType Type => StrategyType.DonchianBreakout15m;

    private readonly IMarketIndicatorService _indicators;
    private readonly ICooldownService _cooldown;
    private readonly IClock _clock;
    private readonly ILogger<DonchianBreakoutEvaluator> _logger;

    // Loop 41 strateji 15m bar üzerinde çalışır — cooldown penceresi de 15m bar
    // çarpanıyla yorumlanır. Sabit, parametre dışı (interval-bound).
    private const int BarMinutes15m = 15;

    public DonchianBreakoutEvaluator(
        IMarketIndicatorService indicators,
        ICooldownService cooldown,
        IClock clock,
        ILogger<DonchianBreakoutEvaluator> logger)
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
                "DonchianBreakout unsupported interval symbol={Symbol} strategyId={StrategyId} interval={Interval}",
                symbol, strategyId, p.KlineInterval);
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        var snapshot = _indicators.TryGetDonchianBreakoutSnapshot(
            symbol,
            donchianPeriod: p.DonchianPeriod,
            volumeWindow: p.VolumeWindow,
            atrPeriod: p.AtrPeriod);

        if (snapshot is null)
        {
            _logger.LogDebug(
                "DonchianBreakout snapshot not ready symbol={Symbol} strategyId={StrategyId}",
                symbol, strategyId);
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        // Pre-warmup safety — degenerate zero values (flat market / not warm).
        if (snapshot.DonchianHigh20 <= 0m
            || snapshot.VolumeAvg20 <= 0m
            || snapshot.CurrentClose <= 0m)
        {
            _logger.LogDebug(
                "DonchianBreakout snapshot incomplete symbol={Symbol} donHi={DonHi} volAvg={VolAvg}",
                symbol, snapshot.DonchianHigh20, snapshot.VolumeAvg20);
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        // 1) Bar kapandı (defansif).
        if (!snapshot.BarClosed)
        {
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        // 2) Donchian üst kırılım.
        var breakoutOk = snapshot.CurrentClose > snapshot.DonchianHigh20;

        // 3) Volume Z-Score > threshold. Std == 0 ⇒ Z-Score hesaplanamaz, no-signal.
        var volumeZScore = snapshot.VolumeStd20 > 0m
            ? (snapshot.CurrentVolume - snapshot.VolumeAvg20) / snapshot.VolumeStd20
            : 0m;
        var volumeOk = snapshot.VolumeStd20 > 0m
            && volumeZScore > p.VolumeZScoreThreshold;

        // 4) MinAtr filtresi (sessiz piyasa engeli).
        var atrPct = snapshot.CurrentClose > 0m
            ? snapshot.Atr14 / snapshot.CurrentClose
            : 0m;
        var atrOk = snapshot.Atr14 > 0m && atrPct >= p.MinAtrPct;

        if (!breakoutOk || !volumeOk || !atrOk)
        {
            _logger.LogDebug(
                "DonchianBreakout skip symbol={Symbol} close={Close} donHi={DonHi} " +
                "z={Z} threshold={Threshold} atrPct={AtrPct} minAtr={MinAtr} decision=Skip",
                symbol, snapshot.CurrentClose, snapshot.DonchianHigh20,
                volumeZScore, p.VolumeZScoreThreshold, atrPct, p.MinAtrPct);
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        // Loop 42 P0 — sinyal koşulları sağlandıktan SONRA cooldown gate.
        // Maliyet sırası kasıtlı: önce ucuz indicator filtreleri, sonra cooldown
        // (state lookup). Cooldown aktifken evaluator yine `null` döner; runtime
        // (StrategyEvaluationHandler) "cooldown_active" reason ile skip event
        // publish eder.
        var now = _clock.UtcNow;
        if (_cooldown.IsCooldown(strategyId, symbol, p.CooldownBarsAfterSignal, BarMinutes15m, now))
        {
            _logger.LogInformation(
                "DonchianBreakout cooldown skip symbol={Symbol} strategyId={StrategyId} " +
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
                "DonchianBreakout geometry invalid symbol={Symbol} entry={Entry} stop={Stop} tp={Tp}",
                symbol, entryPrice, stopPrice, takeProfit);
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        // ContextJson — TimeStop handler (ADR-0017 §17.7) `maxHoldMinutes` key
        // şartı korunur. Cooldown bilgisi ileride handler/runtime için context'te
        // yer alır (V1: stateless evaluator, runtime cooldown eviction).
        var ctx = EvaluatorParameterHelper.SerializeContext(new
        {
            type = "donchian-breakout-15m",
            donchianHigh20 = snapshot.DonchianHigh20,
            donchianLow20 = snapshot.DonchianLow20,
            currentClose = snapshot.CurrentClose,
            currentVolume = snapshot.CurrentVolume,
            volumeAvg20 = snapshot.VolumeAvg20,
            volumeStd20 = snapshot.VolumeStd20,
            volumeZScore,
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
            "DonchianBreakout emit symbol={Symbol} entry={Entry} stop={Stop} tp={Tp} " +
            "z={Z} atr14={Atr} tpPct={TpPct} slPct={SlPct} decision=Emit",
            symbol, entryPrice, stopPrice, takeProfit,
            volumeZScore, snapshot.Atr14, tpPct, slPct);

        // Loop 42 P0 — sinyal yayını öncesi cooldown timestamp kaydı. Geometri
        // valid (Emit path); skip path'lerinde kayıt yapılmaz, böylece reddedilmiş
        // sinyaller sonraki bar'ı bloke etmez.
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
    /// Loop 41 — yalnızca <c>"15m"</c> desteklenir. Diğer değerler
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
    /// AR-GE §3 + §5 (Senaryo 2) — parametre kontratı. Serialised as
    /// <c>Strategies.Seed[].ParametersJson</c>. Default'lar BTCUSDT bazlı
    /// senaryo 2 değerleri (R:R 2.67:1).
    /// </summary>
    private sealed class Parameters
    {
        public string KlineInterval { get; set; } = "15m";

        // Donchian üst kırılım periyodu (kapalı bar penceresi).
        public int DonchianPeriod { get; set; } = 20;

        // Volume mean+std penceresi.
        public int VolumeWindow { get; set; } = 20;
        public decimal VolumeZScoreThreshold { get; set; } = 1.5m;

        // ATR ve ATR-tabanlı TP/SL geometrisi.
        public int AtrPeriod { get; set; } = 14;
        public decimal TpAtrMultiplier { get; set; } = 2.0m;
        public decimal SlAtrMultiplier { get; set; } = 0.65m;

        // Senaryo 2 clip aralıkları (TP %0.5–1.2, SL %0.2–0.5).
        public decimal MinTpPct { get; set; } = 0.005m;
        public decimal MaxTpPct { get; set; } = 0.012m;
        public decimal MinSlPct { get; set; } = 0.002m;
        public decimal MaxSlPct { get; set; } = 0.005m;

        // Time-Stop — 6 bar × 15m = 90dk.
        public int MaxHoldMinutes { get; set; } = 90;

        // Sessiz piyasa filtresi: ATR/Close < %0.06 ⇒ sinyal yok.
        public decimal MinAtrPct { get; set; } = 0.0006m;

        // Cooldown (V1: handler/runtime tarafında uygulanır; evaluator yalnızca
        // bağlamda taşır).
        public int CooldownBarsAfterSignal { get; set; } = 4;
    }
}
