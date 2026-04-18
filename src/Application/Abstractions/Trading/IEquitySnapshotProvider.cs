using BinanceBot.Domain.Common;

namespace BinanceBot.Application.Abstractions.Trading;

/// <summary>
/// Mode-aware equity snapshot used by sizing (ADR-0011 §11.3).
///
///   - Paper       : VirtualBalance.Equity (or CurrentBalance fallback) for the Paper row.
///   - LiveTestnet : not yet wired to Binance account sync — returns 0 (skip sizing).
///   - LiveMainnet : ADR-0006 mainnet guard — always returns 0 (no real orders allowed).
///
/// Returning 0 is a soft skip signal: the caller should treat it as
/// "do not size for this mode this time".
/// </summary>
public interface IEquitySnapshotProvider
{
    Task<decimal> GetEquityAsync(TradingMode mode, CancellationToken ct);
}
