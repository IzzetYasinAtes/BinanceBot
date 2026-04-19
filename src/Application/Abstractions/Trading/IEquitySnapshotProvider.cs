using BinanceBot.Domain.Common;

namespace BinanceBot.Application.Abstractions.Trading;

/// <summary>
/// Mode-aware equity snapshot used by sizing and risk tracking (ADR-0011 §11.3).
///
/// Two flavours:
///
/// <see cref="GetEquityAsync"/> — mark-to-market equity (cash + unrealized PnL).
///   Consumers: sizing (<c>StrategySignalToOrderHandler</c>), close-time snapshot
///   (<c>PositionClosedRiskHandler</c>). They need the live valuation to decide
///   notional / record realized peak at trade exit.
///
/// <see cref="GetRealizedEquityAsync"/> — realized-only equity (cash balance after
///   all closed trades, ignores open-position unrealized PnL). Loop 12 reform:
///   <c>EquityPeakTrackerService</c> consumes this so intraday unrealized swings
///   no longer ratchet PeakEquity and false-trip the drawdown circuit-breaker.
///   See loops/loop_11/summary.md for the failure trace this addresses.
///
/// Mode rules (apply to both methods):
///   - Paper       : VirtualBalance row for the Paper account.
///   - LiveTestnet : not yet wired to Binance account sync — returns 0 (skip).
///   - LiveMainnet : ADR-0006 mainnet guard — always returns 0.
///
/// Returning 0 is a soft skip signal: the caller should treat it as
/// "do not size / do not ratchet for this mode this time".
/// </summary>
public interface IEquitySnapshotProvider
{
    /// <summary>Mark-to-market equity (cash + unrealized PnL).</summary>
    Task<decimal> GetEquityAsync(TradingMode mode, CancellationToken ct);

    /// <summary>
    /// Realized-only equity (cash balance after closed trades, no unrealized
    /// PnL contribution). Loop 12 — used by <c>EquityPeakTrackerService</c>
    /// to ratchet PeakEquity on realized growth only.
    /// </summary>
    Task<decimal> GetRealizedEquityAsync(TradingMode mode, CancellationToken ct);

    /// <summary>
    /// Loop 19 reform — dedicated sizing equity. Returns the realized PnL-based
    /// equity (StartingBalance + sum of closed RealizedPnl). The previous
    /// <see cref="GetEquityAsync"/> mark-to-market value was unstable when
    /// open positions were short-MTM-pumped: a $100 baseline could read $316
    /// with two open positions whose UnrealizedPnl spuriously inflated the
    /// VirtualBalance.Equity column, producing a $126 cap (40 percent of $316)
    /// and oversized 123 XRP allocations on a paper account.
    ///
    /// Sizing equity must be independent of in-flight position valuations — a
    /// strategy storm cannot inflate its own sizing budget. Hard upper bound:
    /// StartingBalance + true realized gains.
    /// </summary>
    Task<decimal> GetSizingEquityAsync(TradingMode mode, CancellationToken ct);
}
