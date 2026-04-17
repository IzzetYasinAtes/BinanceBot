using BinanceBot.Api.Infrastructure;
using BinanceBot.Application.Strategies.Commands.ActivateStrategy;
using BinanceBot.Application.Strategies.Commands.CreateStrategy;
using BinanceBot.Application.Strategies.Commands.DeactivateStrategy;
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

        group.MapPost("/{id:long}/activate", async (long id, IMediator m, CancellationToken ct) =>
            (await m.Send(new ActivateStrategyCommand(id), ct)).ToHttpResult())
            .AddEndpointFilter<AdminAuthFilter>()
            .WithName("ActivateStrategy");

        group.MapPost("/{id:long}/deactivate", async (long id, DeactivateStrategyRequest req, IMediator m, CancellationToken ct) =>
            (await m.Send(new DeactivateStrategyCommand(id, req.Reason), ct)).ToHttpResult())
            .AddEndpointFilter<AdminAuthFilter>()
            .WithName("DeactivateStrategy");

        group.MapPut("/{id:long}/parameters", async (long id, UpdateParametersRequest req, IMediator m, CancellationToken ct) =>
            (await m.Send(new UpdateStrategyParametersCommand(id, req.ParametersJson), ct)).ToHttpResult())
            .AddEndpointFilter<AdminAuthFilter>()
            .WithName("UpdateStrategyParameters");

        return app;
    }
}
