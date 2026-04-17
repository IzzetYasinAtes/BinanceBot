using System.Text.Json;
using BinanceBot.Application.Abstractions.Trading;
using BinanceBot.Domain.Instruments;
using BinanceBot.Domain.MarketData;
using BinanceBot.Domain.Orders;
using Microsoft.Extensions.Logging;

namespace BinanceBot.Infrastructure.Trading.Paper;

/// <summary>
/// Deterministic paper-fill engine (ADR-0008 §8.4 + docs/research/paper-fill-research.md).
/// VIP0 0.1% taker commission, BUY commission in base, SELL commission in quote.
/// MARKET orders walk depth asks (BUY) / bids (SELL) — with bookTicker single-level fallback.
/// </summary>
public sealed class PaperFillSimulator : IPaperFillSimulator
{
    private const decimal TakerFeeRate = 0.001m; // VIP0 0.1%, no BNB discount (V1)
    private static long _virtualTradeCounter;

    private readonly ILogger<PaperFillSimulator> _logger;

    public PaperFillSimulator(ILogger<PaperFillSimulator> logger)
    {
        _logger = logger;
    }

    public PaperFillOutcome Simulate(
        Order order,
        Instrument instrument,
        BookTicker bookTicker,
        OrderBookSnapshot? depthSnapshot,
        DateTimeOffset now)
    {
        // ---- STEP 1: Filter validation -------------------------------------------------
        var filterFailure = ValidateFilters(order, instrument);
        if (filterFailure is not null)
        {
            order.Reject(filterFailure, now);
            return new PaperFillOutcome(false, true, filterFailure, 0m, 0m, 0m);
        }

        // ---- STEP 2: Order type routing -----------------------------------------------
        // V1 scope: MARKET (+ LIMIT that crosses). Non-crossing LIMIT is treated as
        // immediate expire for paper because we don't have a persistent book simulator.
        var bestAsk = bookTicker.AskPrice;
        var bestBid = bookTicker.BidPrice;

        switch (order.Type)
        {
            case OrderType.Market:
                return FillMarket(order, instrument, bookTicker, depthSnapshot, now);

            case OrderType.Limit:
                {
                    var crossing = (order.Side == OrderSide.Buy && order.Price.HasValue && order.Price.Value >= bestAsk)
                                || (order.Side == OrderSide.Sell && order.Price.HasValue && order.Price.Value <= bestBid);
                    if (!crossing)
                    {
                        // Paper V1: we don't hold resting orders — treat as immediate expire.
                        order.Expire(now);
                        _logger.LogDebug("Paper LIMIT not crossing, expired {Cid}", order.ClientOrderId);
                        return new PaperFillOutcome(false, false, "limit_not_crossing", 0m, 0m, 0m);
                    }
                    return FillMarket(order, instrument, bookTicker, depthSnapshot, now);
                }

            case OrderType.LimitMaker:
                {
                    var crossing = (order.Side == OrderSide.Buy && order.Price.HasValue && order.Price.Value >= bestAsk)
                                || (order.Side == OrderSide.Sell && order.Price.HasValue && order.Price.Value <= bestBid);
                    if (crossing)
                    {
                        order.Reject("-2010 would immediately match", now);
                        return new PaperFillOutcome(false, true, "limit_maker_would_match", 0m, 0m, 0m);
                    }
                    order.Expire(now);
                    return new PaperFillOutcome(false, false, "limit_maker_resting_not_supported", 0m, 0m, 0m);
                }

            default:
                // STOP_LOSS / TAKE_PROFIT variants: paper V1 is not wired to last price triggers.
                order.Reject($"paper_unsupported_type_{order.Type}", now);
                return new PaperFillOutcome(false, true, "unsupported_type", 0m, 0m, 0m);
        }
    }

