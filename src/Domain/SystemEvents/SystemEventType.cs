namespace BinanceBot.Domain.SystemEvents;

/// <summary>
/// ADR-0016 §16.9.2 — typed categories for the <c>SystemEvents</c> persistence
/// stream consumed by the UI "Sistem Olayları" tab. Numeric values are stable
/// (UI filters by int) and grouped in category bands (1x lifecycle,
/// 1x strategy, 2x ws, 3x warmup, 4x signal, 5x order, 6x position, 7x risk)
/// so new types append within a band without renumbering.
/// </summary>
public enum SystemEventType
{
    Startup = 1,
    Shutdown = 2,
    StrategyActivated = 10,
    StrategyDeactivated = 11,
    WsStateChanged = 20,
    WarmupCompleted = 30,
    SignalEmitted = 40,
    SignalSkipped = 41,
    OrderPlaced = 50,
    OrderFilled = 51,
    OrderCanceled = 52,
    PositionOpened = 60,
    PositionClosed = 61,
    RiskAlert = 70,
}
