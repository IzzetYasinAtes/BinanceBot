using BinanceBot.Application.Abstractions;

namespace BinanceBot.Infrastructure.Time;

public sealed class BinanceClock : IClock
{
    private long _serverTimeMs;
    private long _driftMs;

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public long BinanceServerTimeMs =>
        _serverTimeMs == 0 ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() : _serverTimeMs;

    public long DriftMs => _driftMs;

    public void SetFromServer(long serverTimeMs)
    {
        var localMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Interlocked.Exchange(ref _serverTimeMs, serverTimeMs);
        Interlocked.Exchange(ref _driftMs, localMs - serverTimeMs);
    }
}
