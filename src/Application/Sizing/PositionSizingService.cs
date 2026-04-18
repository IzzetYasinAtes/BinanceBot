using BinanceBot.Application.Abstractions.Trading;

namespace BinanceBot.Application.Sizing;

/// <summary>
/// Default position sizing implementation (ADR-0011 §11.4 + decision-sizing.md Commit 4).
///
/// Algorithm:
///   1. Bail when equity or entry are non-positive.
///   2. Apply slippage to derive an effective entry price.
///   3. <c>qtyByRisk</c>  = (equity * riskPct) / stopDistance
///      <c>qtyByCap</c>   = (equity * maxPositionPct) / effectiveEntry
///      <c>qtyRaw</c>     = min(qtyByRisk, qtyByCap)
///   4. Snap downward to <c>stepSize</c> (LOT_SIZE filter).
///   5. Skip when stepped qty falls below <c>minQty</c>.
///   6. Skip when notional (qty * effectiveEntry) falls below <c>minNotional</c>.
///   7. When stop distance is unknown (0) the sizing collapses to the cap branch.
/// </summary>
public sealed class PositionSizingService : IPositionSizingService
{
    public PositionSizingResult Calculate(PositionSizingInput i)
    {
        if (i.Equity <= 0m)
        {
            return new PositionSizingResult(0m, 0m, "equity_zero");
        }
        if (i.EntryPrice <= 0m)
        {
            return new PositionSizingResult(0m, 0m, "entry_invalid");
        }

        var effectiveEntry = i.EntryPrice * (1m + i.SlippagePct);
        var riskAmount = i.Equity * i.RiskPct;

        var qtyByRisk = i.StopDistance > 0m
            ? riskAmount / i.StopDistance
            : decimal.MaxValue;

        var notionalCap = i.Equity * i.MaxPositionPct;
        var qtyByCap = effectiveEntry > 0m
            ? notionalCap / effectiveEntry
            : 0m;

        var qtyRaw = Math.Min(qtyByRisk, qtyByCap);
        if (qtyRaw <= 0m)
        {
            return new PositionSizingResult(0m, 0m, "qty_non_positive");
        }

        var qtyStepped = i.StepSize > 0m
            ? Math.Floor(qtyRaw / i.StepSize) * i.StepSize
            : qtyRaw;

        if (qtyStepped < i.MinQty)
        {
            return new PositionSizingResult(0m, qtyStepped * effectiveEntry, "qty_below_min_qty");
        }

        var notional = qtyStepped * effectiveEntry;
        if (notional < i.MinNotional)
        {
            return new PositionSizingResult(0m, notional, "min_notional_floor");
        }

        return new PositionSizingResult(qtyStepped, notional, null);
    }
}
