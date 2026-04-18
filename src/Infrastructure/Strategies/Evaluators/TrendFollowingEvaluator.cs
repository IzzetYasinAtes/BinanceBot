using BinanceBot.Application.Strategies.Evaluation;
using BinanceBot.Domain.MarketData;
using BinanceBot.Domain.Strategies;

namespace BinanceBot.Infrastructure.Strategies.Evaluators;

public sealed class TrendFollowingEvaluator : IStrategyEvaluator
{
    public StrategyType Type => StrategyType.TrendFollowing;

    private sealed class Parameters
    {
        public int FastEma { get; set; } = 3;
        public int SlowEma { get; set; } = 8;
        public int AtrPeriod { get; set; } = 14;
        public decimal AtrStopMultiplier { get; set; } = 2.0m;
        public decimal OrderSize { get; set; } = 0.001m;

        // ADR-0012 §12.5: RSI confirmation filter — drop EMA crosses while the market is in
        // an oversold/overbought regime (whipsaw zone). Range is inclusive.
        public int RsiPeriod { get; set; } = 14;
        public decimal RsiMin { get; set; } = 30m;
        public decimal RsiMax { get; set; } = 70m;
    }

    public Task<StrategyEvaluation?> EvaluateAsync(
        long strategyId, string parametersJson, string symbol,
        IReadOnlyList<Kline> closedBars, CancellationToken cancellationToken)
    {
        var p = EvaluatorParameterHelper.TryParse<Parameters>(parametersJson) ?? new Parameters();
        var minBars = Math.Max(Math.Max(p.SlowEma + 2, p.AtrPeriod + 2), p.RsiPeriod + 2);
        if (closedBars.Count < minBars)
        {
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        var fastPrev = Indicators.Ema(closedBars, p.FastEma, closedBars.Count - 2);
        var fastNow = Indicators.Ema(closedBars, p.FastEma, closedBars.Count - 1);
        var slowPrev = Indicators.Ema(closedBars, p.SlowEma, closedBars.Count - 2);
        var slowNow = Indicators.Ema(closedBars, p.SlowEma, closedBars.Count - 1);

        var crossedUp = fastPrev <= slowPrev && fastNow > slowNow;
        var crossedDown = fastPrev >= slowPrev && fastNow < slowNow;
        if (!crossedUp && !crossedDown)
        {
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        // ADR-0012 §12.5: filter cross signals fired while RSI sits in an extreme regime.
        // crossUp during oversold = "dead-cat bounce" risk; crossDown during overbought = "false dip" risk.
        var rsi = Indicators.Rsi(closedBars, p.RsiPeriod);
        if (rsi < p.RsiMin || rsi > p.RsiMax)
        {
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        var atr = Indicators.Atr(closedBars, p.AtrPeriod);
        var latest = closedBars[^1];
        var stopPrice = crossedUp
            ? latest.ClosePrice - atr * p.AtrStopMultiplier
            : latest.ClosePrice + atr * p.AtrStopMultiplier;

        var ctx = EvaluatorParameterHelper.SerializeContext(new
        {
            type = "trend",
            fastNow,
            slowNow,
            atr,
            rsi,
            cross = crossedUp ? "up" : "down",
        });

        return Task.FromResult<StrategyEvaluation?>(new StrategyEvaluation(
            crossedUp ? StrategySignalDirection.Long : StrategySignalDirection.Short,
            p.OrderSize,
            latest.ClosePrice,
            stopPrice,
            ctx));
    }
}
