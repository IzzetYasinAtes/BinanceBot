namespace BinanceBot.Application.Common;

public sealed record PagedList<T>(
    IReadOnlyList<T> Items,
    long TotalCount,
    int Skip,
    int Take);