    private PaperFillOutcome FillMarket(
        Order order,
        Instrument instrument,
        BookTicker bookTicker,
        OrderBookSnapshot? depthSnapshot,
        DateTimeOffset now)
    {
        // ---- STEP 3: Depth walking ----------------------------------------------------
        var levels = BuildLevels(order.Side, bookTicker, depthSnapshot);
        if (levels.Count == 0)
        {
            order.Reject("paper_no_liquidity", now);
            return new PaperFillOutcome(false, true, "no_liquidity", 0m, 0m, 0m);
        }

        var fills = new List<(decimal Price, decimal Quantity)>();
        var remaining = order.Quantity;
        foreach (var lvl in levels)
        {
            if (remaining <= 0m) break;
            var take = Math.Min(lvl.Qty, remaining);
            if (take <= 0m) continue;
            fills.Add((lvl.Price, take));
            remaining -= take;
        }

        // ---- STEP 4: TIF enforcement --------------------------------------------------
        if (remaining > 0m)
        {
            if (order.TimeInForce == TimeInForce.Fok)
            {
                // FOK: can't fully fill — expire without any fills registered
                order.Expire(now);
                return new PaperFillOutcome(false, false, "fok_expired", 0m, 0m, 0m);
            }
            // IOC/GTC market partial — register what we got then expire if not MARKET-complete
        }

        // ---- STEP 5: Commission + RegisterFill ---------------------------------------
        decimal realizedCash = 0m;
        foreach (var f in fills)
        {
            var (commission, commissionAsset) = ComputeCommission(order.Side, f.Price, f.Quantity, instrument);

            var tradeId = Interlocked.Increment(ref _virtualTradeCounter);
            order.RegisterFill(tradeId, f.Price, f.Quantity, commission, commissionAsset, now);

            // Cash delta (for Paper VirtualBalance). BUY spends quote + base-commission (converted to quote),
            // SELL gains quote - quote-commission. We keep this in QUOTE currency terms.
            if (order.Side == OrderSide.Buy)
            {
                // Spend quote: -price*qty; base commission is taken from received base (doesn't affect cash)
                realizedCash -= f.Price * f.Quantity;
            }
            else
            {
                // Receive quote: +price*qty - quote commission
                realizedCash += (f.Price * f.Quantity) - commission;
            }
        }

        if (remaining > 0m && order.TimeInForce != TimeInForce.Gtc)
        {
            order.Expire(now);
        }

        var executed = order.ExecutedQuantity;
        var avg = executed > 0m ? order.CumulativeQuoteQty / executed : 0m;

        return new PaperFillOutcome(
            Filled: order.Status == OrderStatus.Filled,
            Rejected: false,
            RejectReason: null,
            ExecutedQuantity: executed,
            AvgFillPrice: avg,
            RealizedCashDelta: realizedCash);
    }

    private static (decimal Commission, string Asset) ComputeCommission(
        OrderSide side, decimal price, decimal quantity, Instrument instrument)
    {
        if (side == OrderSide.Buy)
        {
            // Commission in base asset
            var commission = quantity * TakerFeeRate;
            return (commission, instrument.BaseAsset);
        }
        else
        {
            var commission = price * quantity * TakerFeeRate;
            return (commission, instrument.QuoteAsset);
        }
    }

    private static string? ValidateFilters(Order order, Instrument instrument)
    {
        if (order.Quantity < instrument.MinQty || order.Quantity > instrument.MaxQty)
        {
            return $"filter_LOT_SIZE_{order.Quantity}_not_in_[{instrument.MinQty},{instrument.MaxQty}]";
        }

        if (instrument.StepSize > 0m && order.Quantity % instrument.StepSize != 0m)
        {
            return $"filter_LOT_SIZE_step_{instrument.StepSize}_mismatch";
        }

        if (order.Type is OrderType.Limit or OrderType.LimitMaker)
        {
            if (order.Price is null || order.Price.Value <= 0m)
            {
                return "filter_PRICE_missing";
            }
            if (instrument.TickSize > 0m && order.Price.Value % instrument.TickSize != 0m)
            {
                return $"filter_PRICE_FILTER_tick_{instrument.TickSize}_mismatch";
            }
            var notional = order.Quantity * order.Price.Value;
            if (notional < instrument.MinNotional)
            {
                return $"filter_MIN_NOTIONAL_{notional}<{instrument.MinNotional}";
            }
        }

        return null;
    }

    private static List<LevelTuple> BuildLevels(
        OrderSide side,
        BookTicker bookTicker,
        OrderBookSnapshot? depth)
    {
        if (depth is not null)
        {
            var json = side == OrderSide.Buy ? depth.AsksJson : depth.BidsJson;
            var levels = ParseLevels(json);
            if (levels.Count > 0)
            {
                levels.Sort((a, b) => side == OrderSide.Buy
                    ? a.Price.CompareTo(b.Price)  // BUY walks asks ascending
                    : b.Price.CompareTo(a.Price)); // SELL walks bids descending
                return levels;
            }
        }

        // Fallback: single top-of-book level (slippage=0)
        if (side == OrderSide.Buy)
        {
            if (bookTicker.AskPrice > 0m && bookTicker.AskQuantity > 0m)
            {
                return [new LevelTuple(bookTicker.AskPrice, bookTicker.AskQuantity)];
            }
        }
        else
        {
            if (bookTicker.BidPrice > 0m && bookTicker.BidQuantity > 0m)
            {
                return [new LevelTuple(bookTicker.BidPrice, bookTicker.BidQuantity)];
            }
        }

        return [];
    }

    private static List<LevelTuple> ParseLevels(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            using var doc = JsonDocument.Parse(json);
            var levels = new List<LevelTuple>();
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return levels;
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Array) continue;
                var arr = item.EnumerateArray().ToArray();
                if (arr.Length < 2) continue;
                var price = ParseDecimal(arr[0]);
                var qty = ParseDecimal(arr[1]);
                if (price > 0m && qty > 0m)
                {
                    levels.Add(new LevelTuple(price, qty));
                }
            }
            return levels;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static decimal ParseDecimal(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Number => element.GetDecimal(),
            JsonValueKind.String => decimal.TryParse(
                element.GetString(),
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out var d) ? d : 0m,
            _ => 0m,
        };
    }

    private readonly record struct LevelTuple(decimal Price, decimal Qty);
}
