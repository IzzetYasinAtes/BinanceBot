using System.Text.Json;
using BinanceBot.Application.Abstractions;
using BinanceBot.Application.Strategies.Cooldowns;
using BinanceBot.Application.Strategies.Evaluation;
using BinanceBot.Application.Strategies.Swing;
using BinanceBot.Domain.MarketData;
using BinanceBot.Domain.Strategies;
using Microsoft.Extensions.Logging;

namespace BinanceBot.Infrastructure.Strategies.Evaluators;

/// <summary>
/// Loop 112 — ADR-0027 Aile A SwingTrade. <see cref="IStrategyEvaluator"/>
/// plug-in implementasyonu — 4h MTF swing trading, binance-expert §7 spec.
///
/// <para>
/// Tetikleme: <see cref="BinanceBot.Domain.MarketData.Events.KlineClosedEvent"/>
/// — handler interval'a göre filtre yapmadan tüm Active strategy'leri iterate
/// eder; evaluator kendisi 4h interval guard'ıyla filtreler (closedBars
/// son bar Interval != FourHours ⇒ null return, evaluator emit etmez).
/// </para>
///
/// <para>
/// Akış:
/// <list type="number">
///   <item>4h interval guard + warmup eşiği (60 bar).</item>
///   <item>EMA20 vs EMA50 trend yönü tespit (Long: EmaShort &gt; EmaLong; Short: tersi).</item>
///   <item>Volume(bar) &gt; VolumeSma(20) × 1.5 — momentum onayı.</item>
///   <item>RSI(14) ∈ [RsiLongMin, RsiLongMax] (Long) / [RsiShortMin, RsiShortMax] (Short).</item>
///   <item>ATR(14) × SlAtrMultiplier SL (Long: entry - SL_dist; Short: entry + SL_dist).</item>
///   <item>ATR × TpAtrMultiplier TP — R:R 1:2 default.</item>
///   <item>Cooldown — 1 bar = 4h, signal başına bir emit.</item>
/// </list>
/// </para>
///
/// <para>
/// SOLID: tek-sorumluluk swing-trade emit kararı. Bağımlılıklar
/// <see cref="ICooldownService"/> (Application port) + <see cref="IClock"/>
/// (Application port). Domain'e bağımlılık tek yön (StrategyType, Kline, etc.).
/// Indicators static class Infrastructure-içi paylaşımlı helper (PatternComposite
/// + Swing aynı dosyayı tüketir — DRY).
/// </para>
/// </summary>
public sealed class SwingTradeEvaluator : IStrategyEvaluator
{
    public StrategyType Type => StrategyType.SwingTrade;

    /// <summary>
    /// 4h timeframe sabitleri. Bar = 240 minutes = 4 hours. Cooldown anchor
    /// "1 bar" karşılığı 240dk; <see cref="ICooldownService"/> bu bar-minute
    /// üzerinden çalışır.
    /// </summary>
    internal const int BarMinutes4h = 240;

    /// <summary>
    /// Swing 4h evaluator için minimum bar history. EmaLongPeriod (50) + ATR
    /// (14) + RSI (14) + VolumeSma (20) gereksinimleri 50 bar yeterli; emniyet
    /// payı 60 bar.
    /// </summary>
    internal const int MinBarsForEmit = 60;

    private readonly ICooldownService _cooldown;
    private readonly IClock _clock;
    private readonly ILogger<SwingTradeEvaluator> _logger;

