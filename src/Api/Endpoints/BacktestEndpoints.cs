using BinanceBot.Api.Infrastructure;
using BinanceBot.Application.BacktestRuns.Commands.StartBacktest;
using BinanceBot.Application.BacktestRuns.Queries.GetBacktestResult;
using BinanceBot.Application.BacktestRuns.Queries.ListBacktestRuns;
using MediatR;

namespace BinanceBot.Api.Endpoints;

public static class BacktestEndpoints
{
    public sealed record StartBacktestRequest(
        long StrategyId,
        DateTimeOffset FromUtc,
        DateTimeOffset ToUtc,
        decimal InitialBalance);

    public static IEndpointRouteBuilder MapBacktestEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/backtests").WithTags("Backtests");

        group.MapGet("/", async (
                long? strategyId, string? status, int? skip, int? take,
                IMediator m, CancellationToken ct) =>
            (await m.Send(new ListBacktestRunsQuery(strategyId, status, skip ?? 0, take ?? 50), ct))
                .ToHttpResult())
            .WithName("ListBacktestRuns");

        group.MapGet("/{id:long}", async (long id, IMediator m, CancellationToken ct) =>
            (await m.Send(new GetBacktestResultQuery(id), ct)).ToHttpResult())
            .WithName("GetBacktestResult");

        group.MapPost("/", async (StartBacktestRequest req, IMediator m, CancellationToken ct) =>
            (await m.Send(new StartBacktestCommand(
                req.StrategyId, req.FromUtc, req.ToUtc, req.InitialBalance), ct))
                .ToHttpResult())
            .AddEndpointFilter<AdminAuthFilter>()
            .WithName("StartBacktest");

        return app;
    }
}
