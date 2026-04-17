using System.Text.Json;
using Ardalis.Result;
using BinanceBot.Application.Abstractions;
using BinanceBot.Application.Abstractions.Binance;
using BinanceBot.Domain.Common;
using BinanceBot.Domain.Instruments;
using BinanceBot.Domain.Orders;
using BinanceBot.Domain.RiskProfiles;
using BinanceBot.Domain.SystemEvents;
using BinanceBot.Domain.ValueObjects;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BinanceBot.Application.Orders.Commands.PlaceOrder;

public sealed record PlaceOrderCommand(
    string ClientOrderId,
    string Symbol,
    string Side,
    string Type,
    string TimeInForce,
    decimal Quantity,
    decimal? Price,
    decimal? StopPrice,
    long? StrategyId,
    bool DryRun) : IRequest<Result<PlacedOrderDto>>;

public sealed record PlacedOrderDto(
    string ClientOrderId,
    string Symbol,
    string Status,
    decimal Quantity,
    decimal? Price,
    bool DryRun);

public sealed class PlaceOrderCommandValidator : AbstractValidator<PlaceOrderCommand>
{
    public PlaceOrderCommandValidator()
    {
        RuleFor(c => c.ClientOrderId).NotEmpty().MaximumLength(36);
        RuleFor(c => c.Symbol).NotEmpty();
        RuleFor(c => c.Side).Must(s => Enum.TryParse<OrderSide>(s, true, out _));
        RuleFor(c => c.Type).Must(t => Enum.TryParse<OrderType>(t, true, out _));
        RuleFor(c => c.TimeInForce).Must(t => Enum.TryParse<TimeInForce>(t, true, out _));
        RuleFor(c => c.Quantity).GreaterThan(0m);
    }
}

