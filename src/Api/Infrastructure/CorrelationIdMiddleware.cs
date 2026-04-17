using Serilog.Context;

namespace BinanceBot.Api.Infrastructure;

public sealed class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-Id";
    public const string ItemKey = "CorrelationId";

    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task Invoke(HttpContext context)
    {
        var correlationId = context.Request.Headers.TryGetValue(HeaderName, out var headerValue)
            && Guid.TryParse(headerValue.ToString(), out var parsed)
                ? parsed
                : Guid.NewGuid();

        context.Items[ItemKey] = correlationId;
        context.Response.Headers[HeaderName] = correlationId.ToString();

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await _next(context);
        }
    }
}

public sealed class CorrelationIdAccessor : BinanceBot.Application.Abstractions.ICorrelationIdAccessor
{
    private readonly IHttpContextAccessor _accessor;

    public CorrelationIdAccessor(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }

    public Guid CorrelationId
    {
        get
        {
            var ctx = _accessor.HttpContext;
            if (ctx?.Items[CorrelationIdMiddleware.ItemKey] is Guid id)
            {
                return id;
            }
            return Guid.Empty;
        }
    }
}
