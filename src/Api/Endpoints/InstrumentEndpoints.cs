using BinanceBot.Api.Infrastructure;
using BinanceBot.Application.Instruments.Commands.HaltSymbol;
using BinanceBot.Application.Instruments.Commands.RefreshSymbolFilters;
using BinanceBot.Application.Instruments.Queries.GetSymbolFilters;
using BinanceBot.Application.Instruments.Queries.ListActiveSymbols;
using MediatR;

namespace BinanceBot.Api.Endpoints;

public static class InstrumentEndpoints
{
    public sealed record RefreshFiltersRequest(string[]? Symbols);
    public sealed record HaltSymbolRequest(string Reason);

    public static IEndpointRouteBuilder MapInstrumentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/instruments").WithTags("Instruments");

        group.MapGet("/", async (IMediator m, CancellationToken ct) =>
            (await m.Send(new ListActiveSymbolsQuery(), ct)).ToHttpResult())
            .WithName("ListActiveSymbols");

        group.MapGet("/{symbol}/filters", async (string symbol, IMediator m, CancellationToken ct) =>
            (await m.Send(new GetSymbolFiltersQuery(symbol), ct)).ToHttpResult())
            .WithName("GetSymbolFilters");

        group.MapPost("/refresh", async (RefreshFiltersRequest? req, IMediator m, CancellationToken ct) =>
            (await m.Send(new RefreshSymbolFiltersCommand(req?.Symbols), ct)).ToHttpResult())
            .AddEndpointFilter<AdminAuthFilter>()
            .WithName("RefreshSymbolFilters");

        group.MapPost("/{symbol}/halt", async (string symbol, HaltSymbolRequest req, IMediator m, CancellationToken ct) =>
            (await m.Send(new HaltSymbolCommand(symbol, req.Reason), ct)).ToHttpResult())
            .AddEndpointFilter<AdminAuthFilter>()
            .WithName("HaltSymbol");

        return app;
    }
}
