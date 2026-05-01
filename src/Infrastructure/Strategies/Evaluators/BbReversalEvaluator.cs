using BinanceBot.Application.Abstractions;
using BinanceBot.Application.Strategies.Cooldowns;
using BinanceBot.Application.Strategies.Evaluation;
using BinanceBot.Application.Strategies.Indicators;
using BinanceBot.Domain.MarketData;
using BinanceBot.Domain.Strategies;
using Microsoft.Extensions.Logging;

namespace BinanceBot.Infrastructure.Strategies.Evaluators;

/// <summary>
/// Loop 79 — Bollinger Band Reversal evaluator. Multi-regime switch'in
/// <b>Range</b> ayağı; KMS (Trending) ile paralel çalışır. Long-only spot paper.
///
/// Pazar Loop 71-78 boyunca dead-range tarzında dolaştı (8h cumulative -%1.11):
/// KMS oversold-recovery tek başına yetersiz — düşük hareket içinde EMA200 +
/// momentum şartları ya hiç doluyor ya da yanlış zamanda. BB Reversal mean-
/// reversion mantığıyla aynı dead-range'de "alt band'a değdi → orta band'a
/// dön" geometrisini yakalar.
///
/// Entry koşulları (AND, hard filter, skor sistemi YOK):
/// <list type="number">
///   <item><b>Range regime</b>: <c>BbwRangeMin &lt;= BBW &lt;= BbwRangeMax</c>.
///         Dead (BBW küçük) veya trending (BBW büyük) ⇒ skip (KMS bölgesi).</item>
///   <item><b>Lower band yakınlık</b>: <c>close &lt; BollingerLower × (1 + BufferPctEntry)</c>
///         (default %0.05 buffer — false breakdown koruma).</item>
///   <item><b>Oversold</b>: <c>Rsi14 &lt; RsiOversoldEntry</c>.</item>
///   <item><b>RSI dip recovery</b>: <c>Rsi14 &gt; Rsi14Prev</c>.</item>
///   <item><b>Spread</b>: <c>(Ask-Bid)/Ask &lt; SpreadThresholdPct</c>.</item>
///   <item><b>Cooldown</b>: son <c>CooldownBarsAfterSignal</c> bar BB Reversal yok.</item>
/// </list>
///
/// Çıkış geometrisi (sabit, ATR-bağımsız — mean reversion bant'a hedeflenir):
///   - <b>TP</b>: <c>BollingerMean</c> (orta band, 20-bar SMA).
///   - <b>SL</b>: <c>entryPrice × (1 - BufferPctExit)</c> (default %0.1).
///   - <b>MaxHold</b>: <c>MaxHoldMinutes</c> (default 20dk = 4 bar × 5m).
///
/// Risk uyarıları (binance-expert spec):
///   1. False breakdown — RSI rising koruyucu ama %100 garanti değil.
///   2. Round-trip fee %0.2 → net kar için WR &gt; %67 zorunlu (TP %0.3 / SL %0.1, 3:1).
///   3. Testnet spread mainnet'in 3-8 katı; paper-fill etkisi kısmen abartılır.
///   4. KMS+BBR aynı coin'de fill olabilir (max-pos config tek başına yeterli;
///      duplicate-symbol guard opsiyonel, Loop 80 değerlendirme).
///
/// Result-flow: skip ⇒ <c>null</c>; signal ⇒ <see cref="StrategyEvaluation"/>.
/// Exception kontrol akışı için kullanılmaz.
/// </summary>
public sealed class BbReversalEvaluator : IStrategyEvaluator
{
    public StrategyType Type => StrategyType.BollingerBandReversal5m;

    private readonly IMarketIndicatorService _indicators;
    private readonly IBookTickerReader _bookTickers;
    private readonly ICooldownService _cooldown;
    private readonly IClock _clock;
    private readonly ILogger<BbReversalEvaluator> _logger;

    // BB Reversal stratejisi 5m bar üzerinde çalışır — cooldown penceresi
    // 5dk çarpanıyla yorumlanır. Sabit, parametre dışı (interval-bound).
    private const int BarMinutes5m = 5;

    public BbReversalEvaluator(
        IMarketIndicatorService indicators,
        IBookTickerReader bookTickers,
        ICooldownService cooldown,
        IClock clock,
        ILogger<BbReversalEvaluator> logger)
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

