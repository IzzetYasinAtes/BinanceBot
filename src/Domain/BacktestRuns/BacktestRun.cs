using BinanceBot.Domain.BacktestRuns.Events;
using BinanceBot.Domain.Common;
using BinanceBot.Domain.Orders;

namespace BinanceBot.Domain.BacktestRuns;

public sealed class BacktestRun : AggregateRoot<long>
{
    private readonly List<BacktestTrade> _trades = [];

    public long StrategyId { get; private set; }
    public DateTimeOffset FromUtc { get; private set; }
    public DateTimeOffset ToUtc { get; private set; }
    public decimal InitialBalance { get; private set; }
    public decimal FinalBalance { get; private set; }
    public decimal Sharpe { get; private set; }
    public decimal MaxDrawdownPct { get; private set; }
    public decimal WinRate { get; private set; }
    public BacktestStatus Status { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    public IReadOnlyCollection<BacktestTrade> Trades => _trades.AsReadOnly();

    private BacktestRun() { }

    public static BacktestRun Start(
        long strategyId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        decimal initialBalance,
        DateTimeOffset now)
    {
        if (strategyId <= 0)
        {
            throw new DomainException("StrategyId required.");
        }
        if (toUtc <= fromUtc)
        {
            throw new DomainException("ToUtc must be after FromUtc.");
        }
        if (initialBalance <= 0m)
        {
            throw new DomainException("InitialBalance must be positive.");
        }

        var run = new BacktestRun
        {
            StrategyId = strategyId,
            FromUtc = fromUtc,
            ToUtc = toUtc,
            InitialBalance = initialBalance,
            FinalBalance = initialBalance,
            Status = BacktestStatus.Running,
            StartedAt = now,
        };

        run.RaiseDomainEvent(new BacktestStartedEvent(run.Id, strategyId));
        return run;
    }

    public void RecordTrade(
        OrderSide side,
        decimal price,
        decimal quantity,
        decimal pnl,
        DateTimeOffset occurredAt)
    {
        if (Status != BacktestStatus.Running)
        {
            throw new DomainException("Only Running backtests accept trades.");
        }
        var seq = _trades.Count;
        _trades.Add(BacktestTrade.Record(seq, side, price, quantity, pnl, occurredAt));
        FinalBalance += pnl;
    }

    public void Complete(
        decimal sharpe,
        decimal maxDrawdownPct,
        decimal winRate,
        DateTimeOffset now)
    {
        if (Status != BacktestStatus.Running)
        {
            throw new DomainException("Only Running backtest can complete.");
        }
        Sharpe = sharpe;
        MaxDrawdownPct = maxDrawdownPct;
        WinRate = winRate;
        Status = BacktestStatus.Completed;
        CompletedAt = now;
        RaiseDomainEvent(new BacktestCompletedEvent(Id, FinalBalance, sharpe, maxDrawdownPct, _trades.Count));
    }

    public void Fail(string reason, DateTimeOffset now)
    {
        Status = BacktestStatus.Failed;
        FailureReason = reason;
        CompletedAt = now;
        RaiseDomainEvent(new BacktestFailedEvent(Id, reason));
    }
}
