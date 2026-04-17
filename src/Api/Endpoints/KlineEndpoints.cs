using BinanceBot.Api.Infrastructure;
using BinanceBot.Application.MarketData.Queries.GetKlines;
using MediatR;

namespace BinanceBot.Api.Endpoints;

public static class KlineEndpoints
{
    public static IEndpointRouteBuilder MapKlineEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/klines", async (
                string symbol,
                string interval,
                int? limit,
                DateTimeOffset? startTime,
                DateTimeOffset? endTime,
                IMediator mediator,
                CancellationToken ct) =>
            {
                var query = new GetKlinesQuery(symbol, interval, limit ?? 500, startTime, endTime);
                var result = await mediator.Send(query, ct);
                return result.ToHttpResult();
            })
            .WithName("GetKlines")
            .WithTags("MarketData");

        return app;
    }
}
