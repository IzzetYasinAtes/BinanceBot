using BinanceBot.Domain.Common;

namespace BinanceBot.Domain.Balances.Events;

public sealed record VirtualBalanceResetEvent(
    TradingMode Mode,
    Guid IterationId,
    decimal StartingBalance,
    DateTimeOffset StartedAt) : DomainEventBase;

public sealed record VirtualBalanceUpdatedEvent(
    TradingMode Mode,
    decimal CurrentBalance,
    decimal Equity) : DomainEventBase;

public sealed record VirtualBalanceFillAppliedEvent(
    TradingMode Mode,
    decimal RealizedDelta,
    decimal CurrentBalance,
    DateTimeOffset AppliedAt) : DomainEventBase;

/// <summary>
/// Loop 92 — Pozisyon margin allocate audit (ADR-0025). Pozisyon açılınca
/// VirtualBalance.AllocateMarginForPosition tarafından raise edilir.
/// </summary>
public sealed record PositionMarginAllocatedEvent(
    TradingMode Mode,
    decimal Margin,
    decimal AllocatedMarginTotal,
    DateTimeOffset AppliedAt) : DomainEventBase;

/// <summary>
/// Loop 92 — Pozisyon kapandığında margin geri + realizedPnl uygulanması audit'i.
/// </summary>
public sealed record PositionMarginReturnedEvent(
    TradingMode Mode,
    decimal Margin,
    decimal RealizedPnl,
    decimal WalletBalance,
    DateTimeOffset AppliedAt) : DomainEventBase;

/// <summary>
/// Loop 92 — 8h funding fee cycle audit. fundingDelta negatif: ödeme; pozitif: tahsilat.
/// </summary>
public sealed record FundingFeeAppliedEvent(
    TradingMode Mode,
    decimal FundingDelta,
    decimal WalletBalance,
    DateTimeOffset AppliedAt) : DomainEventBase;
