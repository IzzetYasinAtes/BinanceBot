using BinanceBot.Application.Abstractions;
using BinanceBot.Application.Strategies.Cooldowns;
using BinanceBot.Application.Strategies.Evaluation;
using BinanceBot.Application.Strategies.Indicators;
using BinanceBot.Domain.MarketData;
using BinanceBot.Domain.Strategies;
using Microsoft.Extensions.Logging;

namespace BinanceBot.Infrastructure.Strategies.Evaluators;

/// <summary>
/// Loop 67 KMS — KlineMomentumSpread5m evaluator. Long-only spot paper.
///
/// Mantık (binance-expert spec): 5m bar kapanışında — RSI oversold recovery
/// + EMA9 pozitif slope + TradeCount surge + BookTicker spread kontrol +
/// MinAtrPct → mikro long entry.
///
/// Giriş AND koşulları (5):
/// <list type="number">
///   <item>RSI Recovery — <c>Rsi14 &gt; RsiRecoveryThreshold AND
///         Rsi14Prev &lt; RsiRecoveryThreshold</c>. Önceki bar oversold,
///         mevcut bar çıkış sinyali.</item>
///   <item>EMA9 Slope — <c>Ema9Now &gt; Ema9Prev</c>. Kısa-erim pozitif
///         eğim.</item>
///   <item>TradeCount Surge — <c>CurrentTradeCount &gt;
///         AvgTradeCount20 × TradeCountMultiplier</c>.</item>
///   <item>Spread Filter — <c>(Ask − Bid) / Ask &lt; SpreadThresholdPct</c>.
///         Canlı <see cref="IBookTickerReader"/> okumasından; testnet
///         likiditesi düşük olduğu için eşik appsettings'te 0.0015.</item>
///   <item>MinAtrPct — <c>Atr14 / CurrentClose &gt;= MinAtrPct</c>. Sessiz
///         piyasa engeli.</item>
/// </list>
///
/// Çıkış (ATR-tabanlı adaptif geometri):
///   - Take-Profit: <c>entry × (1 + clamp(Atr14 × TpAtrMultiplier / entry,
///     MinTpPct, MaxTpPct))</c> → default %0.50–1.80.
///   - Stop-Loss  : <c>entry × (1 − clamp(Atr14 × SlAtrMultiplier / entry,
///     MinSlPct, MaxSlPct))</c> → default %0.30–0.80.
///   - Time-Stop  : <c>MaxHoldMinutes</c> default 45 (9 bar × 5m); ContextJson
///     <c>maxHoldMinutes</c> anahtarına yazılır (ADR-0017 §17.7).
///
/// Cooldown: <see cref="ICooldownService"/> singleton üzerinden evaluator-level
/// enforce edilir. Default 3 bar (15dk) — 5m timeframe ile uyumlu.
///
/// Result-flow: skip ⇒ <c>null</c>; signal ⇒ <see cref="StrategyEvaluation"/>.
/// Exception kontrol akışı için kullanılmaz — invalid geometry / yetersiz
/// warmup / spread veri yok defensive null path.
/// </summary>
public sealed class KmsMomentumEvaluator : IStrategyEvaluator
{
    public StrategyType Type => StrategyType.KlineMomentumSpread5m;

    private readonly IMarketIndicatorService _indicators;
    private readonly IBookTickerReader _bookTickers;
    private readonly ICooldownService _cooldown;
    private readonly IClock _clock;
    private readonly ILogger<KmsMomentumEvaluator> _logger;

    // KMS strateji 5m bar üzerinde çalışır — cooldown penceresi 5dk çarpanıyla
    // yorumlanır. Sabit, parametre dışı (interval-bound).
    private const int BarMinutes5m = 5;

