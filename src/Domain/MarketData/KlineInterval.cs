namespace BinanceBot.Domain.MarketData;

public enum KlineInterval
{
    OneMinute = 1,
    ThreeMinutes = 3,
    FiveMinutes = 5,
    FifteenMinutes = 15,
    ThirtyMinutes = 30,
    OneHour = 60,
    FourHours = 240,
    OneDay = 1440,
}

public static class KlineIntervalExtensions
{
    public static string ToBinanceCode(this KlineInterval interval) => interval switch
    {
        KlineInterval.OneMinute => "1m",
        KlineInterval.ThreeMinutes => "3m",
        KlineInterval.FiveMinutes => "5m",
        KlineInterval.FifteenMinutes => "15m",
        KlineInterval.ThirtyMinutes => "30m",
        KlineInterval.OneHour => "1h",
        KlineInterval.FourHours => "4h",
        KlineInterval.OneDay => "1d",
        _ => throw new ArgumentOutOfRangeException(nameof(interval), interval, null),
    };

    public static KlineInterval FromBinanceCode(string code) => code switch
    {
        "1m" => KlineInterval.OneMinute,
        "3m" => KlineInterval.ThreeMinutes,
        "5m" => KlineInterval.FiveMinutes,
        "15m" => KlineInterval.FifteenMinutes,
        "30m" => KlineInterval.ThirtyMinutes,
        "1h" => KlineInterval.OneHour,
        "4h" => KlineInterval.FourHours,
        "1d" => KlineInterval.OneDay,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown Binance interval."),
    };
}
