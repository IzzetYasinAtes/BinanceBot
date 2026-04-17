using System.Security.Cryptography;
using System.Text;

namespace BinanceBot.Infrastructure.Binance.Handlers;

public static class BinanceSignatureHelper
{
    public static string Sign(string queryString, string apiSecret)
    {
        var keyBytes = Encoding.UTF8.GetBytes(apiSecret);
        var payloadBytes = Encoding.UTF8.GetBytes(queryString);
        using var hmac = new HMACSHA256(keyBytes);
        var hash = hmac.ComputeHash(payloadBytes);
        return Convert.ToHexStringLower(hash);
    }
}
