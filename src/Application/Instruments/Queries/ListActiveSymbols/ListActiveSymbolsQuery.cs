using Ardalis.Result;
using BinanceBot.Application.Abstractions;
using BinanceBot.Application.Instruments.Queries;
using BinanceBot.Domain.Instruments;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BinanceBot.Application.Instruments.Queries.ListActiveSymbols;

public sealed record ListActiveSymbolsQuery() : IRequest<Result<IReadOnlyList<InstrumentDto>>>;

public sealed class ListActiveSymbolsQueryHandler
    : IRequestHandler<ListActiveSymbolsQuery, Result<IReadOnlyList<InstrumentDto>>>
{
    private readonly IApplicationDbContext _db;

    public ListActiveSymbolsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<Result<IReadOnlyList<InstrumentDto>>> Handle(
        ListActiveSymbolsQuery request, CancellationToken ct)
    {
        var rows = await _db.Instruments
            .AsNoTracking()
            .Where(i => i.Status == InstrumentStatus.Trading)
            .OrderBy(i => i.Symbol)
            .ToListAsync(ct);

        var items = rows.Select(i => new InstrumentDto(
            i.Symbol.Value,
            i.BaseAsset,
            i.QuoteAsset,
            i.Status.ToString(),
            i.TickSize,
            i.StepSize,
            i.MinNotional,
            i.MinQty,
            i.MaxQty,
            i.LastSyncedAt)).ToList();

        return Result.Success<IReadOnlyList<InstrumentDto>>(items);
    }
}
