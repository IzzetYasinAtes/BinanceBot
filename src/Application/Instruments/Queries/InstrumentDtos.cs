namespace BinanceBot.Application.Instruments.Queries;

public sealed record InstrumentDto(
    string Symbol,
    string BaseAsset,
    string QuoteAsset,
    string Status,
    decimal TickSize,
    decimal StepSize,
    decimal MinNotional,
    decimal MinQty,
    decimal MaxQty,
    DateTimeOffset LastSyncedAt);

public sealed record SymbolFiltersDto(
    string Symbol,
    decimal TickSize,
    decimal StepSize,
    decimal MinNotional,
    decimal MinQty,
    decimal MaxQty,
    string Status);
