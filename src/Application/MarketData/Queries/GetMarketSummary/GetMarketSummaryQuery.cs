using Ardalis.Result;
using BinanceBot.Application.Abstractions;
using BinanceBot.Application.Abstractions.Binance;
using BinanceBot.Application.MarketData.Queries;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BinanceBot.Application.MarketData.Queries.GetMarketSummary;

public sealed record GetMarketSummaryQuery(IReadOnlyList<string> Symbols)
    : IRequest<Result<IReadOnlyList<MarketSummaryDto>>>;

public sealed class GetMarketSummaryQueryValidator : AbstractValidator<GetMarketSummaryQuery>
{
    public GetMarketSummaryQueryValidator()
    {
        RuleFor(q => q.Symbols).NotEmpty().Must(s => s.Count <= 10);
    }
}

/// <summary>
/// Returns a per-symbol market summary for UI tiles (ADR-0012 §12.1).
///
/// Source-of-truth split:
///  - 24h rolling window (last price / change% / quote volume / closeTime) → Binance REST
///    <c>/api/v3/ticker/24hr</c> via <see cref="IBinanceMarketData.GetTicker24hAsync"/>.
///    The previous implementation derived these from local 1m kline rows, which silently
///    underflowed because <c>BackfillLimit=1000</c> ≈ 16h40m of history (no row reachable
///    24h back, so changePct collapsed to 0 — UI showed the lie "+0.00%").
///  - Mark price (mid of best bid/ask) → local <c>BookTickers</c> table populated by the
///    WS bookTicker stream. REST <c>lastPrice</c> is the fallback when the WS row hasn't
///    landed yet (boot-up or stream gap).
/// </summary>
public sealed class GetMarketSummaryQueryHandler
    : IRequestHandler<GetMarketSummaryQuery, Result<IReadOnlyList<MarketSummaryDto>>>
{
    private readonly IApplicationDbContext _db;
    private readonly IBinanceMarketData _binance;

    public GetMarketSummaryQueryHandler(IApplicationDbContext db, IBinanceMarketData binance)
    {
        _db = db;
        _binance = binance;
    }

    public async Task<Result<IReadOnlyList<MarketSummaryDto>>> Handle(
        GetMarketSummaryQuery request, CancellationToken ct)
    {
        var ticker24h = await _binance.GetTicker24hAsync(request.Symbols, ct);
        if (ticker24h.Count == 0)
        {
            return Result.Success<IReadOnlyList<MarketSummaryDto>>(Array.Empty<MarketSummaryDto>());
        }

        var symbolFilter = request.Symbols
            .Select(s => s.ToUpperInvariant())
            .ToHashSet(StringComparer.Ordinal);

        // EF Core cannot translate `b.Symbol.Value` (HasConversion) inside a Where predicate;
        // pull all rows then filter in memory. The table holds at most 1 row per active symbol
        // (≤10 by validator), so the materialisation is cheap.
        var bookTickers = await _db.BookTickers.AsNoTracking().ToListAsync(ct);
        var bookByName = bookTickers
            .Where(b => symbolFilter.Contains(b.Symbol.Value))
            .ToDictionary(b => b.Symbol.Value, b => b, StringComparer.Ordinal);

        var results = new List<MarketSummaryDto>(ticker24h.Count);
        foreach (var t in ticker24h)
        {
            var markPrice = bookByName.TryGetValue(t.Symbol, out var bt)
                ? (bt.BidPrice + bt.AskPrice) / 2m
                : t.LastPrice;

            results.Add(new MarketSummaryDto(
                t.Symbol,
                t.LastPrice,
                markPrice,
                t.PriceChangePct,
                t.QuoteVolume,
                t.CloseTime));
        }

        return Result.Success<IReadOnlyList<MarketSummaryDto>>(results);
    }
}
