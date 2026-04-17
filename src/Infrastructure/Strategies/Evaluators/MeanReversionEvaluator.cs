using BinanceBot.Application.Strategies.Evaluation;
using BinanceBot.Domain.MarketData;
using BinanceBot.Domain.Strategies;

namespace BinanceBot.Infrastructure.Strategies.Evaluators;

public sealed class MeanReversionEvaluator : IStrategyEvaluator
{
    public StrategyType Type => StrategyType.MeanReversion;

    private sealed class Parameters
    {
        public int RsiPeriod { get; set; } = 14;
        public decimal RsiOversold { get; set; } = 30m;
        public decimal RsiOverbought { get; set; } = 70m;
        public int BbPeriod { get; set; } = 20;
        public decimal BbStdDev { get; set; } = 2m;
        public decimal OrderSize { get; set; } = 0.001m;
    }

    public Task<StrategyEvaluation?> EvaluateAsync(
        long strategyId, string parametersJson, string symbol,
        IReadOnlyList<Kline> closedBars, CancellationToken cancellationToken)
    {
        var p = EvaluatorParameterHelper.TryParse<Parameters>(parametersJson) ?? new Parameters();
        var minBars = Math.Max(p.RsiPeriod + 2, p.BbPeriod + 2);
        if (closedBars.Count < minBars)
        {
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        var rsi = Rsi(closedBars, p.RsiPeriod);
        var (mean, upper, lower) = BollingerBands(closedBars, p.BbPeriod, p.BbStdDev);
        var latest = closedBars[^1];

        var longSignal = rsi <= p.RsiOversold && latest.ClosePrice <= lower;
        var shortSignal = rsi >= p.RsiOverbought && latest.ClosePrice >= upper;
        var exitSignal = rsi is >= 45m and <= 55m;

        if (!longSignal && !shortSignal && !exitSignal)
        {
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        StrategySignalDirection direction;
        if (longSignal) direction = StrategySignalDirection.Long;
        else if (shortSignal) direction = StrategySignalDirection.Short;
        else direction = StrategySignalDirection.Exit;

        var ctx = EvaluatorParameterHelper.SerializeContext(new
        {
            type = "meanrev",
            rsi, mean, upper, lower,
            price = latest.ClosePrice,
        });

        return Task.FromResult<StrategyEvaluation?>(new StrategyEvaluation(
            direction,
            p.OrderSize,
            latest.ClosePrice,
            null,
            ctx));
    }

    private static decimal Rsi(IReadOnlyList<Kline> bars, int period)
    {
        if (bars.Count < period + 1) return 50m;
        decimal gainSum = 0m, lossSum = 0m;
        var start = bars.Count - period;
        for (var i = start; i < bars.Count; i++)
        {
            var diff = bars[i].ClosePrice - bars[i - 1].ClosePrice;
            if (diff >= 0m) gainSum += diff;
            else lossSum -= diff;
        }
        var avgGain = gainSum / period;
        var avgLoss = lossSum / period;
        if (avgLoss == 0m) return 100m;
        var rs = avgGain / avgLoss;
        return 100m - (100m / (1m + rs));
    }

    private static (decimal mean, decimal upper, decimal lower) BollingerBands(
        IReadOnlyList<Kline> bars, int period, decimal stdDevMultiplier)
    {
        if (bars.Count < period) return (bars[^1].ClosePrice, bars[^1].ClosePrice, bars[^1].ClosePrice);
        decimal sum = 0m;
        var start = bars.Count - period;
        for (var i = start; i < bars.Count; i++) sum += bars[i].ClosePrice;
        var mean = sum / period;

        decimal sqSum = 0m;
        for (var i = start; i < bars.Count; i++)
        {
            var d = bars[i].ClosePrice - mean;
            sqSum += d * d;
        }
        var variance = sqSum / period;
        var stdDev = (decimal)Math.Sqrt((double)variance);
        return (mean, mean + stdDevMultiplier * stdDev, mean - stdDevMultiplier * stdDev);
    }
}
