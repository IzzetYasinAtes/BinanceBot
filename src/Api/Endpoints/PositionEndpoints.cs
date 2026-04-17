using BinanceBot.Api.Infrastructure;
using BinanceBot.Application.Positions.Commands.ClosePosition;
using BinanceBot.Application.Positions.Queries.GetPositionPnl;
using BinanceBot.Application.Positions.Queries.GetTodayPnl;
using BinanceBot.Application.Positions.Queries.ListPositions;
using MediatR;

namespace BinanceBot.Api.Endpoints;

public static class PositionEndpoints
{
    public sealed record ClosePositionRequest(decimal ExitPrice, string Reason);

    public static IEndpointRouteBuilder MapPositionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/positions").WithTags("Positions");

        group.MapGet("/", async (
                string? status, string? symbol,
                DateTimeOffset? from, DateTimeOffset? to,
                IMediator m, CancellationToken ct) =>
            (await m.Send(new ListPositionsQuery(status, symbol, from, to), ct)).ToHttpResult())
            .WithName("ListPositions");

        group.MapGet("/pnl/today", async (IMediator m, CancellationToken ct) =>
            (await m.Send(new GetTodayPnlQuery(), ct)).ToHttpResult())
            .WithName("GetTodayPnl");

        group.MapGet("/{symbol}/pnl", async (string symbol, IMediator m, CancellationToken ct) =>
            (await m.Send(new GetPositionPnlQuery(symbol), ct)).ToHttpResult())
            .WithName("GetPositionPnl");

        group.MapPost("/{symbol}/close", async (string symbol, ClosePositionRequest req, IMediator m, CancellationToken ct) =>
            (await m.Send(new ClosePositionCommand(symbol, req.ExitPrice, req.Reason), ct))
                .ToHttpResult())
            .WithName("ClosePosition");

        return app;
    }
}
