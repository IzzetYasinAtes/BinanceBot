namespace BinanceBot.Application.System.Queries;

public sealed record SystemStatusDto(
    bool TestnetOnly,
    string WsState,
    long ClockDriftMs,
    bool DatabaseUp,
    int PendingMigrationsCount,
    string[] PendingMigrations,
    DateTimeOffset AsOfUtc);

public sealed record SystemEventTailDto(
    long Cursor,
    IReadOnlyList<SystemEventRowDto> Items);

public sealed record SystemEventRowDto(
    long Id,
    string EventType,
    string Severity,
    string Source,
    Guid? CorrelationId,
    DateTimeOffset OccurredAt,
    string PayloadJson);
