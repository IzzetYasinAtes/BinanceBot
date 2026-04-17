using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace BinanceBot.Api.Infrastructure;

public sealed class AdminAuthFilter : IEndpointFilter
{
    public const string HeaderName = "X-Admin-Key";
    private const string ConfigSection = "Admin";
    private const string KeyPath = "Key";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var config = ctx.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        var expected = config.GetSection(ConfigSection)[KeyPath];
        if (string.IsNullOrWhiteSpace(expected))
        {
            return Results.Problem(
                title: "Admin key not configured",
                detail: "Server must set Admin:Key via user-secrets or env (ADR-0007).",
                statusCode: StatusCodes.Status500InternalServerError);
        }

        if (!ctx.HttpContext.Request.Headers.TryGetValue(HeaderName, out var provided)
            || !FixedTimeEquals(provided.ToString(), expected))
        {
            return Results.Unauthorized();
        }

        return await next(ctx);
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        if (a.Length != b.Length) return false;
        var diff = 0;
        for (var i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
        return diff == 0;
    }
}
