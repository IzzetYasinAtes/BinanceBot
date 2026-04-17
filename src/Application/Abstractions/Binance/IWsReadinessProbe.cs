namespace BinanceBot.Application.Abstractions.Binance;

/// <summary>
/// Signals whether the Binance market data WebSocket pipeline is ready to receive
/// (channel allocated and at least one successful WS connect completed).
/// Backfill or other consumers can poll this to avoid colliding with the WS
/// stream during startup.
/// </summary>
public interface IWsReadinessProbe
{
    bool IsReady { get; }
}
