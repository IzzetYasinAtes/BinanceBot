using BinanceBot.Application.Strategies.Evaluation;
using BinanceBot.Domain.MarketData;
using BinanceBot.Domain.Strategies;

namespace BinanceBot.Infrastructure.Strategies.Evaluators;

public sealed class TrendFollowingEvaluator : IStrategyEvaluator
{
    public StrategyType Type => StrategyType.TrendFollowing;

    private sealed class Parameters
    {
        public int FastEma { get; set; } = 20;
        public int SlowEma { get; set; } = 50;
        public int AtrPeriod { get; set; } = 14;
        public decimal AtrStopMultiplier { get; set; } = 2.0m;
        public decimal OrderSize { get; set; } = 0.001m;
    }

    public Task<StrategyEvaluation?> EvaluateAsync(
        long strategyId, string parametersJson, string symbol,
        IReadOnlyList<Kline> closedBars, CancellationToken cancellationToken)
    {
        var p = EvaluatorParameterHelper.TryParse<Parameters>(parametersJson) ?? new Parameters();
        var minBars = Math.Max(p.SlowEma + 2, p.AtrPeriod + 2);
        if (closedBars.Count < minBars)
        {
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        var fastPrev = Ema(closedBars, p.FastEma, closedBars.Count - 2);
        var fastNow = Ema(closedBars, p.FastEma, closedBars.Count - 1);
        var slowPrev = Ema(closedBars, p.SlowEma, closedBars.Count - 2);
        var slowNow = Ema(closedBars, p.SlowEma, closedBars.Count - 1);

        var crossedUp = fastPrev <= slowPrev && fastNow > slowNow;
        var crossedDown = fastPrev >= slowPrev && fastNow < slowNow;
        if (!crossedUp && !crossedDown)
        {
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        var atr = Atr(closedBars, p.AtrPeriod);
        var latest = closedBars[^1];
        var stopPrice = crossedUp
            ? latest.ClosePrice - atr * p.AtrStopMultiplier
            : latest.ClosePrice + atr * p.AtrStopMultiplier;

        var ctx = EvaluatorParameterHelper.SerializeContext(new
        {
            type = "trend",
            fastNow, slowNow, atr,
            cross = crossedUp ? "up" : "down",
        });

        return Task.FromResult<StrategyEvaluation?>(new StrategyEvaluation(
            crossedUp ? StrategySignalDirection.Long : StrategySignalDirection.Short,
            p.OrderSize,
            latest.ClosePrice,
            stopPrice,
            ctx));
    }

    private static decimal Ema(IReadOnlyList<Kline> bars, int period, int endIndex)
    {
        if (endIndex < period - 1)
        {
            return bars[endIndex].ClosePrice;
        }
        decimal alpha = 2m / (period + 1);
        decimal ema = bars[endIndex - period + 1].ClosePrice;
        for (var i = endIndex - period + 2; i <= endIndex; i++)
        {
            ema = alpha * bars[i].ClosePrice + (1 - alpha) * ema;
        }
        return ema;
    }

    private static decimal Atr(IReadOnlyList<Kline> bars, int period)
    {
        if (bars.Count < period + 1) return 0m;
        var start = bars.Count - period;
        decimal sum = 0m;
        for (var i = start; i < bars.Count; i++)
        {
            var high = bars[i].HighPrice;
            var low = bars[i].LowPrice;
            var prevClose = bars[i - 1].ClosePrice;
            var tr = Math.Max(high - low, Math.Max(Math.Abs(high - prevClose), Math.Abs(low - prevClose)));
            sum += tr;
        }
        return sum / period;
    }
}
