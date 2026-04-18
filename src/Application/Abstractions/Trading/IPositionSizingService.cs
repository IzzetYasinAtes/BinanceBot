namespace BinanceBot.Application.Abstractions.Trading;

/// <summary>
/// Equity-aware position sizing (ADR-0011 §11.4).
/// Pure, stateless calculation — caller provides equity / instrument filters / risk caps.
/// Returns either a snapped quantity ready for PlaceOrderCommand or a SkipReason
/// (no exception, no partial behaviour).
/// </summary>
public interface IPositionSizingService
{
    PositionSizingResult Calculate(PositionSizingInput input);
}

public sealed record PositionSizingInput(
    decimal Equity,
    decimal EntryPrice,
    decimal StopDistance,
    decimal RiskPct,
    decimal MaxPositionPct,
    decimal MinNotional,
    decimal StepSize,
    decimal MinQty,
    decimal SlippagePct);

public sealed record PositionSizingResult(
    decimal Quantity,
    decimal NotionalEstimate,
    string? SkipReason);
