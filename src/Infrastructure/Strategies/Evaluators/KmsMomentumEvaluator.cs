using BinanceBot.Application.Abstractions;
using BinanceBot.Application.Strategies.Cooldowns;
using BinanceBot.Application.Strategies.Evaluation;
using BinanceBot.Application.Strategies.Indicators;
using BinanceBot.Domain.MarketData;
using BinanceBot.Domain.Strategies;
using Microsoft.Extensions.Logging;

namespace BinanceBot.Infrastructure.Strategies.Evaluators;

/// <summary>
/// Loop 71 KMS — KlineMomentumSpread5m skor-tabanlı evaluator. Long-only spot
/// paper. Loop 70 sonu: AND gate yapısı parametre tune ile saatte 5-15 emit
/// hedefine ulaşamadı (over-restrictive). Yeni model: 6 puan üzerinden
/// gate-skorlama; min 4/6 emit + spread/MinAtr hard skip + RSI Zone hard
/// gate (≥1 puan zorunlu).
///
/// Loop 77 — EMA200 trend gate + BBW (Bollinger band-width) regime filter.
/// Loop 76 trailing-stop deploy başarılı oldu ama entry kalitesi hâlâ
/// problemli (downtrend long entry'leri büyük SL'lere yol açıyor). EMA200
/// hard-gate <c>closePrice &gt; EMA200</c> şartını skor hesabı ÖNCESİ
/// koyarak downtrend long'ları eler; <c>Ema200GateEnabled</c> toggle
/// 0-emit sigortası olarak korunur. BBW skor sistemine 1 puan nice-to-have
/// olarak girer (yüksek band-width = breakout dostu rejim) — hard-gate
/// DEĞİL, frekans korunur.
///
/// Gate skor tablosu (binance-expert spec, Loop 77):
/// <list type="bullet">
///   <item>RSI Zone (oversold + momentum) 0–2 puan — must-have (≥1).</item>
///   <item>EMA9 pozitif slope 0–1 puan — nice-to-have.</item>
///   <item>TradeCount surge 0–1 puan — nice-to-have.</item>
///   <item>Spread filtresi 0–1 puan — hard-gate (0 ⇒ skip).</item>
///   <item>MinAtrPct (CoinClass-aware) 0–1 puan — hard-gate (0 ⇒ skip).</item>
///   <item>BBW regime 0–1 puan — nice-to-have (Loop 77).</item>
/// </list>
/// Toplam tavan 7 puan; <c>MinScoreThreshold</c> default 4 (Loop 76 ile
/// hizalı, frekans korunur).
///
/// RSI Zone (continuous, cross değil):
/// <list type="bullet">
///   <item>2 puan: <c>Rsi14 &lt; RsiOversoldZone &amp;&amp; Rsi14 &gt; Rsi14Prev</c>.</item>
///   <item>1 puan: <c>Rsi14 &lt; RsiNeutralCeiling &amp;&amp; Rsi14 &gt; Rsi14Prev</c>.</item>
///   <item>0 puan: diğer (RSI çok yüksek veya momentum negatif).</item>
/// </list>
///
/// CoinClass asimetri (BTC/ETH vs alt):
/// <list type="bullet">
///   <item>"large" ⇒ <c>MinAtrPctLarge</c> (default 0.0002) — BTC, ETH.</item>
///   <item>"mid"   ⇒ <c>MinAtrPctMid</c>   (default 0.0003) — SOL.</item>
///   <item>"alt"   ⇒ <c>MinAtrPctAlt</c>   (default 0.0004) — XRP, ADA.</item>
/// </list>
///
/// Skor-bazlı dinamik TP/SL/MaxHold (entry sonrası geometri kalitesi skor ile
/// ölçeklenir — yüksek skor ⇒ geniş TP, dar SL, uzun hold):
/// <list type="bullet">
///   <item>4/6 ⇒ TpMul=Low(1.5), SlMul=Low(0.85), MaxHold=30dk.</item>
///   <item>5/6 ⇒ TpMul=Mid(1.8), SlMul=Mid(0.75), MaxHold=45dk.</item>
///   <item>6/6 ⇒ TpMul=High(2.2), SlMul=High(0.65), MaxHold=60dk.</item>
/// </list>
///
/// Streak guard: <see cref="ICooldownService.GetCurrentScoreThreshold"/> aktif
/// minimum skor eşiğini döner. Şimdilik sabit 4 (Loop 72: ardışık SL sonrası
/// 5/6'ya bump).
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

        // Loop 77 — EMA200 trend hard-gate (skor öncesi). Spec:
        //   if (Ema200GateEnabled && Ema200 > 0 && CurrentClose <= Ema200) skip.
        //   Ema200 == 0 ⇒ warmup yetersiz (200 bar dolmadı) ⇒ gate açık.
        //   Toggle false ⇒ acil 0-emit sigortası, gate bypass.
        if (p.Ema200GateEnabled
            && snapshot.Ema200 > 0m
            && snapshot.CurrentClose <= snapshot.Ema200)
        {
            _logger.LogInformation(
                "KMS skip ema200_gate symbol={Symbol} strategyId={StrategyId} " +
                "close={Close} ema200={Ema200} decision=Ema200GateSkip",
                symbol, strategyId, snapshot.CurrentClose, snapshot.Ema200);
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        // Loop 78 — BBW hard-gate (Loop 77 t120 4 ardışık big SL → CB tripped post-mortem).
        // Pratikte BBW=0 (sıkışık bant, zayıf trend) emit'leri sürekli loss veriyor:
        // EMA200 üstü uptrend filtresinden geçen ama BBW < threshold (volatilite yok)
        // sinyaller "false breakout" tuzağı oluşturuyor. Bu kontrol BBW score
        // hesabından önce gelir; <c>BbwHardGate=true</c> ⇒ <c>BollingerBandWidth</c>
        // <c>BbwThreshold</c>'un altındaysa skip (skor toplama bakılmaz).
        // Default false (geriye uyumluluk); appsettings seed'lerde true ile deploy.
        if (p.BbwHardGate && snapshot.BollingerBandWidth < p.BbwThreshold)
        {
            _logger.LogInformation(
                "KMS skip bbw_hard_gate symbol={Symbol} strategyId={StrategyId} " +
                "bbw={Bbw} threshold={Threshold} decision=BbwHardGateSkip",
                symbol, strategyId, snapshot.BollingerBandWidth, p.BbwThreshold);
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        // CoinClass asimetri çözümü — BTC/ETH (large) düşük ATR'da bile likit;
        // altcoin (alt) çok düşük ATR = trend yok / pump-dump tuzağı, daha
        // yüksek MinAtr aranır. Bilinmeyen sınıf "alt" varsayılır (en
        // muhafazakar).
        var coinClass = NormalizeCoinClass(p.CoinClass);
        var minAtrPct = coinClass switch
        {
            "large" => p.MinAtrPctLarge,
            "mid" => p.MinAtrPctMid,
            _ => p.MinAtrPctAlt,
        };

        // ── GATE SKORLAMA ─────────────────────────────────────────────────
        // Spread payload'ı önceden çekilir — BookTicker yoksa skip; "yetersiz
        // veri" durumunda emit yasak (defensive). Spread skoru aşağıda hesaplanır.
        var ticker = _bookTickers.GetLatest(symbol);
        if (ticker is null || ticker.AskPrice <= 0m || ticker.BidPrice <= 0m)
        {
            _logger.LogDebug(
                "KMS skip no booktickers symbol={Symbol} strategyId={StrategyId}",
                symbol, strategyId);
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        var spreadPct = (ticker.AskPrice - ticker.BidPrice) / ticker.AskPrice;
        var spreadScore = spreadPct < p.SpreadThresholdPct ? 1 : 0;

        // RSI Zone (continuous + momentum) — 0/1/2 puan.
        var rsiMomentumPositive = snapshot.Rsi14 > snapshot.Rsi14Prev;
        int rsiZoneScore;
        if (snapshot.Rsi14 < p.RsiOversoldZone && rsiMomentumPositive)
        {
            rsiZoneScore = 2;
        }
        else if (snapshot.Rsi14 < p.RsiNeutralCeiling && rsiMomentumPositive)
        {
            rsiZoneScore = 1;
        }
        else
        {
            rsiZoneScore = 0;
        }

        // EMA9 slope — 0/1.
        var slopeScore = snapshot.Ema9Now > snapshot.Ema9Prev ? 1 : 0;

        // TradeCount surge — 0/1. Eşik düşürüldü (0.8 default) — surge zorunlu
        // değil, bonus.
        var surgeScore = snapshot.CurrentTradeCount >
            snapshot.AvgTradeCount20 * p.TradeCountSurgeMultiplier
                ? 1 : 0;

        // MinAtr (CoinClass-aware) — 0/1, hard-gate.
        var atrPct = snapshot.CurrentClose > 0m
            ? snapshot.Atr14 / snapshot.CurrentClose
            : 0m;
        var atrScore = (snapshot.Atr14 > 0m && atrPct >= minAtrPct) ? 1 : 0;

        // Loop 77 — BBW (Bollinger band-width) regime nice-to-have skoru.
        // Yüksek BBW = volatilite genişliyor, breakout dostu rejim. Hard-gate
        // değil — toggle off veya threshold altı = 0 puan, emit kalır.
        var bbwScore = (p.BbwScoreEnabled && snapshot.BollingerBandWidth > p.BbwThreshold)
            ? p.BbwScorePoints
            : 0;

        var totalScore = rsiZoneScore + slopeScore + surgeScore + spreadScore + atrScore + bbwScore;

        // Streak guard — Loop 72'de dinamikleşir; şimdilik 4 sabit.
        var requiredScore = _cooldown.GetCurrentScoreThreshold(strategyId, symbol);
        if (requiredScore <= 0)
        {
            requiredScore = p.MinScoreThreshold;
        }

        // Hard-gate: spread veya MinAtr 0 ⇒ direkt skip (skor toplam ne olursa
        // olsun likidite/volatilite anti-disaster).
        // RSI Zone hard-gate: ≥1 zorunlu (rsiZoneScore == 0 ise emit yok).
        var hardGateOk = spreadScore > 0 && atrScore > 0 && rsiZoneScore > 0;

        if (!hardGateOk || totalScore < requiredScore)
        {
            _logger.LogDebug(
                "KMS skip score symbol={Symbol} totalScore={Total} required={Req} " +
                "rsiZone={RsiZone} slope={Slope} surge={Surge} spread={Spread} atr={Atr} bbw={Bbw} " +
                "rsi={Rsi} rsiPrev={RsiPrev} spreadPct={SpreadPct} atrPct={AtrPct} " +
                "minAtrPct={MinAtr} coinClass={CoinClass} ema200={Ema200} bbwValue={BbwValue} " +
                "decision=Skip",
                symbol, totalScore, requiredScore,
                rsiZoneScore, slopeScore, surgeScore, spreadScore, atrScore, bbwScore,
                snapshot.Rsi14, snapshot.Rsi14Prev, spreadPct, atrPct,
                minAtrPct, coinClass, snapshot.Ema200, snapshot.BollingerBandWidth);
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

        // ── SKOR-BAZLI DİNAMİK TP/SL/MaxHold ──────────────────────────────
        var (tpMul, slMul, maxHoldMinutes) = ResolveGeometryByScore(totalScore, p);

        var entryPrice = snapshot.CurrentClose;

        // ATR-tabanlı TP/SL geometrisi + Min/Max clip. Atr14 > 0 garanti
        // (atrScore == 1 hard-gate).
        var rawTpPct = snapshot.Atr14 * tpMul / entryPrice;
        var rawSlPct = snapshot.Atr14 * slMul / entryPrice;
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
            entryReason = "score_gate",
            score = totalScore,
            requiredScore,
            coinClass,
            rsiZoneScore,
            slopeScore,
            surgeScore,
            spreadScore,
            atrScore,
            bbwScore,
            rsi14 = snapshot.Rsi14,
            rsi14Prev = snapshot.Rsi14Prev,
            ema9Now = snapshot.Ema9Now,
            ema9Prev = snapshot.Ema9Prev,
            ema200 = snapshot.Ema200,
            atr14 = snapshot.Atr14,
            atrPct,
            minAtrPct,
            bbw = snapshot.BollingerBandWidth,
            bbwThreshold = p.BbwThreshold,
            bbwHardGate = p.BbwHardGate,
            tradeCountAvg20 = snapshot.AvgTradeCount20,
            currentTradeCount = snapshot.CurrentTradeCount,
            askPrice = ticker.AskPrice,
            bidPrice = ticker.BidPrice,
            spreadPct,
            currentClose = snapshot.CurrentClose,
            tpAtrMultiplier = tpMul,
            slAtrMultiplier = slMul,
            tpPct,
            slPct,
            maxHoldMinutes,
            cooldownBarsAfterSignal = p.CooldownBarsAfterSignal,
            beMoveTriggerPct = p.BeMoveTriggerPct,
            beMoveOffsetPct = p.BeMoveOffsetPct,
            lastBarOpenTime = snapshot.LastBarOpenTime,
        });

        _logger.LogInformation(
            "KMS emit symbol={Symbol} score={Score}/7 coinClass={CoinClass} " +
            "rsiZone={RsiZone} slope={Slope} surge={Surge} spread={Spread} atr={Atr} bbw={Bbw} " +
            "entry={Entry} stop={Stop} tp={Tp} tpMul={TpMul} slMul={SlMul} " +
            "tpPct={TpPct} slPct={SlPct} maxHold={MaxHold} " +
            "rsi={Rsi} rsiPrev={RsiPrev} spreadPct={SpreadPct} atrPct={AtrPct} " +
            "ema200={Ema200} bbwValue={BbwValue} " +
            "decision=Emit",
            symbol, totalScore, coinClass,
            rsiZoneScore, slopeScore, surgeScore, spreadScore, atrScore, bbwScore,
            entryPrice, stopPrice, takeProfit, tpMul, slMul,
            tpPct, slPct, maxHoldMinutes,
            snapshot.Rsi14, snapshot.Rsi14Prev, spreadPct, atrPct,
            snapshot.Ema200, snapshot.BollingerBandWidth);

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

    /// <summary>
    /// Skor → geometri eşleştirme. 4/5/6 paketleri:
    ///   4 ⇒ Low (dar TP, geniş SL, kısa hold) — düşük güven, hızlı kapat.
    ///   5 ⇒ Mid (default).
    ///   6 ⇒ High (geniş TP, dar SL, uzun hold) — yüksek güven, kar büyüt.
    /// 4'ün altı zaten skip path'te (requiredScore koruması).
    /// </summary>
    private static (decimal TpMul, decimal SlMul, int MaxHoldMinutes) ResolveGeometryByScore(
        int score, Parameters p) => score switch
        {
            >= 6 => (p.TpAtrMultiplierHigh, p.SlAtrMultiplierHigh, p.MaxHoldMinutesHighScore),
            5 => (p.TpAtrMultiplier, p.SlAtrMultiplier, p.MaxHoldMinutes),
            _ => (p.TpAtrMultiplierLow, p.SlAtrMultiplierLow, p.MaxHoldMinutesLowScore),
        };

    private static string NormalizeCoinClass(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "alt";
        var lower = raw.Trim().ToLowerInvariant();
        return lower switch
        {
            "large" => "large",
            "mid" => "mid",
            "alt" => "alt",
            _ => "alt",
        };
    }

    private static decimal Clamp(decimal value, decimal min, decimal max)
    {
        if (min > 0m && value < min) return min;
        if (max > 0m && value > max) return max;
        return value;
    }

    /// <summary>
    /// Loop 71 KMS skor-bazlı parametre kontratı. Serialised as
    /// <c>Strategies.Seed[].ParametersJson</c>. Default'lar binance-expert
    /// spec baseline.
    /// </summary>
    private sealed class Parameters
    {
        // Periyotlar.
        public int RsiPeriod { get; set; } = 14;
        public int EmaPeriod { get; set; } = 9;
        public int AtrPeriod { get; set; } = 14;
        public int TradeCountWindow { get; set; } = 20;

        // RSI Zone (continuous, cross değil).
        public decimal RsiOversoldZone { get; set; } = 40m;
        public decimal RsiNeutralCeiling { get; set; } = 52m;

        // Skor eşiği (min emit). 4/6 default.
        public int MinScoreThreshold { get; set; } = 4;

        // CoinClass — strategy seed'ten string ("large"/"mid"/"alt").
        public string? CoinClass { get; set; }

        // CoinClass-aware MinAtr eşikleri.
        public decimal MinAtrPctLarge { get; set; } = 0.0002m;
        public decimal MinAtrPctMid { get; set; } = 0.0003m;
        public decimal MinAtrPctAlt { get; set; } = 0.0004m;

        // TradeCount surge — 0.8 default (gevşetildi, sadece bonus puan).
        public decimal TradeCountSurgeMultiplier { get; set; } = 0.8m;

        // ATR çarpanları — skor 5/6 (mid) baseline.
        public decimal TpAtrMultiplier { get; set; } = 1.8m;
        public decimal SlAtrMultiplier { get; set; } = 0.75m;

        // Skor 4 (low) — dar TP, geniş SL.
        public decimal TpAtrMultiplierLow { get; set; } = 1.5m;
        public decimal SlAtrMultiplierLow { get; set; } = 0.85m;

        // Skor 6 (high) — geniş TP, dar SL.
        public decimal TpAtrMultiplierHigh { get; set; } = 2.2m;
        public decimal SlAtrMultiplierHigh { get; set; } = 0.65m;

        // TP/SL clamp (binance-expert anti-disaster).
        public decimal MinTpPct { get; set; } = 0.005m;
        public decimal MaxTpPct { get; set; } = 0.018m;
        public decimal MinSlPct { get; set; } = 0.003m;
        public decimal MaxSlPct { get; set; } = 0.008m;

        // Time-Stop (skora göre değişir).
        public int MaxHoldMinutes { get; set; } = 45;
        public int MaxHoldMinutesLowScore { get; set; } = 30;
        public int MaxHoldMinutesHighScore { get; set; } = 60;

        // Spread filter.
        public decimal SpreadThresholdPct { get; set; } = 0.005m;

        // Cooldown — 3 bar × 5dk = 15dk.
        public int CooldownBarsAfterSignal { get; set; } = 3;

        // Loop 75 — break-even SL move parametreleri. MVP'de strateji-specific
        // değerler okunmuyor (MarkToMarketWorker global IOptions<BreakEvenOptions>
        // kullanır); JSON'da audit + future override için tutulur. Loop 76+ Position
        // aggregate'a parametre push edilirse evaluator bu alanları emit ContextJson'una
        // koyar ve handler Position.Open'a aktarır.
        public decimal BeMoveTriggerPct { get; set; } = 0.0010m;
        public decimal BeMoveOffsetPct { get; set; } = 0.0002m;

        // Loop 77 — EMA200 trend gate. Hard-gate: closePrice <= EMA200 ise
        // emit yok (downtrend long elenir). Toggle false ⇒ gate bypass
        // (0-emit acil sigortası). EMA200 == 0 ⇒ warmup yetersiz, gate açık.
        public bool Ema200GateEnabled { get; set; } = true;

        // Loop 77 — Bollinger band-width skor sistemine bonus puan ekler.
        // Hard-gate DEĞİL; toggle false ile sıfır puan döner ama emit'i
        // engellemez (frekans koruma).
        public bool BbwScoreEnabled { get; set; } = true;

        // Loop 77 — BBW eşiği. (Upper - Lower) / Middle > BbwThreshold ⇒
        // BbwScorePoints. Default 0.008 = %0.8 bant genişliği — sıkışmadan
        // yeni çıkmış, breakout dostu rejimi yakalar.
        public decimal BbwThreshold { get; set; } = 0.008m;

        // Loop 77 — BBW puan ağırlığı (default 1, totalScore tavanı 7'ye çıkar).
        public int BbwScorePoints { get; set; } = 1;

        // Loop 78 — BBW hard-gate. Loop 77 t120 post-mortem: BBW=0 emit'leri
        // ardışık big SL'lere yol açtı (sıkışık bant + EMA200 üstü = false
        // breakout tuzağı). True ⇒ <c>BollingerBandWidth &lt; BbwThreshold</c>
        // ise skip (EMA200 gate'in hemen sonrası, skor öncesi). Default false
        // geriye uyumluluk; production seed'lerde true ile deploy.
        public bool BbwHardGate { get; set; } = false;
    }
}