    public SwingTradeEvaluator(
        ICooldownService cooldown,
        IClock clock,
        ILogger<SwingTradeEvaluator> logger)
    {
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
        var options = EvaluatorParameterHelper.TryParse<SwingTradeOptions>(parametersJson)
            ?? new SwingTradeOptions();

        // 4h interval guard — handler tüm interval'lar için event publish ediyor;
        // evaluator yalnızca 4h kapanışında çalışır. closedBars son bar interval'i
        // FourHours değilse skip.
        if (closedBars.Count == 0 || closedBars[^1].Interval != KlineInterval.FourHours)
        {
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        // Warmup yetersizse skip.
        if (closedBars.Count < MinBarsForEmit)
        {
            _logger.LogDebug(
                "SwingTrade warmup symbol={Symbol} strategyId={StrategyId} bars={Bars}/{Min}",
                symbol, strategyId, closedBars.Count, MinBarsForEmit);
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        // Indicator hesabı — paylaşılan Indicators helper'ı (Infrastructure-içi).
        var lastBar = closedBars[^1];
        var entry = lastBar.ClosePrice;

        var emaShort = Indicators.Ema(closedBars, options.EmaShortPeriod, closedBars.Count - 1);
        var emaLong = Indicators.Ema(closedBars, options.EmaLongPeriod, closedBars.Count - 1);

        var volumeSma = Indicators.VolumeSma(closedBars, options.VolumeSmaPeriod);
        var rsi = Indicators.Rsi(closedBars, options.RsiPeriod);
        var atr = Indicators.Atr(closedBars, options.AtrPeriod);

        // Indicator sağlık kontrolü — herhangi biri 0 ise warmup eksik / sahte hesap.
        if (emaShort <= 0m || emaLong <= 0m || volumeSma <= 0m || atr <= 0m || entry <= 0m)
        {
            _logger.LogDebug(
                "SwingTrade indicator-not-ready symbol={Symbol} strategyId={StrategyId} " +
                "emaShort={ES} emaLong={EL} volSma={VS} atr={Atr} entry={Entry}",
                symbol, strategyId, emaShort, emaLong, volumeSma, atr, entry);
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        // Volume momentum gate (her iki yön için ortak).
        var volumeSurge = lastBar.Volume >= volumeSma * options.VolumeSurgeMultiplier;
        if (!volumeSurge)
        {
            _logger.LogDebug(
                "SwingTrade skip volume_low symbol={Symbol} strategyId={StrategyId} " +
                "vol={Vol} sma={Sma} mult={Mult}",
                symbol, strategyId, lastBar.Volume, volumeSma, options.VolumeSurgeMultiplier);
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        // Trend yönü — EMA cross. Eşitlik durumu skip (false signal riski).
        StrategySignalDirection? direction = null;
        if (emaShort > emaLong)
        {
            // Long aday — RSI band kontrolü.
            if (rsi >= options.RsiLongMin && rsi <= options.RsiLongMax)
            {
                direction = StrategySignalDirection.Long;
            }
            else
            {
                _logger.LogDebug(
                    "SwingTrade skip rsi_outside_long symbol={Symbol} rsi={Rsi} band=[{Min},{Max}]",
                    symbol, rsi, options.RsiLongMin, options.RsiLongMax);
            }
        }
        else if (emaShort < emaLong)
        {
            // Short aday — RSI band kontrolü.
            if (rsi >= options.RsiShortMin && rsi <= options.RsiShortMax)
            {
                direction = StrategySignalDirection.Short;
            }
            else
            {
                _logger.LogDebug(
                    "SwingTrade skip rsi_outside_short symbol={Symbol} rsi={Rsi} band=[{Min},{Max}]",
                    symbol, rsi, options.RsiShortMin, options.RsiShortMax);
            }
        }
        else
        {
            _logger.LogDebug(
                "SwingTrade skip ema_flat symbol={Symbol} emaShort={ES} emaLong={EL}",
                symbol, emaShort, emaLong);
        }

        if (direction is null)
        {
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        // Cooldown kontrolü — emit kararından önce. 4h bar başına bir signal
        // (binance-expert §7 max 5-10 trade/gün/coin için yeterli).
        var now = _clock.UtcNow;
        if (_cooldown.IsCooldown(strategyId, symbol, options.CooldownBarsAfterSignal, BarMinutes4h, now))
        {
            _logger.LogDebug(
                "SwingTrade cooldown skip symbol={Symbol} strategyId={StrategyId} bars={Bars}",
                symbol, strategyId, options.CooldownBarsAfterSignal);
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        // Geometri — ATR-bazlı SL/TP. R:R = TpAtrMultiplier / SlAtrMultiplier.
        var slDistance = atr * options.SlAtrMultiplier;
        var tpDistance = atr * options.TpAtrMultiplier;

        decimal stopPrice;
        decimal takeProfit;
        if (direction == StrategySignalDirection.Long)
        {
            stopPrice = entry - slDistance;
            takeProfit = entry + tpDistance;
        }
        else
        {
            stopPrice = entry + slDistance;
            takeProfit = entry - tpDistance;
        }

        // SL/TP sanity — pozitif olmalı (ATR çok büyükse SL negatife düşmesin).
        if (stopPrice <= 0m || takeProfit <= 0m)
        {
            _logger.LogWarning(
                "SwingTrade skip invalid_geometry symbol={Symbol} entry={Entry} sl={Sl} tp={Tp} atr={Atr}",
                symbol, entry, stopPrice, takeProfit, atr);
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        // ContextJson — audit için emit kararının matematiksel kanıtı.
        // maxHoldMinutes (CamelCase: maxHoldMinutes — OrderFilledPositionHandler bu
        // key'i okuyup Position.MaxHoldDuration set eder; MaxHoldHours × 60 dakika.
        // timeExitMinProfitPct: MarkToMarketWorker yeni dal "profitable time-exit"
        // hold süresi dolduğunda + UPnl pozitif eşik üzerindeyse close eder.
        var maxHoldMinutes = options.MaxHoldHours * 60;
        var ctx = new SwingTradeContext(
            Type: "swing-trade",
            Direction: direction.Value.ToString(),
            EmaShort: emaShort,
            EmaLong: emaLong,
            Rsi: rsi,
            Atr: atr,
            Volume: lastBar.Volume,
            VolumeSma: volumeSma,
            VolumeSurgeMultiplier: options.VolumeSurgeMultiplier,
            Entry: entry,
            StopPrice: stopPrice,
            TakeProfit: takeProfit,
            SlAtrMultiplier: options.SlAtrMultiplier,
            TpAtrMultiplier: options.TpAtrMultiplier,
            MaxHoldMinutes: maxHoldMinutes,
            TimeExitMinProfitPct: options.TimeExitMinProfitPct,
            BarOpenTime: lastBar.OpenTime,
            BarCloseTime: lastBar.CloseTime);
        var ctxJson = JsonSerializer.Serialize(ctx);

        _logger.LogInformation(
            "SwingTrade emit symbol={Symbol} strategyId={StrategyId} direction={Direction} " +
            "entry={Entry} sl={Sl} tp={Tp} ema={ES}/{EL} rsi={Rsi} vol={Vol}/{VolSma}",
            symbol, strategyId, direction, entry, stopPrice, takeProfit,
            emaShort, emaLong, rsi, lastBar.Volume, volumeSma);

        _cooldown.RecordSignal(strategyId, symbol, now);

        // SuggestedQuantity placeholder (sizing handler'da; SnowballSizing /
        // RiskProfile bazlı). Pattern composite'te de aynı 0.00000001 placeholder.
        return Task.FromResult<StrategyEvaluation?>(new StrategyEvaluation(
            Direction: direction.Value,
            SuggestedQuantity: 0.00000001m,
            SuggestedPrice: entry,
            SuggestedStopPrice: stopPrice,
            ContextJson: ctxJson,
            SuggestedTakeProfit: takeProfit));
    }

    /// <summary>
    /// Emit kararının audit payload'ı. <see cref="StrategyEvaluation.ContextJson"/>
    /// içine serialize edilir; OrderFilledPositionHandler maxHoldMinutes alanını
    /// okuyup <see cref="BinanceBot.Domain.Positions.Position.MaxHoldDuration"/>
    /// set eder. UI ContextJson okur, emit nedenini insanca gösterir.
    ///
    /// Property naming JSON çıktısında PascalCase; OrderFilledPositionHandler
    /// case-insensitive okuduğu için <c>maxHoldMinutes</c> alias'ına gerek yok
    /// — JsonSerializer default System.Text.Json case-insensitive değildir, ama
    /// OrderFilledPositionHandler explicit <c>"maxHoldMinutes"</c> property name
    /// arıyor (camelCase). Bu nedenle sadece property name'i camelCase'e çekmeden
    /// JsonPropertyName ile camelCase serialize edilmesi gerekir.
    /// </summary>
    private sealed record SwingTradeContext(
        [property: System.Text.Json.Serialization.JsonPropertyName("type")] string Type,
        [property: System.Text.Json.Serialization.JsonPropertyName("direction")] string Direction,
        [property: System.Text.Json.Serialization.JsonPropertyName("emaShort")] decimal EmaShort,
        [property: System.Text.Json.Serialization.JsonPropertyName("emaLong")] decimal EmaLong,
        [property: System.Text.Json.Serialization.JsonPropertyName("rsi")] decimal Rsi,
        [property: System.Text.Json.Serialization.JsonPropertyName("atr")] decimal Atr,
        [property: System.Text.Json.Serialization.JsonPropertyName("volume")] decimal Volume,
        [property: System.Text.Json.Serialization.JsonPropertyName("volumeSma")] decimal VolumeSma,
        [property: System.Text.Json.Serialization.JsonPropertyName("volumeSurgeMultiplier")] decimal VolumeSurgeMultiplier,
        [property: System.Text.Json.Serialization.JsonPropertyName("entry")] decimal Entry,
        [property: System.Text.Json.Serialization.JsonPropertyName("stopPrice")] decimal StopPrice,
        [property: System.Text.Json.Serialization.JsonPropertyName("takeProfit")] decimal TakeProfit,
        [property: System.Text.Json.Serialization.JsonPropertyName("slAtrMultiplier")] decimal SlAtrMultiplier,
        [property: System.Text.Json.Serialization.JsonPropertyName("tpAtrMultiplier")] decimal TpAtrMultiplier,
        [property: System.Text.Json.Serialization.JsonPropertyName("maxHoldMinutes")] int MaxHoldMinutes,
        [property: System.Text.Json.Serialization.JsonPropertyName("timeExitMinProfitPct")] decimal TimeExitMinProfitPct,
        [property: System.Text.Json.Serialization.JsonPropertyName("barOpenTime")] DateTimeOffset BarOpenTime,
        [property: System.Text.Json.Serialization.JsonPropertyName("barCloseTime")] DateTimeOffset BarCloseTime);
}
