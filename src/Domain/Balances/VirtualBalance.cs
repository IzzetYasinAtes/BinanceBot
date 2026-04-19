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
    ///
    /// Loop 18 fix (cash invariance): the previous implementation clamped
    /// <see cref="CurrentBalance"/> at 0 whenever a fill drove it negative,
    /// which silently destroyed information whenever multiple paper positions
    /// were open concurrently — Pos1 BUY (-100) clamped to 0, then Pos1 SELL
    /// (+99.5) credited from 0 instead of -100, producing a phantom +100 gain
    /// or, with a different ordering, a phantom -100 loss. Over 34 closed
    /// positions Loop 17 ended at $0 cash with only -$0.71 net realized PnL.
    ///
    /// PaperFillSimulator emits a quote-currency cash delta per fill that is
    /// mathematically consistent: open + close round-trips sum to the realized
    /// PnL. Allowing the running balance to dip negative (it represents
    /// cash-out-while-positions-open, not real spot equity) restores the
    /// invariance <c>final = starting + sum(realizedPnl)</c>. Defense against
    /// over-leveraging is the responsibility of upstream sizing + position-cap
    /// gates (RiskProfile.MaxOpenPositions, MaxPositionSizePct) — not a
    /// domain-level clamp that swallows accounting deltas.
    /// </summary>
    public void ApplyFill(decimal realizedDelta, DateTimeOffset now)
    {
        if (Mode != TradingMode.Paper)
        {
            throw new DomainException(
                $"VirtualBalance.ApplyFill only valid for Paper mode (was {Mode}).");
        }

        CurrentBalance += realizedDelta;
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
