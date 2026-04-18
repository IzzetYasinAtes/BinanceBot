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

        var rsi = Indicators.Rsi(closedBars, p.RsiPeriod);
        var (mean, upper, lower) = Indicators.BollingerBands(closedBars, p.BbPeriod, p.BbStdDev);
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
            rsi,
            mean,
            upper,
            lower,
            price = latest.ClosePrice,
        });

        return Task.FromResult<StrategyEvaluation?>(new StrategyEvaluation(
            direction,
            p.OrderSize,
            latest.ClosePrice,
            null,
            ctx));
    }
}
