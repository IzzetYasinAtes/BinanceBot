using BinanceBot.Api.Infrastructure;
using BinanceBot.Application.Strategies.Commands.CreateStrategy;
using BinanceBot.Application.Strategies.Commands.UpdateStrategyParameters;
using BinanceBot.Application.Strategies.Queries.GetLatestSignals;
using BinanceBot.Application.Strategies.Queries.GetStrategyDetail;
using BinanceBot.Application.Strategies.Queries.GetStrategySignals;
using BinanceBot.Application.Strategies.Queries.ListStrategies;
using MediatR;

namespace BinanceBot.Api.Endpoints;

public static class StrategyEndpoints
{
    public sealed record CreateStrategyRequest(string Name, string Type, string[] Symbols, string? ParametersJson);
    public sealed record DeactivateStrategyRequest(string Reason);
    public sealed record UpdateParametersRequest(string ParametersJson);

    // ADR-0019 §19.8 — strategy toggle endpoints are deprecated (410 Gone).
    // Strategy activation is now controlled exclusively via
    // appsettings.json Strategies.Seed[].Activate. The response shape keeps
    // the legacy "error" field so existing telemetry/alerting wiring that
    // matched on /activate or /deactivate still sees structured feedback.
    private const string DeprecationMessage =
        "Endpoint deprecated. Strategy activation is controlled via appsettings.json Seeds[].Activate";

    public static IEndpointRouteBuilder MapStrategyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/strategies").WithTags("Strategies");

        group.MapGet("/", async (string? status, IMediator m, CancellationToken ct) =>
            (await m.Send(new ListStrategiesQuery(status), ct)).ToHttpResult())
            .WithName("ListStrategies");

        group.MapGet("/signals/latest", async (int? limit, IMediator m, CancellationToken ct) =>
            (await m.Send(new GetLatestSignalsQuery(limit ?? 12), ct)).ToHttpResult())
            .WithName("GetLatestSignals");

        group.MapGet("/{id:long}", async (long id, IMediator m, CancellationToken ct) =>
            (await m.Send(new GetStrategyDetailQuery(id), ct)).ToHttpResult())
            .WithName("GetStrategyDetail");

        group.MapGet("/{id:long}/signals", async (
                long id, DateTimeOffset? from, DateTimeOffset? to,
                IMediator m, CancellationToken ct) =>
            (await m.Send(new GetStrategySignalsQuery(id, from, to), ct)).ToHttpResult())
            .WithName("GetStrategySignals");

        group.MapPost("/", async (CreateStrategyRequest req, IMediator m, CancellationToken ct) =>
            (await m.Send(new CreateStrategyCommand(req.Name, req.Type, req.Symbols, req.ParametersJson ?? "{}"), ct))
                .ToHttpResult())
            .AddEndpointFilter<AdminAuthFilter>()
            .WithName("CreateStrategy");

        // ADR-0019 §19.8 + §19.12 — /activate + /deactivate return 410 Gone.
        // Handlers (ActivateStrategyCommand / DeactivateStrategyCommand) are
        // NOT deleted — only the route response is neutralised so the domain
        // wiring survives a future policy reversal (1 line swap back to
        // m.Send(...)). RFC 7231 §6.5.9 semantics: permanently removed.
        group.MapPost("/{id:long}/activate",
                (long id, ILoggerFactory lf) =>
                {
                    lf.CreateLogger("StrategyEndpoints").LogWarning(
                        "Deprecated endpoint called: POST /api/strategies/{StrategyId}/activate. " +
                        "Strategy activation is controlled via appsettings.json Seeds[].Activate.",
                        id);
                    return Results.Json(
                        new { error = DeprecationMessage },
                        statusCode: StatusCodes.Status410Gone);
                })
            .AddEndpointFilter<AdminAuthFilter>()
            .WithName("ActivateStrategy")
            ;

        group.MapPost("/{id:long}/deactivate",
                (long id, ILoggerFactory lf) =>
                {
                    lf.CreateLogger("StrategyEndpoints").LogWarning(
                        "Deprecated endpoint called: POST /api/strategies/{StrategyId}/deactivate. " +
                        "Strategy activation is controlled via appsettings.json Seeds[].Activate.",
                        id);
                    return Results.Json(
                        new { error = DeprecationMessage },
                        statusCode: StatusCodes.Status410Gone);
                })
            .AddEndpointFilter<AdminAuthFilter>()
            .WithName("DeactivateStrategy")
            ;

        group.MapPut("/{id:long}/parameters", async (long id, UpdateParametersRequest req, IMediator m, CancellationToken ct) =>
            (await m.Send(new UpdateStrategyParametersCommand(id, req.ParametersJson), ct)).ToHttpResult())
            .AddEndpointFilter<AdminAuthFilter>()
            .WithName("UpdateStrategyParameters");

        return app;
    }
}
