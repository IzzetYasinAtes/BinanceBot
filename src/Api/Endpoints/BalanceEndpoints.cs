using BinanceBot.Api.Infrastructure;
using BinanceBot.Application.Balances.Commands.ResetPaperBalance;
using BinanceBot.Application.Balances.Queries.GetBalances;
using MediatR;

namespace BinanceBot.Api.Endpoints;

public static class BalanceEndpoints
{
    public sealed record ResetPaperBalanceRequest(decimal? StartingBalance);

    public static IEndpointRouteBuilder MapBalanceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/balances").WithTags("Balances");

        group.MapGet("/", async (IMediator m, CancellationToken ct) =>
            (await m.Send(new GetBalancesQuery(), ct)).ToHttpResult())
            .WithName("GetBalances");

        var paper = app.MapGroup("/api/papertrade").WithTags("PaperTrade");

        paper.MapPost("/reset", async (
                ResetPaperBalanceRequest? req,
                IMediator m,
                CancellationToken ct) =>
            (await m.Send(new ResetPaperBalanceCommand(req?.StartingBalance), ct)).ToHttpResult())
            .AddEndpointFilter<AdminAuthFilter>()
            .WithName("ResetPaperBalance");

        return app;
    }
}
