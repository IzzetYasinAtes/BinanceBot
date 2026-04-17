namespace BinanceBot.Domain.Common;

/// <summary>
/// Platform-wide execution channel for orders/positions.
/// Integer backing intentionally matches singleton Id's in:
///   - VirtualBalance (Id == (int)Mode)
///   - RiskProfile    (Id == (int)Mode)
/// See ADR-0008 for the 3-way fan-out contract.
/// </summary>
public enum TradingMode
{
    Paper = 1,
    LiveTestnet = 2,
    LiveMainnet = 3,
}

public static class TradingModeExtensions
{
    /// <summary>
    /// Suffix for ClientOrderId fan-out: sig-{StrategyId}-{BarOpenUnix}-{suffix}.
    /// Kept short to respect Binance 36 char cid limit.
    /// </summary>
    public static string ToCidSuffix(this TradingMode mode) => mode switch
    {
        TradingMode.Paper => "p",
        TradingMode.LiveTestnet => "lt",
        TradingMode.LiveMainnet => "lm",
        _ => throw new DomainException($"Unknown TradingMode: {mode}"),
    };
}
