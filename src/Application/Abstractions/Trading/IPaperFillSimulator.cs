using BinanceBot.Domain.Instruments;
using BinanceBot.Domain.MarketData;
using BinanceBot.Domain.Orders;

namespace BinanceBot.Application.Abstractions.Trading;

/// <summary>
/// Deterministic paper-fill engine (ADR-0008 §8.4 + docs/research/paper-fill-research.md).
/// Consumes order book + top-of-book snapshot and emits fills onto the Order aggregate.
/// No DB side effects — pure decision engine; caller persists via DbContext.
///
/// ADR-0012 §12.9: signature became async to allow a configurable latency simulator at the
/// top of the body (mainnet MARKET round-trip ≈ 100ms).
/// </summary>
public interface IPaperFillSimulator
{
    /// <summary>
    /// Simulate execution of <paramref name="order"/> against the provided market state.
    /// Mutates the order (RegisterFill/Expire/Reject) with virtual trade ids.
    /// </summary>
    /// <returns>Summary describing the simulation outcome.</returns>
    Task<PaperFillOutcome> SimulateAsync(
        Order order,
        Instrument instrument,
        BookTicker bookTicker,
        OrderBookSnapshot? depthSnapshot,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

public sealed record PaperFillOutcome(
    bool Filled,
    bool Rejected,
    string? RejectReason,
    decimal ExecutedQuantity,
    decimal AvgFillPrice,
    decimal RealizedCashDelta);
