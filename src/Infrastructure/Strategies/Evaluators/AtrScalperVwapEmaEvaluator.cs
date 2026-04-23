using BinanceBot.Application.Strategies.Evaluation;
using BinanceBot.Application.Strategies.Indicators;
using BinanceBot.Domain.MarketData;
using BinanceBot.Domain.Strategies;
using Microsoft.Extensions.Logging;

namespace BinanceBot.Infrastructure.Strategies.Evaluators;

/// <summary>
/// Loop 33 AR-GE (Strateji D) — 1m kline VWAP reclaim + EMA20 slope + volume
/// confirm + ATR14 çarpanlı dinamik TP/SL micro-scalper. MicroScalperVwapEma30s
/// evaluator'ünün %80'ini (filtre geometrisi) koruyarak yalnızca TP/SL hedefini
/// sabit yüzde yerine <c>entry ± multiplier × ATR14</c> olarak hesaplar.
///
/// AR-GE motivasyonu (<c>loops/loop_33/strategy-arge.md</c> §1–§3):
///   - BNB break-even WR %90.8, ETH %71.2 — sabit %0.2–0.3 TP, round-trip fee
///     tarafından yutuluyordu (fee/gross %85). ATR-tabanlı TP yüksek vol
///     dönemlerinde $0.50–0.70 hedef sağlar; fee/gross %24'e düşer.
///   - SOL/ADA gibi yüksek-ATR semboller break-even WR'ı %49.5'e indirir —
///     MicroScalper'dan 12 puan daha ulaşabilir eşik.
///
/// Giriş (<see cref="StrategySignalDirection.Long"/>, kapalı 1m bar sonrası):
///   1. Direction gate  — <c>emaNow &gt; emaPrev × (1 + SlopeTolerance)</c>.
///      Default <c>SlopeTolerance = -0.003</c> (AR-GE önerisi) → hafif
///      negatif slope'a izin verir (pullback giriş); MicroScalper default'u
///      da aynı.
///   2. VWAP context    — önceki bar kapanışı VWAP altında (pullback).
///   3. VWAP reclaim    — son bar kapanışı VWAP üstünde veya tolerans
///      zone'unda (<see cref="Parameters.VwapTolerancePct"/>).
///   4. Volume confirm  — <c>lastVolume &gt;= volumeSma20 × VolumeMultiplier</c>.
///      Default multiplier 0.5 (high-vol altcoin spike rejection).
///
/// Çıkış (ATR14-tabanlı adaptif geometri):
///   - Take-Profit: <c>entry × (1 + pct)</c> nerede
///     <c>pct = clamp(ATR14 × TpAtrMultiplier / entry, MinTpPct, MaxTpPct)</c>.
///   - Stop-Loss  : <c>entry × (1 − clamp(ATR14 × SlAtrMultiplier / entry,
///     MinSlPct, MaxSlPct))</c>.
///   - Time-Stop  : <c>MaxHoldMinutes</c> (default 6) — ContextJson
///     <c>maxHoldMinutes</c> anahtarına yazılır (ADR-0017 §17.7 uyumlu).
///
/// Fallback (ATR14 = 0, warmup incomplete veya flat piyasa):
///   Evaluator <see cref="Parameters.TpGrossPct"/> / <see cref="Parameters.StopPct"/>
///   alanlarına düşer (backward-compat; default 0 ise MinTp/MinSl clip devreye
///   girer). Bu yol <c>ContextJson.mode = "atr-fallback"</c> olarak işaretlenir.
///
/// Evaluator hiçbir DB bağlantısı açmaz, rolling buffer yönetmez — tüm primitive
/// verileri <see cref="IMarketIndicatorService.TryGetMicroScalperSnapshot"/>
/// üzerinden alır; <c>Atr14</c> alanı Loop 33'te eklendi.
/// </summary>
public sealed class AtrScalperVwapEmaEvaluator : IStrategyEvaluator
{
    public StrategyType Type => StrategyType.AtrScalperVwapEma1m;

    private readonly IMarketIndicatorService _indicators;
    private readonly ILogger<AtrScalperVwapEmaEvaluator> _logger;

