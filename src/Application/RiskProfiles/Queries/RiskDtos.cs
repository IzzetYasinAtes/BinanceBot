namespace BinanceBot.Application.RiskProfiles.Queries;

public sealed record RiskProfileDto(
    decimal RiskPerTradePct,
    decimal MaxPositionSizePct,
    decimal MaxDrawdown24hPct,
    decimal MaxDrawdownAllTimePct,
    int MaxConsecutiveLosses,
    int MaxOpenPositions,
    decimal? RiskPerTradeCap,
    decimal? MaxPositionCap,
    string? CapsAdminNote,
    int ConsecutiveLosses,
    decimal CurrentDrawdownPct,
    decimal RealizedPnl24h,
    decimal RealizedPnlAllTime,
    decimal PeakEquity,
    DateTimeOffset UpdatedAt);

public sealed record CircuitBreakerStatusDto(
    string Status,
    string? Reason,
    DateTimeOffset? TrippedAt,
    decimal CurrentDrawdownPct);

public sealed record DrawdownPointDto(
    DateTimeOffset AtUtc,
    decimal Equity,
    decimal DrawdownPct);
