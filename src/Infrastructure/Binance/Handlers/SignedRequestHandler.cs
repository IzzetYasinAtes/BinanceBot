using System.Collections.Specialized;
using System.Text;
using System.Web;
using Microsoft.Extensions.Options;

namespace BinanceBot.Infrastructure.Binance.Handlers;

/// <summary>
/// Binance signed endpoint'leri icin HMAC-SHA256 imza ekler.
/// Yalnizca `X-Sign: true` header'ini iceren istekler icin devreye girer.
/// </summary>
public sealed class SignedRequestHandler : DelegatingHandler
{
    public const string SignMarker = "X-Sign";
    private const int DefaultRecvWindow = 5000;

    private readonly IOptionsMonitor<BinanceOptions> _options;

    public SignedRequestHandler(IOptionsMonitor<BinanceOptions> options)
    {
        _options = options;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (!request.Headers.Contains(SignMarker))
        {
            return await base.SendAsync(request, cancellationToken);
        }
        request.Headers.Remove(SignMarker);

        var opts = _options.CurrentValue;
        if (string.IsNullOrWhiteSpace(opts.ApiKey) || string.IsNullOrWhiteSpace(opts.ApiSecret))
        {
            throw new InvalidOperationException("Binance ApiKey/ApiSecret missing (signed endpoint).");
        }

        NameValueCollection query;
        HttpContent? originalContent = null;

        if (request.Method == HttpMethod.Get || request.Method == HttpMethod.Delete)
        {
            var uri = request.RequestUri!;
            query = HttpUtility.ParseQueryString(uri.Query);
        }
        else
        {
            originalContent = request.Content;
            var body = originalContent is null ? string.Empty : await originalContent.ReadAsStringAsync(cancellationToken);
            query = HttpUtility.ParseQueryString(body);
        }

        query["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        if (query["recvWindow"] is null)
        {
            query["recvWindow"] = DefaultRecvWindow.ToString();
        }

        var unsignedQuery = query.ToString() ?? string.Empty;
        var signature = BinanceSignatureHelper.Sign(unsignedQuery, opts.ApiSecret);
        var signedQuery = unsignedQuery + "&signature=" + signature;

        if (request.Method == HttpMethod.Get || request.Method == HttpMethod.Delete)
        {
            var builder = new UriBuilder(request.RequestUri!) { Query = signedQuery };
            request.RequestUri = builder.Uri;
        }
        else
        {
            request.Content = new StringContent(
                signedQuery, Encoding.UTF8, "application/x-www-form-urlencoded");
            originalContent?.Dispose();
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
