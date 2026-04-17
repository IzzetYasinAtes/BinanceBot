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

    /// <summary>
    /// REST kline backfill toggle (run once on host start). Defaults to true so
    /// charts are populated even when the WS supplies only live deltas.
    /// </summary>
    public bool BackfillEnabled { get; init; } = true;

    /// <summary>
    /// Bars per symbol/interval to request from /api/v3/klines. Binance hard cap is 1000.
    /// </summary>
    [Range(1, 1000)]
    public int BackfillLimit { get; init; } = 1000;

    /// <summary>
    /// Intervals to backfill on boot. Independent from <see cref="KlineIntervals"/>
    /// (the WS subscription set) so we can backfill a coarser/finer set if needed.
    /// </summary>
    [MinLength(1)]
    public string[] BackfillIntervals { get; init; } = ["1m"];
}
