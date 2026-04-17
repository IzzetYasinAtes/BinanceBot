using BinanceBot.Api.Infrastructure;
using BinanceBot.Application.Orders.Commands.CancelOrder;
using BinanceBot.Application.Orders.Commands.PlaceOrder;
using BinanceBot.Application.Orders.Queries.GetOrderByClientId;
using BinanceBot.Application.Orders.Queries.ListOpenOrders;
using BinanceBot.Application.Orders.Queries.ListOrderHistory;
using MediatR;

namespace BinanceBot.Api.Endpoints;

public static class OrderEndpoints
{
    public sealed record PlaceOrderRequest(
        string ClientOrderId,
        string Symbol,
        string Side,
        string Type,
        string TimeInForce,
        decimal Quantity,
        decimal? Price,
        decimal? StopPrice,
        long? StrategyId);

    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/orders").WithTags("Orders");

        group.MapGet("/open", async (string? symbol, IMediator m, CancellationToken ct) =>
            (await m.Send(new ListOpenOrdersQuery(symbol), ct)).ToHttpResult())
            .WithName("ListOpenOrders");

        group.MapGet("/history", async (
                string? symbol, DateTimeOffset? from, DateTimeOffset? to,
                int? skip, int? take,
                IMediator m, CancellationToken ct) =>
            (await m.Send(new ListOrderHistoryQuery(symbol, from, to, skip ?? 0, take ?? 50), ct))
                .ToHttpResult())
            .WithName("ListOrderHistory");

        group.MapGet("/{clientOrderId}", async (string clientOrderId, IMediator m, CancellationToken ct) =>
            (await m.Send(new GetOrderByClientIdQuery(clientOrderId), ct)).ToHttpResult())
            .WithName("GetOrderByClientId");

        group.MapPost("/", async (PlaceOrderRequest req, IMediator m, CancellationToken ct) =>
            (await m.Send(new PlaceOrderCommand(
                req.ClientOrderId, req.Symbol, req.Side, req.Type, req.TimeInForce,
                req.Quantity, req.Price, req.StopPrice, req.StrategyId, DryRun: true), ct))
                .ToHttpResult())
            .WithName("PlaceOrder");

        group.MapDelete("/{clientOrderId}", async (string clientOrderId, string? reason, IMediator m, CancellationToken ct) =>
            (await m.Send(new CancelOrderCommand(clientOrderId, reason ?? "user_cancelled"), ct))
                .ToHttpResult())
            .WithName("CancelOrder");

        return app;
    }
}