        var snapshot = _indicators.TryGetBbReversalSnapshot(
            symbol,
            rsiPeriod: p.RsiPeriod,
            bbPeriod: p.BbPeriod,
            bbStdDev: p.BbStdDev,
            atrPeriod: p.AtrPeriod);

        if (snapshot is null)
        {
            _logger.LogDebug(
                "BBR snapshot not ready symbol={Symbol} strategyId={StrategyId}",
                symbol, strategyId);
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        // Pre-warmup safety — degenerate flat market.
        if (snapshot.CurrentClose <= 0m
            || snapshot.BollingerMean <= 0m
            || snapshot.BollingerLower <= 0m)
        {
            _logger.LogDebug(
                "BBR snapshot incomplete symbol={Symbol} close={Close} bbMid={Mid} bbLower={Low}",
                symbol, snapshot.CurrentClose, snapshot.BollingerMean, snapshot.BollingerLower);
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        // ── Regime kontrolü: Range = [BbwRangeMin, BbwRangeMax] ──────────
        // Dead (BBW < min) ⇒ sıkışık, breakout riski / fee yer.
        // Trending (BBW > max) ⇒ KMS bölgesi, mean-reversion ters yön.
        var bbw = snapshot.BollingerBandWidth;
        if (bbw < p.BbwRangeMin || bbw > p.BbwRangeMax)
        {
            var regime = bbw < p.BbwRangeMin ? "Dead" : "Trending";
            _logger.LogInformation(
                "BBR skip regime symbol={Symbol} strategyId={StrategyId} " +
                "bbw={Bbw} rangeMin={Min} rangeMax={Max} regime={Regime} decision=RegimeSkip",
                symbol, strategyId, bbw, p.BbwRangeMin, p.BbwRangeMax, regime);
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        // ── Entry koşulları (AND) ─────────────────────────────────────────
        // 1) Lower band yakınlık (false breakdown buffer dahil).
        var lowerEntryThreshold = snapshot.BollingerLower * (1m + p.BufferPctEntry);
        var nearLowerBand = snapshot.CurrentClose < lowerEntryThreshold;

        // 2) Oversold.
        var oversold = snapshot.Rsi14 < p.RsiOversoldEntry;

        // 3) RSI dip recovery (dip yapıp dönüyor).
        var rsiRecovery = snapshot.Rsi14 > snapshot.Rsi14Prev;

        if (!nearLowerBand || !oversold || !rsiRecovery)
        {
            _logger.LogDebug(
                "BBR skip entry symbol={Symbol} strategyId={StrategyId} " +
                "close={Close} bbLower={Low} threshold={Thr} nearLower={Near} " +
                "rsi={Rsi} rsiPrev={RsiPrev} oversold={OS} recovery={Rec} decision=EntrySkip",
                symbol, strategyId,
                snapshot.CurrentClose, snapshot.BollingerLower, lowerEntryThreshold, nearLowerBand,
                snapshot.Rsi14, snapshot.Rsi14Prev, oversold, rsiRecovery);
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        // 4) Spread filter (canlı BookTicker — yetersiz veri ⇒ skip).
        var ticker = _bookTickers.GetLatest(symbol);
        if (ticker is null || ticker.AskPrice <= 0m || ticker.BidPrice <= 0m)
        {
            _logger.LogDebug(
                "BBR skip no booktickers symbol={Symbol} strategyId={StrategyId}",
                symbol, strategyId);
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        var spreadPct = (ticker.AskPrice - ticker.BidPrice) / ticker.AskPrice;
        if (spreadPct >= p.SpreadThresholdPct)
        {
            _logger.LogInformation(
                "BBR skip spread symbol={Symbol} strategyId={StrategyId} " +
                "spreadPct={Spread} threshold={Thr} decision=SpreadSkip",
                symbol, strategyId, spreadPct, p.SpreadThresholdPct);
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        // 5) Cooldown gate — sinyal koşulları sağlandıktan SONRA. Ucuz
        // filtreler önce, state lookup sonra (KMS pattern).
        var now = _clock.UtcNow;
        if (_cooldown.IsCooldown(strategyId, symbol, p.CooldownBarsAfterSignal, BarMinutes5m, now))
        {
            _logger.LogInformation(
                "BBR cooldown skip symbol={Symbol} strategyId={StrategyId} " +
                "cooldownBars={Bars} barMinutes={BarMinutes} decision=CooldownSkip",
                symbol, strategyId, p.CooldownBarsAfterSignal, BarMinutes5m);
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        // ── Geometri ──────────────────────────────────────────────────────
        // TP = BB middle (mean reversion). SL = entry × (1 - BufferPctExit).
        // ATR-bağımsız — bant geometrisi zaten volatilite içeriyor.
        var entryPrice = snapshot.CurrentClose;
        var takeProfit = snapshot.BollingerMean;
        var stopPrice = entryPrice * (1m - p.BufferPctExit);

        if (takeProfit <= entryPrice || stopPrice >= entryPrice)
        {
            _logger.LogDebug(
                "BBR geometry invalid symbol={Symbol} entry={Entry} stop={Stop} tp={Tp}",
                symbol, entryPrice, stopPrice, takeProfit);
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        var tpPct = (takeProfit - entryPrice) / entryPrice;
        var slPct = (entryPrice - stopPrice) / entryPrice;

        var ctx = EvaluatorParameterHelper.SerializeContext(new
        {
            type = "bb-reversal-5m",
            entryReason = "lower_band_oversold_recovery",
            regime = "Range",
            rsi14 = snapshot.Rsi14,
            rsi14Prev = snapshot.Rsi14Prev,
            bbLower = snapshot.BollingerLower,
            bbMean = snapshot.BollingerMean,
            bbw,
            bbwRangeMin = p.BbwRangeMin,
            bbwRangeMax = p.BbwRangeMax,
            atr14 = snapshot.Atr14,
            askPrice = ticker.AskPrice,
            bidPrice = ticker.BidPrice,
            spreadPct,
            currentClose = snapshot.CurrentClose,
            lowerEntryThreshold,
            bufferPctEntry = p.BufferPctEntry,
            bufferPctExit = p.BufferPctExit,
            tpPct,
            slPct,
            maxHoldMinutes = p.MaxHoldMinutes,
            cooldownBarsAfterSignal = p.CooldownBarsAfterSignal,
            lastBarOpenTime = snapshot.LastBarOpenTime,
        });

        _logger.LogInformation(
            "BBR emit symbol={Symbol} strategyId={StrategyId} " +
            "close={Close} bbLower={Low} bbMid={Mid} bbw={Bbw} " +
            "rsi={Rsi} rsiPrev={RsiPrev} spreadPct={Spread} " +
            "entry={Entry} stop={Stop} tp={Tp} tpPct={TpPct} slPct={SlPct} " +
            "maxHold={MaxHold} decision=Emit",
            symbol, strategyId,
            snapshot.CurrentClose, snapshot.BollingerLower, snapshot.BollingerMean, bbw,
            snapshot.Rsi14, snapshot.Rsi14Prev, spreadPct,
            entryPrice, stopPrice, takeProfit, tpPct, slPct,
            p.MaxHoldMinutes);

        // Sinyal yayını öncesi cooldown timestamp kaydı. Geometri valid path;
        // skip path'lerinde kayıt yapılmaz, böylece reddedilmiş sinyaller
        // sonraki bar'ı bloke etmez (KMS pattern).
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

    /// <summary>
    /// Loop 79 BB Reversal parametre kontratı. Serialised as
    /// <c>Strategies.Seed[].ParametersJson</c>. Default'lar binance-expert spec.
    /// </summary>
    private sealed class Parameters
    {
        // Periyotlar.
        public int RsiPeriod { get; set; } = 14;
        public int BbPeriod { get; set; } = 20;
        public decimal BbStdDev { get; set; } = 2.0m;
        public int AtrPeriod { get; set; } = 14;

        // Range regime BBW eşikleri (binance-expert spec).
        public decimal BbwRangeMin { get; set; } = 0.003m;
        public decimal BbwRangeMax { get; set; } = 0.010m;

        // Entry filtreleri.
        public decimal RsiOversoldEntry { get; set; } = 35m;
        public decimal BufferPctEntry { get; set; } = 0.0005m; // %0.05
        public decimal SpreadThresholdPct { get; set; } = 0.005m;

        // Çıkış geometrisi.
        public decimal BufferPctExit { get; set; } = 0.001m;   // %0.1 SL
        public int MaxHoldMinutes { get; set; } = 20;          // 4 bar × 5m

        // Cooldown.
        public int CooldownBarsAfterSignal { get; set; } = 3;
    }
}
