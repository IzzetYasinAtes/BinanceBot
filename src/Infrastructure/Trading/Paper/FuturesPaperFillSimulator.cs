using System.Text.Json;
using BinanceBot.Application.Abstractions.Trading;
using BinanceBot.Domain.Instruments;
using BinanceBot.Domain.MarketData;
using BinanceBot.Domain.Orders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BinanceBot.Infrastructure.Trading.Paper;

/// <summary>
/// Loop 92 — Futures paper-fill engine (ADR-0025). Replaces the Spot-only
/// <c>PaperFillSimulator</c>. Key Futures differences:
/// <list type="bullet">
///   <item>Taker fee: %0.05 (0.0005) yerine Spot %0.10. Both legs (BUY/SELL) commission in QUOTE asset (USDT).</item>
///   <item>Cash flow: <c>RealizedCashDelta</c> margin akışını temsil eder.
///         Long open: <c>-(price × qty / leverage) − fee</c>; Long close: <c>+notional ± realizedPnl − fee</c>.
///         Short open: aynı margin allocate, sadece kapanışta yön farklı.</item>
///   <item>positionSide=BOTH (One-way mode). Hedge mode emülasyonu yok (binance-expert spec §5).</item>
///   <item>Funding fee 8h cycle (00:00, 08:00, 16:00 UTC) — MVP'de FundingRateWorker
///         paper VirtualBalance'a uygular; bu simulator order-leg fee ile sınırlı.</item>
/// </list>
///
/// Order tipi rotası (Spot ile aynı): MARKET / LIMIT crossing → fill;
/// LimitMaker crossing → reject; non-crossing LIMIT → expire.
/// </summary>
public sealed class FuturesPaperFillSimulator : IPaperFillSimulator
{
    private static long _virtualTradeCounter;

    private readonly ILogger<FuturesPaperFillSimulator> _logger;
    private readonly PaperFillOptions _options;

    public FuturesPaperFillSimulator(
        ILogger<FuturesPaperFillSimulator> logger,
        IOptions<PaperFillOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    public async Task<PaperFillOutcome> SimulateAsync(
        Order order,
        Instrument instrument,
        BookTicker bookTicker,
        OrderBookSnapshot? depthSnapshot,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (_options.SimulatedLatencyMs > 0)
        {
            await Task.Delay(_options.SimulatedLatencyMs, cancellationToken);
        }

        var filterFailure = ValidateFilters(order, instrument);
        if (filterFailure is not null)
        {
            order.Reject(filterFailure, now);
            return new PaperFillOutcome(false, true, filterFailure, 0m, 0m, 0m);
        }

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
                        order.Expire(now);
                        _logger.LogDebug("Futures paper LIMIT not crossing, expired {Cid}", order.ClientOrderId);
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
        var levels = BuildLevels(order.Side, bookTicker, depthSnapshot);
        if (levels.Count == 0)
        {
            order.Reject("paper_no_liquidity", now);
            return new PaperFillOutcome(false, true, "no_liquidity", 0m, 0m, 0m);
        }

        var topPrice = levels[0].Price;
        var topPriceWithSlip = order.Side == OrderSide.Buy
            ? topPrice * (1m + _options.FixedSlippagePct)
            : topPrice * (1m - _options.FixedSlippagePct);
        var notionalEstimate = order.Quantity * topPriceWithSlip;
        if (notionalEstimate < instrument.MinNotional)
        {
            var reason = $"filter_MIN_NOTIONAL_{notionalEstimate}<{instrument.MinNotional}";
            order.Reject(reason, now);
            return new PaperFillOutcome(false, true, reason, 0m, 0m, 0m);
        }

        var fills = new List<(decimal Price, decimal Quantity)>();
        var remaining = order.Quantity;
        foreach (var lvl in levels)
        {
            if (remaining <= 0m) break;
            var take = Math.Min(lvl.Qty, remaining);
            if (take <= 0m) continue;

            var slipPrice = order.Side == OrderSide.Buy
                ? lvl.Price * (1m + _options.FixedSlippagePct)
                : lvl.Price * (1m - _options.FixedSlippagePct);

            fills.Add((slipPrice, take));
            remaining -= take;
        }

        if (remaining > 0m)
        {
            if (order.TimeInForce == TimeInForce.Fok)
            {
                order.Expire(now);
                return new PaperFillOutcome(false, false, "fok_expired", 0m, 0m, 0m);
            }
        }

        // Loop 92 — Futures fee schedule: taker %0.05 (0.0005) USDT-quote her iki leg için.
        // Spot ile farkı: BUY commission da quote (USDT), base asset değil.
        //
        // Loop 94 (Fix #2) — Futures cash semantics refactor.
        // Eskiden RealizedCashDelta = signedNotional - fee (Spot semantik):
        //   open BUY → wallet -= notional + fee (notional cash'ten düşüyordu, BUG)
        //   close SELL → wallet += notional - fee (PnL implicit kapanışta)
        // Yeni Futures semantik: notional MARGIN'a alınır (AllocatedMargin), wallet
        // sadece commission ile değişir. Margin allocate / return + realized PnL
        // wallet'a yansıtma <see cref="OrderFilledPositionHandler"/> tarafından
        // open/close branch'inde yapılır (simulator order context'sini bilmediği
        // için bu sorumluluğu üstlenmez).
        decimal realizedCash = 0m;
        decimal quoteCommissionTotal = 0m;
        foreach (var f in fills)
        {
            var notional = f.Price * f.Quantity;
            var quoteFee = PaperFeeSimulator.CalculateTakerFee(notional, _options.UseBnbFeeDiscount);
            var commissionAsset = instrument.QuoteAsset;  // Futures: always USDT

            var tradeId = Interlocked.Increment(ref _virtualTradeCounter);
            order.RegisterFill(tradeId, f.Price, f.Quantity, quoteFee, commissionAsset, now);

            // Loop 94: sadece commission düşülür. Margin akışı (AllocateMarginForPosition /
            // ReturnMarginAndApplyPnl) + realized PnL wallet'a OrderFilledPositionHandler'da.
            realizedCash -= quoteFee;
            quoteCommissionTotal += quoteFee;
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
            RealizedCashDelta: realizedCash,
            QuoteCommissionTotal: quoteCommissionTotal);
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
                    ? a.Price.CompareTo(b.Price)
                    : b.Price.CompareTo(a.Price));
                return levels;
            }
        }

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