    public KmsMomentumEvaluator(
        IMarketIndicatorService indicators,
        IBookTickerReader bookTickers,
        ICooldownService cooldown,
        IClock clock,
        ILogger<KmsMomentumEvaluator> logger)
    {
        _indicators = indicators;
        _bookTickers = bookTickers;
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

        var snapshot = _indicators.TryGetKmsMomentumSnapshot(
            symbol,
            rsiPeriod: p.RsiPeriod,
            emaPeriod: p.EmaPeriod,
            atrPeriod: p.AtrPeriod,
            tradeCountWindow: p.TradeCountWindow);

        if (snapshot is null)
        {
            _logger.LogDebug(
                "KMS snapshot not ready symbol={Symbol} strategyId={StrategyId}",
                symbol, strategyId);
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        // Pre-warmup safety — degenerate zero values (flat market / not warm).
        if (snapshot.CurrentClose <= 0m
            || snapshot.Ema9Now <= 0m
            || snapshot.AvgTradeCount20 <= 0m)
        {
            _logger.LogDebug(
                "KMS snapshot incomplete symbol={Symbol} close={Close} ema9={Ema9} tcAvg={TcAvg}",
                symbol, snapshot.CurrentClose, snapshot.Ema9Now, snapshot.AvgTradeCount20);
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        // 1) RSI Recovery — önceki bar threshold altı, current üstü.
        var rsiRecoveryOk =
            snapshot.Rsi14 > p.RsiRecoveryThreshold
            && snapshot.Rsi14Prev < p.RsiRecoveryThreshold;

        // 2) EMA9 pozitif slope.
        var slopeOk = snapshot.Ema9Now > snapshot.Ema9Prev;

        // 3) TradeCount surge.
        var surgeOk = snapshot.CurrentTradeCount >
            snapshot.AvgTradeCount20 * p.TradeCountMultiplier;

        // 5) MinAtr filtresi (4 spread için ayrı blok aşağıda).
        var atrPct = snapshot.CurrentClose > 0m
            ? snapshot.Atr14 / snapshot.CurrentClose
            : 0m;
        var atrOk = snapshot.Atr14 > 0m && atrPct >= p.MinAtrPct;

        if (!rsiRecoveryOk || !slopeOk || !surgeOk || !atrOk)
        {
            _logger.LogDebug(
                "KMS skip filters symbol={Symbol} rsi={Rsi} rsiPrev={RsiPrev} thr={Thr} " +
                "ema9={Ema9} ema9Prev={Ema9Prev} tcCur={TcCur} tcAvg={TcAvg} mult={Mult} " +
                "atrPct={AtrPct} minAtr={MinAtr} decision=Skip",
                symbol, snapshot.Rsi14, snapshot.Rsi14Prev, p.RsiRecoveryThreshold,
                snapshot.Ema9Now, snapshot.Ema9Prev,
                snapshot.CurrentTradeCount, snapshot.AvgTradeCount20, p.TradeCountMultiplier,
                atrPct, p.MinAtrPct);
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        // 4) Spread filter — canlı BookTicker okuması; payload yoksa skip.
        var ticker = _bookTickers.GetLatest(symbol);
        if (ticker is null || ticker.AskPrice <= 0m || ticker.BidPrice <= 0m)
        {
            _logger.LogDebug(
                "KMS skip no booktickers symbol={Symbol} strategyId={StrategyId}",
                symbol, strategyId);
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        var spreadPct = (ticker.AskPrice - ticker.BidPrice) / ticker.AskPrice;
        if (spreadPct >= p.SpreadThresholdPct)
        {
            _logger.LogDebug(
                "KMS skip wide spread symbol={Symbol} bid={Bid} ask={Ask} spreadPct={Spread} thr={Thr}",
                symbol, ticker.BidPrice, ticker.AskPrice, spreadPct, p.SpreadThresholdPct);
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        // Cooldown gate — sinyal koşulları sağlandıktan SONRA. Maliyet sırası
        // kasıtlı: önce ucuz indikatör/spread filtreleri, sonra cooldown
        // (state lookup) ve geometri.
        var now = _clock.UtcNow;
        if (_cooldown.IsCooldown(strategyId, symbol, p.CooldownBarsAfterSignal, BarMinutes5m, now))
        {
            _logger.LogInformation(
                "KMS cooldown skip symbol={Symbol} strategyId={StrategyId} " +
                "cooldownBars={Bars} barMinutes={BarMinutes} decision=CooldownSkip",
                symbol, strategyId, p.CooldownBarsAfterSignal, BarMinutes5m);
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        var entryPrice = snapshot.CurrentClose;

        // ATR-tabanlı TP/SL geometrisi + Min/Max clip. Atr14 > 0 garanti
        // (atrOk true).
        var rawTpPct = snapshot.Atr14 * p.TpAtrMultiplier / entryPrice;
        var rawSlPct = snapshot.Atr14 * p.SlAtrMultiplier / entryPrice;
        var tpPct = Clamp(rawTpPct, p.MinTpPct, p.MaxTpPct);
        var slPct = Clamp(rawSlPct, p.MinSlPct, p.MaxSlPct);

        var takeProfit = entryPrice * (1m + tpPct);
        var stopPrice = entryPrice * (1m - slPct);

        if (takeProfit <= entryPrice || stopPrice >= entryPrice)
        {
            _logger.LogDebug(
                "KMS geometry invalid symbol={Symbol} entry={Entry} stop={Stop} tp={Tp}",
                symbol, entryPrice, stopPrice, takeProfit);
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        var ctx = EvaluatorParameterHelper.SerializeContext(new
        {
            type = "kms-momentum-5m",
            entryReason = "rsi_recovery_ema_slope_tc_surge",
            rsi14 = snapshot.Rsi14,
            rsi14Prev = snapshot.Rsi14Prev,
            ema9Now = snapshot.Ema9Now,
            ema9Prev = snapshot.Ema9Prev,
            atr14 = snapshot.Atr14,
            atrPct,
            tradeCountAvg20 = snapshot.AvgTradeCount20,
            currentTradeCount = snapshot.CurrentTradeCount,
            askPrice = ticker.AskPrice,
            bidPrice = ticker.BidPrice,
            spreadPct,
            currentClose = snapshot.CurrentClose,
            tpAtrMultiplier = p.TpAtrMultiplier,
            slAtrMultiplier = p.SlAtrMultiplier,
            tpPct,
            slPct,
            maxHoldMinutes = p.MaxHoldMinutes,
            cooldownBarsAfterSignal = p.CooldownBarsAfterSignal,
            lastBarOpenTime = snapshot.LastBarOpenTime,
        });

        _logger.LogInformation(
            "KMS emit symbol={Symbol} entry={Entry} stop={Stop} tp={Tp} " +
            "rsi={Rsi} rsiPrev={RsiPrev} ema9={Ema9} ema9Prev={Ema9Prev} " +
            "tcCur={TcCur} tcAvg={TcAvg} spreadPct={Spread} atr14={Atr} " +
            "tpPct={TpPct} slPct={SlPct} decision=Emit",
            symbol, entryPrice, stopPrice, takeProfit,
            snapshot.Rsi14, snapshot.Rsi14Prev, snapshot.Ema9Now, snapshot.Ema9Prev,
            snapshot.CurrentTradeCount, snapshot.AvgTradeCount20, spreadPct,
            snapshot.Atr14, tpPct, slPct);

        // Sinyal yayını öncesi cooldown timestamp kaydı. Geometri valid (Emit
        // path); skip path'lerinde kayıt yapılmaz, böylece reddedilmiş
        // sinyaller sonraki bar'ı bloke etmez.
        _cooldown.RecordSignal(strategyId, symbol, now);

        // SuggestedQuantity placeholder — fan-out handler sizing service üzerinden
        // gerçek miktarı hesaplar (ADR-0021 %20 equity / floor).
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
    /// <c>Strategies.Seed[].ParametersJson</c>. Default'lar testnet 5m
    /// baseline (RSI threshold 32, TradeCount × 1.1 testnet, Spread 0.0015).
    /// </summary>
    private sealed class Parameters
    {
        // RSI oversold recovery cross.
        public int RsiPeriod { get; set; } = 14;
        public decimal RsiRecoveryThreshold { get; set; } = 32m;

        // EMA9 slope.
        public int EmaPeriod { get; set; } = 9;

        // TradeCount surge.
        public int TradeCountWindow { get; set; } = 20;
        public decimal TradeCountMultiplier { get; set; } = 1.5m;

        // ATR ve TP/SL geometrisi.
        public int AtrPeriod { get; set; } = 14;
        public decimal TpAtrMultiplier { get; set; } = 1.8m;
        public decimal SlAtrMultiplier { get; set; } = 0.75m;

        public decimal MinTpPct { get; set; } = 0.005m;
        public decimal MaxTpPct { get; set; } = 0.018m;
        public decimal MinSlPct { get; set; } = 0.003m;
        public decimal MaxSlPct { get; set; } = 0.008m;

        // Time-Stop — 9 bar × 5m = 45dk.
        public int MaxHoldMinutes { get; set; } = 45;

        // Sessiz piyasa filtresi: ATR/Close < %0.05 ⇒ sinyal yok.
        public decimal MinAtrPct { get; set; } = 0.0005m;

        // Spread filter — testnet liquidity düşük; eşik appsettings 0.0015.
        public decimal SpreadThresholdPct { get; set; } = 0.0005m;

        // Cooldown — 3 bar × 5dk = 15dk.
        public int CooldownBarsAfterSignal { get; set; } = 3;
    }
}
