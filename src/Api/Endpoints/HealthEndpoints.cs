using BinanceBot.Application.Abstractions;
using BinanceBot.Infrastructure.Binance.Streams;
using BinanceBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BinanceBot.Api.Endpoints;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health/live", () => Results.Ok(new { status = "alive" }))
            .WithName("HealthLive")
            .WithTags("Health");

        app.MapGet("/health/ready", async (
                ApplicationDbContext db,
                IClock clock,
                BinanceWsSupervisor ws,
                CancellationToken ct) =>
            {
                var canConnect = await db.Database.CanConnectAsync(ct);
                var pending = await db.Database.GetPendingMigrationsAsync(ct);

                var ready = canConnect && !pending.Any();
                var body = new
                {
                    status = ready ? "ready" : "not-ready",
                    database = canConnect ? "up" : "down",
                    pendingMigrations = pending.ToArray(),
                    wsState = ws.State.ToString(),
                    clockDriftMs = clock.DriftMs,
                };

                return ready
                    ? Results.Ok(body)
                    : Results.Json(body, statusCode: StatusCodes.Status503ServiceUnavailable);
            })
            .WithName("HealthReady")
            .WithTags("Health");

        return app;
    }
}
