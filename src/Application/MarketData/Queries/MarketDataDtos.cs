namespace BinanceBot.Application.MarketData.Queries;

public sealed record BookTickerDto(
    string Symbol,
    decimal BidPrice,
    decimal BidQuantity,
    decimal AskPrice,
    decimal AskQuantity,
    decimal MidPrice,
    decimal SpreadBps,
    DateTimeOffset UpdatedAt);

public sealed record DepthSnapshotDto(
    string Symbol,
    long LastUpdateId,
    IReadOnlyList<DepthLevelDto> Bids,
    IReadOnlyList<DepthLevelDto> Asks,
    DateTimeOffset CapturedAt);

public sealed record DepthLevelDto(decimal Price, decimal Quantity);

public sealed record MarketSummaryDto(
    string Symbol,
    decimal LastPrice,
    decimal MarkPrice,
    decimal Change24hPct,
    decimal Volume24hQuote,
    DateTimeOffset AsOfUtc);
