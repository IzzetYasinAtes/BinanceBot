using System.Text.Json;
using BinanceBot.Application.Abstractions;
using BinanceBot.Domain.Common;
using BinanceBot.Domain.Orders;
using BinanceBot.Domain.Orders.Events;
using BinanceBot.Domain.Positions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BinanceBot.Infrastructure.Orders;

/// <summary>
/// Position glue: when an order fills, open a new <see cref="Position"/> aggregate,
/// add to the existing one, or close the opposite-side one (ADR-0011 §11.6).
///
/// Per decision-sizing.md Commit 8 this also handles **reverse-side fills as closes**
/// so signal-driven exits (<c>CloseSignalPositionCommand</c>) reactively flip the
/// Position from Open to Closed without an admin call.
/// </summary>
public sealed class OrderFilledPositionHandler : INotificationHandler<OrderFilledEvent>
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IClock _clock;
    private readonly ILogger<OrderFilledPositionHandler> _logger;

    public OrderFilledPositionHandler(
        IServiceScopeFactory scopeFactory,
        IClock clock,
        ILogger<OrderFilledPositionHandler> logger)
    {
        _scopeFactory = scopeFactory;
        _clock = clock;
        _logger = logger;
    }

    public async Task Handle(OrderFilledEvent notification, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        // Tracking read so we can mutate the order's mirrored Position safely.
        var order = await db.Orders
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.ClientOrderId == notification.ClientOrderId
                                   && o.Mode == notification.Mode, cancellationToken);
        if (order is null)
        {
            _logger.LogWarning("OrderFilled handler: order {Cid} {Mode} not found",
                notification.ClientOrderId, notification.Mode);
            return;
        }

        if (order.ExecutedQuantity == 0m)
        {
            return;
        }

        var fillPrice = order.CumulativeQuoteQty / order.ExecutedQuantity;
        var fillQty = order.ExecutedQuantity;
        var now = _clock.UtcNow;

        // ADR-0020 §20.7 — aggregate quote-denominated commission for this order's
        // fills so the Position aggregate tracks fee-aware RealizedPnl. BUY legs
        // report commission in base asset (quote-equivalent = fee * price); SELL
        // legs report commission already in quote currency. Same aggregation
        // applies for live fills so mainnet/testnet reuse the same pathway when
        // ADR-0006 unblocks.
        var orderQuoteFee = await db.OrderFills
            .AsNoTracking()
            .Where(f => f.OrderId == order.Id)
            .SumAsync(f => order.Side == OrderSide.Buy
                ? f.Commission * f.Price
                : f.Commission, cancellationToken);

        var openPosition = await db.Positions
            .FirstOrDefaultAsync(p =>
                p.Symbol == order.Symbol &&
                p.Mode == order.Mode &&
                p.Status == PositionStatus.Open, cancellationToken);

        // ADR-0014 §14.5 + ADR-0017 §17.7 + decision-pattern-reform.md C9 (option B):
        // the evaluator's max-hold budget travels in StrategySignal.ContextJson (the
        // Order schema doesn't carry it). Look up the most recent signal for the same
        // (symbol, strategy) within a 5-minute freshness window and decode one of:
        //   - <c>maxHoldMinutes</c> (ADR-0016 VWAP-EMA V2 — preferred)
        //   - <c>maxHoldBars</c>    (ADR-0014 pattern scalping — 1 bar = 1 minute)
        // Both alternatives are accepted so the handler is forward- and backward-
        // compatible across the pattern -> VWAP-EMA transition (Loop 21 regression).
        // When neither key is present Position.MaxHoldDuration stays null and the
        // time stop branch of the monitor is inert.
        TimeSpan? maxHoldDuration = null;
        if (order.StrategyId is long sid)
        {
            var freshnessCutoff = now.AddMinutes(-5);
            var lastSignal = await db.StrategySignals.AsNoTracking()
                .Where(s => s.Symbol == order.Symbol
                    && s.StrategyId == sid
                    && s.EmittedAt >= freshnessCutoff)
                .OrderByDescending(s => s.EmittedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (lastSignal is not null && !string.IsNullOrWhiteSpace(lastSignal.ContextJson))
            {
                try
                {
                    using var doc = JsonDocument.Parse(lastSignal.ContextJson);
                    int? holdMinutes = null;

                    // Prefer maxHoldMinutes (VWAP-EMA V2), fall back to maxHoldBars
                    // (pattern strategies). BinanceBot 1m bars -> 1 bar = 1 minute.
                    if (doc.RootElement.TryGetProperty("maxHoldMinutes", out var mhMin)
                        && mhMin.ValueKind == JsonValueKind.Number
                        && mhMin.TryGetInt32(out var minutesFromJson)
                        && minutesFromJson > 0)
                    {
                        holdMinutes = minutesFromJson;
                    }
                    else if (doc.RootElement.TryGetProperty("maxHoldBars", out var mhb)
                        && mhb.ValueKind == JsonValueKind.Number
                        && mhb.TryGetInt32(out var barsFromJson)
                        && barsFromJson > 0)
                    {
                        holdMinutes = barsFromJson;
                    }

                    if (holdMinutes is int m && m > 0)
                    {
                        maxHoldDuration = TimeSpan.FromMinutes(m);
                    }
                }
                catch (JsonException)
                {
                    // Non-structured ContextJson — time stop stays inert.
                }
            }
        }

        try
        {
            if (openPosition is null)
            {
                var newDirection = order.Side == OrderSide.Buy ? TradeDirection.Long : TradeDirection.Short;
                // ADR-0012 §12.4: forward Order.StopPrice into Position so StopLossMonitorService
                // can soft-trigger an exit when mark price crosses the level.
                // Loop 10 take-profit fix: same forwarding pattern for Order.TakeProfit so
                // TakeProfitMonitorService can realize gains on the resulting Position.
                // ADR-0014 §14.5: maxHoldDuration sourced above from StrategySignal.ContextJson.
                var pos = Position.Open(order.Symbol, newDirection, fillQty, fillPrice,
                    order.StopPrice, order.StrategyId, order.Mode, now,
                    entryCommission: orderQuoteFee,
                    takeProfit: order.TakeProfit,
                    maxHoldDuration: maxHoldDuration);
                db.Positions.Add(pos);
                _logger.LogInformation(
                    "Position opened from fill {Cid} direction={Direction} qty={Qty} price={Price}",
                    notification.ClientOrderId, newDirection, fillQty, fillPrice);
            }
            else
            {
                var sameSide =
                    (openPosition.Direction == TradeDirection.Long && order.Side == OrderSide.Buy) ||
                    (openPosition.Direction == TradeDirection.Short && order.Side == OrderSide.Sell);

                if (sameSide)
                {
                    // ADR-0020 §20.6 — partial fill commission accumulates on the
                    // aggregate via AddFill's addCommission parameter.
                    openPosition.AddFill(fillQty, fillPrice, now, addCommission: orderQuoteFee);
                    _logger.LogInformation(
                        "Position {Pos} added fill {Cid} qty={Qty} fee={Fee}",
                        openPosition.Id, notification.ClientOrderId, fillQty, orderQuoteFee);
                }
                else if (fillQty >= openPosition.Quantity)
                {
                    // ADR-0020 §20.6 — the reverse fill closes the position so the
                    // order's quote commission funds the ExitCommission ledger.
                    // Any leftover flip-side quantity carries the remainder as the
                    // new position's entry commission (pro-rata by qty share).
                    decimal exitFee;
                    decimal flipEntryFee;
                    if (fillQty > 0m)
                    {
                        var closeShare = openPosition.Quantity / fillQty;
                        exitFee = orderQuoteFee * closeShare;
                        flipEntryFee = orderQuoteFee - exitFee;
                    }
                    else
                    {
                        exitFee = 0m;
                        flipEntryFee = 0m;
                    }

                    openPosition.Close(fillPrice, $"order_{notification.ClientOrderId}", now,
                        exitCommission: exitFee);

                    // Loop 73 zombi-position diagnostic guard: Position.Close() sets
                    // Status=Closed + ClosedAt + ExitPrice atomically inside the
                    // domain method. Any future refactor that breaks this invariant
                    // would silently produce "zombi" rows (Status=Closed but
                    // ClosedAt=NULL). Fail loudly here so a regression surfaces in
                    // logs immediately rather than leaking to the DB.
                    if (openPosition.Status != PositionStatus.Closed
                        || openPosition.ClosedAt is null
                        || openPosition.ExitPrice is null)
                    {
                        _logger.LogError(
                            "ZOMBI-GUARD Position.Close() left aggregate inconsistent: pos={Pos} status={Status} closedAt={ClosedAt} exitPrice={ExitPrice}",
                            openPosition.Id, openPosition.Status, openPosition.ClosedAt, openPosition.ExitPrice);
                    }

                    _logger.LogInformation(
                        "Position {Pos} closed by reverse fill {Cid} price={Price} fee={Fee}",
                        openPosition.Id, notification.ClientOrderId, fillPrice, exitFee);

                    var leftover = fillQty - openPosition.Quantity;
                    if (leftover > 0m)
                    {
                        var flipDirection = order.Side == OrderSide.Buy
                            ? TradeDirection.Long
                            : TradeDirection.Short;
                        // The flip uses the same incoming stop / take-profit hints as the closing entry order.
                        var flip = Position.Open(order.Symbol, flipDirection, leftover, fillPrice,
                            order.StopPrice, order.StrategyId, order.Mode, now,
                            entryCommission: flipEntryFee,
                            takeProfit: order.TakeProfit,
                            maxHoldDuration: maxHoldDuration);
                        db.Positions.Add(flip);
                        _logger.LogInformation(
                            "Position {Pos} flipped from leftover qty={Qty}",
                            flip.Id, leftover);
                    }
                }
                else
                {
                    // Partial close: requires aggregate change (Position.Reduce) — not yet supported.
                    _logger.LogWarning(
                        "Partial close not yet supported pos={Pos} order={Cid} fillQty={Fq} posQty={Pq}",
                        openPosition.Id, notification.ClientOrderId, fillQty, openPosition.Quantity);
                }
            }

            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Domain.Common.DomainException ex)
        {
            _logger.LogWarning(ex,
                "Position update failed for filled order {Cid}: {Message}",
                notification.ClientOrderId, ex.Message);
        }
    }
}
