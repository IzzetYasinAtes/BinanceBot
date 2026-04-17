using Microsoft.Extensions.Options;

namespace BinanceBot.Infrastructure.Binance.Handlers;

public sealed class ApiKeyHandler : DelegatingHandler
{
    private readonly IOptionsMonitor<BinanceOptions> _options;

    public ApiKeyHandler(IOptionsMonitor<BinanceOptions> options)
    {
        _options = options;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var apiKey = _options.CurrentValue.ApiKey;
        if (!string.IsNullOrWhiteSpace(apiKey)
            && !request.Headers.Contains("X-MBX-APIKEY"))
        {
            request.Headers.Add("X-MBX-APIKEY", apiKey);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