public sealed class PlaceOrderCommandHandler
    : IRequestHandler<PlaceOrderCommand, Result<PlacedOrderDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly IBinanceTrading _trading;
    private readonly IClock _clock;
    private readonly ICorrelationIdAccessor _correlation;
    private readonly ILogger<PlaceOrderCommandHandler> _logger;

    public PlaceOrderCommandHandler(
        IApplicationDbContext db,
        IBinanceTrading trading,
        IClock clock,
        ICorrelationIdAccessor correlation,
        ILogger<PlaceOrderCommandHandler> logger)
    {
        _db = db;
        _trading = trading;
        _clock = clock;
        _correlation = correlation;
        _logger = logger;
    }

    public async Task<Result<PlacedOrderDto>> Handle(PlaceOrderCommand request, CancellationToken ct)
    {
        var existing = await _db.Orders
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.ClientOrderId == request.ClientOrderId, ct);
        if (existing is not null)
        {
            return Result.Success(new PlacedOrderDto(
                existing.ClientOrderId, existing.Symbol.Value, existing.Status.ToString(),
                existing.Quantity, existing.Price, request.DryRun));
        }

        if (!Enum.TryParse<OrderSide>(request.Side, true, out var side)
            || !Enum.TryParse<OrderType>(request.Type, true, out var type)
            || !Enum.TryParse<TimeInForce>(request.TimeInForce, true, out var tif))
        {
            return Result<PlacedOrderDto>.Invalid(new ValidationError(
                "Enum", "Invalid side/type/timeInForce", null, ValidationSeverity.Error));
        }

        var symbolVo = Symbol.From(request.Symbol);
        var instrument = await _db.Instruments
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Symbol == symbolVo, ct);
        if (instrument is null)
        {
            return Result<PlacedOrderDto>.NotFound($"Instrument {symbolVo} not registered.");
        }

        var filterResult = ValidateFilters(instrument, request.Quantity, request.Price, type);
        if (!filterResult.IsSuccess)
        {
            return filterResult;
        }

        var riskProfile = await _db.RiskProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == RiskProfile.SingletonId, ct);
        if (riskProfile is null)
        {
            return Result<PlacedOrderDto>.Error("Risk profile missing.");
        }

        var riskResult = RiskGate(riskProfile);
        if (!riskResult.IsSuccess)
        {
            return riskResult;
        }

        var order = Order.Place(
            request.ClientOrderId, symbolVo, side, type, tif,
            request.Quantity, request.Price, request.StopPrice,
            request.StrategyId, _clock.UtcNow);

        _db.Orders.Add(order);

        var binanceReq = new PlaceOrderRequest(
            symbolVo.Value, side.ToString().ToUpperInvariant(),
            type.ToString().ToUpperInvariant(), tif.ToString().ToUpperInvariant(),
            request.Quantity, request.Price, request.StopPrice, request.ClientOrderId);

        if (request.DryRun)
        {
            _logger.LogInformation("Placing DRY-RUN order {Cid} {Symbol} {Side} {Qty}",
                request.ClientOrderId, symbolVo, side, request.Quantity);

            var testResult = await _trading.PlaceTestOrderAsync(binanceReq, ct);
            if (!testResult.Accepted)
            {
                order.Reject(testResult.ErrorMessage ?? "test_endpoint_failed", _clock.UtcNow);
            }
        }
        else
        {
            _logger.LogInformation("Placing LIVE order {Cid} {Symbol} {Side} {Qty}",
                request.ClientOrderId, symbolVo, side, request.Quantity);

            var liveResult = await _trading.PlaceLiveOrderAsync(binanceReq, ct);
            if (!liveResult.Accepted)
            {
                order.Reject(liveResult.ErrorMessage ?? liveResult.ErrorCode ?? "live_endpoint_failed",
                    _clock.UtcNow);
            }
            else
            {
                if (liveResult.ExchangeOrderId is long xid)
                {
                    order.AttachExchangeId(xid, _clock.UtcNow);
                }
                foreach (var fill in liveResult.Fills)
                {
                    order.RegisterFill(
                        fill.TradeId, fill.Price, fill.Quantity,
                        fill.Commission, fill.CommissionAsset, _clock.UtcNow);
                }
            }
        }

        _db.SystemEvents.Add(SystemEvent.Record(
            eventType: "order.placed",
            severity: order.Status == OrderStatus.Rejected ? SystemEventSeverity.Warning : SystemEventSeverity.Info,
            payloadJson: JsonSerializer.Serialize(new
            {
                order.ClientOrderId,
                Symbol = order.Symbol.Value,
                Side = order.Side.ToString(),
                Type = order.Type.ToString(),
                order.Quantity,
                order.Price,
                Status = order.Status.ToString(),
                request.DryRun,
            }),
            source: "PlaceOrderCommand",
            correlationId: _correlation.CorrelationId == Guid.Empty ? null : _correlation.CorrelationId,
            occurredAt: _clock.UtcNow));

        await _db.SaveChangesAsync(ct);

        return Result.Success(new PlacedOrderDto(
            order.ClientOrderId, order.Symbol.Value, order.Status.ToString(),
            order.Quantity, order.Price, request.DryRun));
    }

    private static Result<PlacedOrderDto> ValidateFilters(
        Instrument instrument, decimal qty, decimal? price, OrderType type)
    {
        if (qty < instrument.MinQty || qty > instrument.MaxQty)
        {
            return Result<PlacedOrderDto>.Invalid(new ValidationError(
                "Quantity",
                $"Quantity {qty} out of LOT_SIZE range [{instrument.MinQty},{instrument.MaxQty}]",
                null, ValidationSeverity.Error));
        }

        if (instrument.StepSize > 0m)
        {
            var rem = qty % instrument.StepSize;
            if (rem != 0m)
            {
                return Result<PlacedOrderDto>.Invalid(new ValidationError(
                    "Quantity",
                    $"Quantity {qty} not aligned with stepSize {instrument.StepSize}",
                    null, ValidationSeverity.Error));
            }
        }

        if (type is OrderType.Limit or OrderType.LimitMaker)
        {
            if (price is null || price <= 0m)
            {
                return Result<PlacedOrderDto>.Invalid(new ValidationError(
                    "Price", "Limit orders require positive Price", null, ValidationSeverity.Error));
            }
            if (instrument.TickSize > 0m)
            {
                var rem = price.Value % instrument.TickSize;
                if (rem != 0m)
                {
                    return Result<PlacedOrderDto>.Invalid(new ValidationError(
                        "Price",
                        $"Price {price} not aligned with tickSize {instrument.TickSize}",
                        null, ValidationSeverity.Error));
                }
            }
            var notional = qty * price.Value;
            if (notional < instrument.MinNotional)
            {
                return Result<PlacedOrderDto>.Invalid(new ValidationError(
                    "Notional",
                    $"Notional {notional} below MIN_NOTIONAL {instrument.MinNotional}",
                    null, ValidationSeverity.Error));
            }
        }

        return Result.Success<PlacedOrderDto>(null!);
    }

    private static Result<PlacedOrderDto> RiskGate(RiskProfile profile)
    {
        if (profile.CircuitBreakerStatus == CircuitBreakerStatus.Tripped)
        {
            return Result<PlacedOrderDto>.Forbidden($"Circuit breaker TRIPPED: {profile.CircuitBreakerReason}");
        }
        if (profile.ConsecutiveLosses >= profile.MaxConsecutiveLosses)
        {
            return Result<PlacedOrderDto>.Forbidden(
                $"Consecutive losses {profile.ConsecutiveLosses} >= cap {profile.MaxConsecutiveLosses}");
        }
        if (profile.CurrentDrawdownPct >= profile.MaxDrawdownAllTimePct)
        {
            return Result<PlacedOrderDto>.Forbidden(
                $"Drawdown {profile.CurrentDrawdownPct:P2} >= cap {profile.MaxDrawdownAllTimePct:P2}");
        }
        return Result.Success<PlacedOrderDto>(null!);
    }
}
