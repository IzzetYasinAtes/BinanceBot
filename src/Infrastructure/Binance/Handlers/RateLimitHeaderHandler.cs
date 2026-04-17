using Microsoft.Extensions.Logging;

namespace BinanceBot.Infrastructure.Binance.Handlers;

public sealed class RateLimitHeaderHandler : DelegatingHandler
{
    private const string WeightHeader = "X-MBX-USED-WEIGHT-1M";
    private const string OrderCountHeader = "X-MBX-ORDER-COUNT-1M";

    private readonly ILogger<RateLimitHeaderHandler> _logger;

    public RateLimitHeaderHandler(ILogger<RateLimitHeaderHandler> logger)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);

        if (response.Headers.TryGetValues(WeightHeader, out var weights))
        {
            var weight = weights.FirstOrDefault();
            if (int.TryParse(weight, out var w))
            {
                if (w > 1000)
                {
                    _logger.LogWarning("Binance request weight high: {Weight}/1200 (1m)", w);
                }
                else
                {
                    _logger.LogDebug("Binance weight usage: {Weight}/1200 (1m)", w);
                }
            }
        }

        if (response.Headers.TryGetValues(OrderCountHeader, out var orders))
        {
            var orderCount = orders.FirstOrDefault();
            if (int.TryParse(orderCount, out var o))
            {
                _logger.LogDebug("Binance order count: {Count}/100 (1m)", o);
            }
        }

        if ((int)response.StatusCode == 429 || (int)response.StatusCode == 418)
        {
            _logger.LogError("Binance rate limit hit: {Status} on {Path}",
                response.StatusCode, request.RequestUri?.AbsolutePath);
        }

        return response;
    }
}
