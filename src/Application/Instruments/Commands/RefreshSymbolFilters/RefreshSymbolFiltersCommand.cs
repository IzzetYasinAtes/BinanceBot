using Ardalis.Result;
using BinanceBot.Application.Abstractions;
using BinanceBot.Application.Abstractions.Binance;
using BinanceBot.Domain.Common;
using BinanceBot.Domain.Instruments;
using BinanceBot.Domain.ValueObjects;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BinanceBot.Application.Instruments.Commands.RefreshSymbolFilters;

public sealed record RefreshSymbolFiltersCommand(IReadOnlyList<string>? Symbols) : IRequest<Result<int>>;

public sealed class RefreshSymbolFiltersCommandHandler
    : IRequestHandler<RefreshSymbolFiltersCommand, Result<int>>
{
    private readonly IApplicationDbContext _db;
    private readonly IBinanceMarketData _market;
    private readonly IClock _clock;

    public RefreshSymbolFiltersCommandHandler(
        IApplicationDbContext db,
        IBinanceMarketData market,
        IClock clock)
    {
        _db = db;
        _market = market;
        _clock = clock;
    }

    public async Task<Result<int>> Handle(RefreshSymbolFiltersCommand request, CancellationToken ct)
    {
        var symbols = request.Symbols is { Count: > 0 }
            ? request.Symbols
            : await _db.Instruments.AsNoTracking().Select(i => i.Symbol.Value).ToListAsync(ct);

        if (symbols.Count == 0)
        {
            return Result.Success(0);
        }

        var dtos = await _market.GetExchangeInfoAsync(symbols, ct);
        var now = _clock.UtcNow;
        var affected = 0;

        foreach (var dto in dtos)
        {
            var vo = Symbol.From(dto.Symbol);
            var existing = await _db.Instruments.FirstOrDefaultAsync(i => i.Symbol == vo, ct);
            var status = MapStatus(dto.Status);

            try
            {
                if (existing is null)
                {
                    var created = Instrument.Create(vo, dto.BaseAsset, dto.QuoteAsset, status,
                        dto.TickSize, dto.StepSize, dto.MinNotional, dto.MinQty, dto.MaxQty, now);
                    _db.Instruments.Add(created);
                    affected++;
                }
                else
                {
                    existing.UpdateFilters(dto.TickSize, dto.StepSize, dto.MinNotional,
                        dto.MinQty, dto.MaxQty, status, now);
                    affected++;
                }
            }
            catch (DomainException ex)
            {
                return Result<int>.Error(ex.Message);
            }
        }

        await _db.SaveChangesAsync(ct);
        return Result.Success(affected);
    }

    private static InstrumentStatus MapStatus(string raw) => raw.ToUpperInvariant() switch
    {
        "TRADING" => InstrumentStatus.Trading,
        "HALT" => InstrumentStatus.Halt,
        "BREAK" => InstrumentStatus.Break,
        "PRE_TRADING" => InstrumentStatus.PreTrading,
        "POST_TRADING" => InstrumentStatus.PostTrading,
        "END_OF_DAY" => InstrumentStatus.EndOfDay,
        "AUCTION_MATCH" => InstrumentStatus.AuctionMatch,
        _ => InstrumentStatus.Halt,
    };
}
