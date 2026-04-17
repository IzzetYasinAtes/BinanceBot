using BinanceBot.Domain.Balances.Events;
using BinanceBot.Domain.Common;

namespace BinanceBot.Domain.Balances;

/// <summary>
/// Mode-scoped cash + equity snapshot (ADR-0008 §8.4).
/// Id == (int)Mode — singleton-per-mode seeded via migration.
/// </summary>
public sealed class VirtualBalance : AggregateRoot<int>
{
    public TradingMode Mode { get; private set; }
    public decimal StartingBalance { get; private set; }
    public decimal CurrentBalance { get; private set; }
    public decimal Equity { get; private set; }
    public Guid IterationId { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? LastResetAt { get; private set; }
    public int ResetCount { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private VirtualBalance() { }

    public static VirtualBalance CreateDefault(
        TradingMode mode,
        decimal startingBalance,
        DateTimeOffset now)
    {
        if (startingBalance < 0m)
        {
            throw new DomainException("StartingBalance must be >= 0.");
        }

        return new VirtualBalance
        {
            Id = RiskProfileIdOf(mode),
            Mode = mode,
            StartingBalance = startingBalance,
            CurrentBalance = startingBalance,
            Equity = startingBalance,
            IterationId = Guid.NewGuid(),
            StartedAt = now,
            LastResetAt = null,
            ResetCount = 0,
            UpdatedAt = now,
        };
    }

    public void ResetForIteration(decimal startingBalance, DateTimeOffset now)
    {
        if (Mode != TradingMode.Paper)
        {
            throw new DomainException(
                $"Reset not allowed for mode {Mode}; only Paper is resettable.");
        }
        if (startingBalance <= 0m)
        {
            throw new DomainException("StartingBalance must be positive on reset.");
        }

        IterationId = Guid.NewGuid();
        StartingBalance = startingBalance;
        CurrentBalance = startingBalance;
        Equity = startingBalance;
        StartedAt = now;
        LastResetAt = now;
        ResetCount++;
        UpdatedAt = now;

        RaiseDomainEvent(new VirtualBalanceResetEvent(Mode, IterationId, startingBalance, now));
    }

    /// <summary>
    /// Paper-only. Realized delta (can be +/-) adjusts cash balance.
    /// Commission/slippage impact already baked into realizedDelta by caller.
    /// </summary>
    public void ApplyFill(decimal realizedDelta, DateTimeOffset now)
    {
        if (Mode != TradingMode.Paper)
        {
            throw new DomainException(
                $"VirtualBalance.ApplyFill only valid for Paper mode (was {Mode}).");
        }

        CurrentBalance += realizedDelta;
        if (CurrentBalance < 0m)
        {
            CurrentBalance = 0m;
        }
        Equity = CurrentBalance;
        UpdatedAt = now;

        RaiseDomainEvent(new VirtualBalanceFillAppliedEvent(Mode, realizedDelta, CurrentBalance, now));
    }

    /// <summary>
    /// Mark-to-market equity refresh. Allowed for all modes.
    /// </summary>
    public void ApplyUnrealized(decimal unrealizedPnl, DateTimeOffset now)
    {
        Equity = CurrentBalance + unrealizedPnl;
        UpdatedAt = now;
        RaiseDomainEvent(new VirtualBalanceUpdatedEvent(Mode, CurrentBalance, Equity));
    }

    private static int RiskProfileIdOf(TradingMode mode) => (int)mode;
}
