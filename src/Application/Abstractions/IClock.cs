namespace BinanceBot.Application.Abstractions;

public interface IClock
{
    DateTimeOffset UtcNow { get; }

    long BinanceServerTimeMs { get; }

    long DriftMs { get; }
}
