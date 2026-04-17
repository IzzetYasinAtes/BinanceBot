namespace BinanceBot.Infrastructure.Binance.Streams;

public enum WsSupervisorState
{
    Disconnected = 0,
    Connecting = 1,
    Connected = 2,
    Subscribing = 3,
    Streaming = 4,
    Reconnecting = 5,
    Faulted = 6,
}
