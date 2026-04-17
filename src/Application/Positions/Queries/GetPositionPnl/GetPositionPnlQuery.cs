using Ardalis.Result;
using BinanceBot.Application.Abstractions;
using BinanceBot.Application.Positions.Queries;
using BinanceBot.Domain.Positions;
using BinanceBot.Domain.ValueObjects;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BinanceBot.Application.Positions.Queries.GetPositionPnl;

public sealed record GetPositionPnlQuery(string Symbol) : IRequest<Result<PositionPnlDto>>;

public sealed class GetPositionPnlQueryHandler
    : IRequestHandler<GetPositionPnlQuery, Result<PositionPnlDto>>
{
    private readonly IApplicationDbContext _db;

    public GetPositionPnlQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<Result<PositionPnlDto>> Handle(GetPositionPnlQuery request, CancellationToken ct)
    {
        var symbolVo = Symbol.From(request.Symbol);
        var position = await _db.Positions
            .AsNoTracking()
            .Where(p => p.Symbol == symbolVo && p.Status == PositionStatus.Open)
            .OrderByDescending(p => p.UpdatedAt)
            .FirstOrDefaultAsync(ct);

        if (position is null)
        {
            return Result<PositionPnlDto>.NotFound($"No open position for {symbolVo}.");
        }

        return Result.Success(new PositionPnlDto(
            position.Symbol.Value,
            position.UnrealizedPnl,
            position.RealizedPnl,
            position.MarkPrice,
            position.AverageEntryPrice));
    }
}
