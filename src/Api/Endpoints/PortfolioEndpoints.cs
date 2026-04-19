using BinanceBot.Api.Infrastructure;
using BinanceBot.Application.Portfolio.Queries.GetPortfolioSummary;
using BinanceBot.Domain.Common;
using MediatR;

namespace BinanceBot.Api.Endpoints;

/// <summary>
/// Loop 19 — single-shot portfolio metrics endpoint. The dashboard previously
/// fanned out to /api/balances + /api/positions/pnl/today + /api/positions and
/// reconciled three asynchronous reads on the client; that reconciliation
/// produced the misleading "Mevcut Bakiye $316" display when VirtualBalance.Equity
/// raced unrealized PnL writes. A single query enforces a consistent snapshot.
/// </summary>
public static class PortfolioEndpoints
{
    public static IEndpointRouteBuilder MapPortfolioEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio").WithTags("Portfolio");

        group.MapGet("/summary", async (
                TradingMode? mode,
                IMediator m,
                CancellationToken ct) =>
            (await m.Send(new GetPortfolioSummaryQuery(mode ?? TradingMode.Paper), ct))
                .ToHttpResult())
            .WithName("GetPortfolioSummary");

        return app;
    }
}
