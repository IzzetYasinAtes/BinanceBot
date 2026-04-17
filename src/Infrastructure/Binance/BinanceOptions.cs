using System.ComponentModel.DataAnnotations;

namespace BinanceBot.Infrastructure.Binance;

public sealed class BinanceOptions
{
    public const string SectionName = "Binance";

    [Required]
    [Url]
    public string RestBaseUrl { get; init; } = "https://testnet.binance.vision";

    [Required]
    [RegularExpression(@"^wss?://.+", ErrorMessage = "WsBaseUrl must start with ws:// or wss://")]
    public string WsBaseUrl { get; init; } = "wss://stream.testnet.binance.vision";

    [Required]
    [MinLength(1)]
    public string[] Symbols { get; init; } = [];

    [Required]
    [MinLength(1)]
    public string[] KlineIntervals { get; init; } = [];

    [Range(1, 5000)]
    public int DepthSnapshotLimit { get; init; } = 1000;

    [Range(1000, 60000)]
    public int RestTimeoutMs { get; init; } = 10000;

    [Range(1, 30000)]
    public int WsReconnectInitialDelayMs { get; init; } = 500;

    [Range(1000, 60000)]
    public int WsReconnectMaxDelayMs { get; init; } = 30000;

    [Range(5000, 120000)]
    public int WsPingIntervalMs { get; init; } = 20000;

    [Range(5000, 300000)]
    public int WsPongTimeoutMs { get; init; } = 60000;

    public string? ApiKey { get; init; }
    public string? ApiSecret { get; init; }

    public bool AllowMainnet { get; init; }
}
