using BinanceBot.Api.Infrastructure;
using BinanceBot.Application.System.Queries.GetSystemStatus;
using BinanceBot.Application.System.Queries.TailSystemEvents;
using MediatR;

namespace BinanceBot.Api.Endpoints;

public static class SystemEndpoints
{
    public static IEndpointRouteBuilder MapSystemEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/system/status", async (IMediator m, CancellationToken ct) =>
            (await m.Send(new GetSystemStatusQuery(), ct)).ToHttpResult())
            .WithName("GetSystemStatus")
            .WithTags("System");

        app.MapGet("/api/logs/tail", async (
                long? since, string? level, int? limit,
                IMediator m, CancellationToken ct) =>
            (await m.Send(new TailSystemEventsQuery(since, level, limit ?? 200), ct)).ToHttpResult())
            .WithName("TailSystemEvents")
            .WithTags("System");

        return app;
    }
}
