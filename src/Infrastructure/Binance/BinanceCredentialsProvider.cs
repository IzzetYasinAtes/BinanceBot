using BinanceBot.Application.Abstractions.Binance;
using Microsoft.Extensions.Options;

namespace BinanceBot.Infrastructure.Binance;

public sealed class BinanceCredentialsProvider : IBinanceCredentialsProvider
{
    private readonly IOptionsMonitor<BinanceOptions> _options;

    public BinanceCredentialsProvider(IOptionsMonitor<BinanceOptions> options)
    {
        _options = options;
    }

    public bool HasTestnetCredentials()
    {
        var opts = _options.CurrentValue;
        return !string.IsNullOrWhiteSpace(opts.ApiKey)
            && !string.IsNullOrWhiteSpace(opts.ApiSecret);
    }

    // ADR-0006: mainnet stays blocked at config level (AllowMainnet is false in all ship configs).
    public bool HasMainnetCredentials()
    {
        var opts = _options.CurrentValue;
        return opts.AllowMainnet
            && !string.IsNullOrWhiteSpace(opts.ApiKey)
            && !string.IsNullOrWhiteSpace(opts.ApiSecret);
    }
}
