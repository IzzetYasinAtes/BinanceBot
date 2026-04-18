using BinanceBot.Api.Infrastructure;
using BinanceBot.Application.RiskProfiles.Commands.OverrideRiskCaps;
using BinanceBot.Application.RiskProfiles.Commands.ResetCircuitBreaker;
using BinanceBot.Application.RiskProfiles.Commands.UpdateRiskProfile;
using BinanceBot.Application.RiskProfiles.Queries.GetCircuitBreakerStatus;
using BinanceBot.Application.RiskProfiles.Queries.GetDrawdownHistory;
using BinanceBot.Application.RiskProfiles.Queries.GetRiskProfile;
using MediatR;

namespace BinanceBot.Api.Endpoints;

public static class RiskEndpoints
{
    public sealed record UpdateRiskProfileRequest(
        decimal RiskPerTradePct,
        decimal MaxPositionSizePct,
        decimal MaxDrawdown24hPct,
        decimal MaxDrawdownAllTimePct,
        int MaxConsecutiveLosses,
        int MaxOpenPositions);

    public sealed record OverrideCapsRequest(
        decimal RiskPerTradeCap,
        decimal MaxPositionCap,
        string AdminNote);

    public sealed record ResetCbRequest(string AdminNote);

    public static IEndpointRouteBuilder MapRiskEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/risk").WithTags("Risk");

        group.MapGet("/profile", async (IMediator m, CancellationToken ct) =>
            (await m.Send(new GetRiskProfileQuery(), ct)).ToHttpResult())
            .WithName("GetRiskProfile");

        group.MapGet("/circuit-breaker", async (IMediator m, CancellationToken ct) =>
            (await m.Send(new GetCircuitBreakerStatusQuery(), ct)).ToHttpResult())
            .WithName("GetCircuitBreakerStatus");

        group.MapGet("/drawdown-history", async (int? days, IMediator m, CancellationToken ct) =>
            (await m.Send(new GetDrawdownHistoryQuery(days ?? 30), ct)).ToHttpResult())
            .WithName("GetDrawdownHistory");

        group.MapPut("/profile", async (UpdateRiskProfileRequest req, IMediator m, CancellationToken ct) =>
            (await m.Send(new UpdateRiskProfileCommand(
                req.RiskPerTradePct, req.MaxPositionSizePct,
                req.MaxDrawdown24hPct, req.MaxDrawdownAllTimePct,
                req.MaxConsecutiveLosses, req.MaxOpenPositions), ct))
                .ToHttpResult())
            .AddEndpointFilter<AdminAuthFilter>()
            .WithName("UpdateRiskProfile");

        group.MapPost("/override-caps", async (OverrideCapsRequest req, IMediator m, CancellationToken ct) =>
            (await m.Send(new OverrideRiskCapsCommand(
                req.RiskPerTradeCap, req.MaxPositionCap, req.AdminNote), ct))
                .ToHttpResult())
            .AddEndpointFilter<AdminAuthFilter>()
            .WithName("OverrideRiskCaps");

        group.MapPost("/circuit-breaker/reset", async (ResetCbRequest req, IMediator m, CancellationToken ct) =>
            (await m.Send(new ResetCircuitBreakerCommand(req.AdminNote), ct))
                .ToHttpResult())
            .AddEndpointFilter<AdminAuthFilter>()
            .WithName("ResetCircuitBreaker");

        return app;
    }
}