    public AtrScalperVwapEmaEvaluator(
        IMarketIndicatorService indicators,
        ILogger<AtrScalperVwapEmaEvaluator> logger)
    {
        _indicators = indicators;
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

        // Loop 37 — KlineInterval param ile snapshot buffer seçimi. 5m timeframe
        // migrasyonu: Loop 33-36'da 1m bar body × MaxHold potansiyeli TP hit
        // için matematiksel yetersiz; 5m'e geçiş TP hit olasılığını %50+ artırır.
        var interval = ParseIntervalOrDefault(p.KlineInterval);
        var snapshot = _indicators.TryGetMicroScalperSnapshot(symbol, interval);
        if (snapshot is null)
        {
            _logger.LogDebug(
                "AtrScalper snapshot not ready symbol={Symbol} strategyId={StrategyId} interval={Interval}",
                symbol, strategyId, interval);
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        // Pre-warmup safety — degenerate zero values protect against flat / empty
        // volume windows. Atr14 == 0 is tolerated here (fallback path below).
        if (snapshot.Vwap <= 0m
            || snapshot.VolumeSma20 <= 0m
            || snapshot.Ema20Now <= 0m
            || snapshot.Ema20Prev <= 0m)
        {
            _logger.LogDebug(
                "AtrScalper snapshot incomplete symbol={Symbol} vwap={Vwap} volSma={Vol} ema={Ema}",
                symbol, snapshot.Vwap, snapshot.VolumeSma20, snapshot.Ema20Now);
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        // §18.8 style — direction gate with negative slope tolerance allowed by
        // default (pullback context).
        var slopeGateLevel = snapshot.Ema20Prev * (1m + p.SlopeTolerance);
        var directionGate = snapshot.Ema20Now > slopeGateLevel;

        var vwapContext = snapshot.PrevBarClose < snapshot.Vwap;

        var vwapAboveReclaim = snapshot.LastBarClose > snapshot.Vwap;
        var vwapDistance = snapshot.Vwap > 0m
            ? Math.Abs(snapshot.LastBarClose - snapshot.Vwap) / snapshot.Vwap
            : decimal.MaxValue;
        var vwapZoneOk = p.VwapTolerancePct > 0m && vwapDistance < p.VwapTolerancePct;
        var vwapReclaim = vwapAboveReclaim || vwapZoneOk;

        var volumeRatio = snapshot.VolumeSma20 > 0m
            ? snapshot.LastBarVolume / snapshot.VolumeSma20
            : 0m;
        var volumeConfirm = volumeRatio >= p.VolumeMultiplier;

        if (!directionGate || !vwapContext || !vwapReclaim || !volumeConfirm)
        {
            var slope = snapshot.Ema20Prev > 0m
                ? (snapshot.Ema20Now - snapshot.Ema20Prev) / snapshot.Ema20Prev
                : 0m;
            _logger.LogDebug(
                "AtrScalper skip symbol={Symbol} slope={Slope} slopeGate={GateLevel} " +
                "vwapCtx={Below} reclaim={Reclaim} vwapZoneOk={ZoneOk} volRatio={Ratio} decision=Skip",
                symbol, slope, slopeGateLevel,
                vwapContext, vwapAboveReclaim, vwapZoneOk, volumeRatio);
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        var entryPrice = snapshot.LastBarClose;

        // AR-GE §5 — ATR14 tabanlı dinamik geometri. ATR14 = 0 fallback'inde
        // backward-compat TpGrossPct / StopPct alanlarına düşer; her iki modda
        // da Min/Max clip uygulanır (SOL/ADA ATR saatlik %0.3–0.8 — tipik
        // değerlerde clip bağlayıcı değil).
        decimal tpPct;
        decimal slPct;
        string mode;

        if (snapshot.Atr14 > 0m && entryPrice > 0m)
        {
            var rawTpPct = snapshot.Atr14 * p.TpAtrMultiplier / entryPrice;
            var rawSlPct = snapshot.Atr14 * p.SlAtrMultiplier / entryPrice;
            tpPct = Clamp(rawTpPct, p.MinTpPct, p.MaxTpPct);
            slPct = Clamp(rawSlPct, p.MinSlPct, p.MaxSlPct);
            mode = "atr-dynamic";
        }
        else
        {
            // Fallback — backward-compat with MicroScalper fixed-pct contract.
            // Clip still applies so zero defaults don't produce invalid geometry.
            tpPct = Clamp(p.TpGrossPct, p.MinTpPct, p.MaxTpPct);
            slPct = Clamp(p.StopPct, p.MinSlPct, p.MaxSlPct);
            mode = "atr-fallback";
        }

        var takeProfit = entryPrice * (1m + tpPct);
        var stopPrice = entryPrice * (1m - slPct);

        if (takeProfit <= entryPrice || stopPrice >= entryPrice)
        {
            _logger.LogDebug(
                "AtrScalper geometry invalid symbol={Symbol} entry={Entry} stop={Stop} tp={Tp} mode={Mode}",
                symbol, entryPrice, stopPrice, takeProfit, mode);
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        var slopeRatio = snapshot.Ema20Prev > 0m
            ? (snapshot.Ema20Now - snapshot.Ema20Prev) / snapshot.Ema20Prev
            : 0m;

        // ContextJson — TimeStop handler (ADR-0017 §17.7) `maxHoldMinutes` key
        // şartı korunur. Ek olarak ATR metadatası loglamaya akar.
        var ctx = EvaluatorParameterHelper.SerializeContext(new
        {
            type = "atr-scalper-vwap-ema-1m",
            mode,
            vwap = snapshot.Vwap,
            ema20Now = snapshot.Ema20Now,
            ema20Prev = snapshot.Ema20Prev,
            slope = slopeRatio,
            prevBarClose = snapshot.PrevBarClose,
            lastBarClose = snapshot.LastBarClose,
            vwapDistance,
            volumeRatio,
            atr14 = snapshot.Atr14,
            tpAtrMultiplier = p.TpAtrMultiplier,
            slAtrMultiplier = p.SlAtrMultiplier,
            tpPct,
            slPct,
            maxHoldMinutes = p.MaxHoldMinutes,
        });

        _logger.LogInformation(
            "AtrScalper emit symbol={Symbol} entry={Entry} stop={Stop} tp={Tp} " +
            "atr14={Atr} tpPct={TpPct} slPct={SlPct} mode={Mode} decision=Emit",
            symbol, entryPrice, stopPrice, takeProfit,
            snapshot.Atr14, tpPct, slPct, mode);

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
    /// Loop 37 — seed JSON <c>KlineInterval</c> string'ini enum'a çevirir.
    /// Desteklenen değerler: <c>"1m"</c> → <see cref="KlineInterval.OneMinute"/>,
    /// <c>"5m"</c> → <see cref="KlineInterval.FiveMinutes"/>. Diğer her şey
    /// <see cref="KlineInterval.OneMinute"/> fallback. Buffer mevcut değilse
    /// MarketIndicatorService snapshot'ı null döner → evaluator erken çıkar.
    /// </summary>
    private static KlineInterval ParseIntervalOrDefault(string? raw) =>
        raw switch
        {
            "1m" => KlineInterval.OneMinute,
            "5m" => KlineInterval.FiveMinutes,
            _ => KlineInterval.OneMinute,
        };

    /// <summary>
    /// AR-GE §5 ve Implementation Roadmap — parametre kontratı. Serialised as
    /// <c>Strategies.Seed[].ParametersJson</c>. MicroScalper ile uyumlu taban +
    /// yeni ATR alanları.
    /// </summary>
    private sealed class Parameters
    {
        public string KlineInterval { get; set; } = "1m";
        public string EmaTimeframe { get; set; } = "1m";
        public int EmaPeriod { get; set; } = 20;

        public int VwapWindowBars { get; set; } = 15;
        public decimal VwapTolerancePct { get; set; } = 0.008m;

        public int VolumeSmaBars { get; set; } = 20;
        public decimal VolumeMultiplier { get; set; } = 0.5m;

        public decimal SlopeTolerance { get; set; } = -0.003m;

        // Loop 33 AR-GE §5 — ATR-adaptif TP/SL.
        public int AtrPeriod { get; set; } = 14;
        public decimal TpAtrMultiplier { get; set; } = 1.5m;
        public decimal SlAtrMultiplier { get; set; } = 0.8m;

        // Clip çiti — degenerate ATR (0 veya flat piyasa) + aşırı vol spike
        // için üst/alt emniyet. AR-GE SOL önerisi: TP [0.005, 0.012].
        public decimal MinTpPct { get; set; } = 0.004m;
        public decimal MaxTpPct { get; set; } = 0.012m;
        public decimal MinSlPct { get; set; } = 0.002m;
        public decimal MaxSlPct { get; set; } = 0.008m;

        // Backward-compat fallback alanları. ATR14 = 0 durumunda kullanılır.
        public decimal TpGrossPct { get; set; } = 0m;
        public decimal StopPct { get; set; } = 0m;

        public int MaxHoldMinutes { get; set; } = 6;
    }
}
