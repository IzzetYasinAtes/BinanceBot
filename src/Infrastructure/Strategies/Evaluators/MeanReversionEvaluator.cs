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

        // Loop 11 stop fix — mean-reversion entries had no stop, leaving losers uncapped.
        // Stop is placed BbStopMultiplier × stdDev away from entry on the lossy side
        // (long: below entry; short: above). With BbStdDev=2 and BbStopMultiplier=1.5
        // the stop sits ~0.75 band-width past the band edge, giving room for the bounce
        // without letting a regime change run the position into the ground.
        public decimal BbStopMultiplier { get; set; } = 1.5m;
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
        // Loop 11 — RSI exit band tightened from [45,55] to [48,52]. The wider band emitted
        // exit signals on nearly every bar in chop, drowning the order pipeline; the tighter
        // band keeps Exit semantics for "true mean" only and lets the BB-mean TP do the
        // heavy lifting on profit realisation.
        var exitSignal = rsi is >= 48m and <= 52m;

        if (!longSignal && !shortSignal && !exitSignal)
        {
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        StrategySignalDirection direction;
        if (longSignal) direction = StrategySignalDirection.Long;
        else if (shortSignal) direction = StrategySignalDirection.Short;
        else direction = StrategySignalDirection.Exit;

        // Loop 10 take-profit fix — for entry signals the BB middle band IS the mean-reversion
        // target; price reverting to the mean is the canonical exit. Exit signals carry no TP
        // (the exit IS the trigger).
        decimal? takeProfit = direction switch
        {
            StrategySignalDirection.Long when mean > latest.ClosePrice => mean,
            StrategySignalDirection.Short when mean < latest.ClosePrice => mean,
            _ => null,
        };

        // Loop 11 stop fix — recover stdDev from the band geometry (lower = mean - BbStdDev*std)
        // to size the stop in the same units the bands are drawn in. Stop sits BbStopMultiplier
        // band-widths past entry on the lossy side. Exit signals carry no stop — the exit IS
        // the trigger, and existing positions retain their original stop.
        var stdDev = p.BbStdDev > 0m ? (mean - lower) / p.BbStdDev : 0m;
        var stopOffset = stdDev * p.BbStopMultiplier;
        decimal? stopPrice = direction switch
        {
            StrategySignalDirection.Long when stopOffset > 0m => latest.ClosePrice - stopOffset,
            StrategySignalDirection.Short when stopOffset > 0m => latest.ClosePrice + stopOffset,
            _ => null,
        };

        var ctx = EvaluatorParameterHelper.SerializeContext(new
        {
            type = "meanrev",
            rsi,
            mean,
            upper,
            lower,
            stdDev,
            price = latest.ClosePrice,
            takeProfit,
            stopPrice,
        });

        return Task.FromResult<StrategyEvaluation?>(new StrategyEvaluation(
            direction,
            p.OrderSize,
            latest.ClosePrice,
            stopPrice,
            ctx,
            SuggestedTakeProfit: takeProfit));
    }
}
