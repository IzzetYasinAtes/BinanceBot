using BinanceBot.Api.Infrastructure;
using BinanceBot.Application.MarketData.Queries.GetBookTicker;
using BinanceBot.Application.MarketData.Queries.GetDepthSnapshot;
using BinanceBot.Application.MarketData.Queries.GetMarketSummary;
using MediatR;

namespace BinanceBot.Api.Endpoints;

public static class MarketEndpoints
{
    public static IEndpointRouteBuilder MapMarketEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/ticker/book", async (string symbol, IMediator m, CancellationToken ct) =>
            (await m.Send(new GetBookTickerQuery(symbol), ct)).ToHttpResult())
            .WithName("GetBookTicker")
            .WithTags("MarketData");

        app.MapGet("/api/depth", async (string symbol, int? depth, IMediator m, CancellationToken ct) =>
            (await m.Send(new GetDepthSnapshotQuery(symbol, depth ?? 20), ct)).ToHttpResult())
            .WithName("GetDepthSnapshot")
            .WithTags("MarketData");

        app.MapGet("/api/market/summary", async (string symbols, IMediator m, CancellationToken ct) =>
        {
            var parsed = symbols
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToArray();
            return (await m.Send(new GetMarketSummaryQuery(parsed), ct)).ToHttpResult();
        })
            .WithName("GetMarketSummary")
            .WithTags("MarketData");

        return app;
    }
}
