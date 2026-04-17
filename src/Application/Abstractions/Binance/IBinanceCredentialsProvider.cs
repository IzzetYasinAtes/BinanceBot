namespace BinanceBot.Application.Abstractions.Binance;

/// <summary>
/// Binance API key availability probe — ADR-0008 §8.8.
/// PlaceOrderCommand uses it to route LiveTestnet to "no_credentials_testnet"
/// reject instead of a real HTTP call when keys are not provisioned.
/// </summary>
public interface IBinanceCredentialsProvider
{
    bool HasTestnetCredentials();
    bool HasMainnetCredentials();
}
