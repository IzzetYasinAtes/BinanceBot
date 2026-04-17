using System.Text.Json;
using Ardalis.Result;
using BinanceBot.Application.Abstractions;
using BinanceBot.Application.MarketData.Queries;
using BinanceBot.Domain.ValueObjects;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BinanceBot.Application.MarketData.Queries.GetDepthSnapshot;

public sealed record GetDepthSnapshotQuery(string Symbol, int Depth) : IRequest<Result<DepthSnapshotDto>>;

public sealed class GetDepthSnapshotQueryValidator : AbstractValidator<GetDepthSnapshotQuery>
{
    private static readonly int[] AllowedDepths = [5, 10, 20, 50, 100, 500, 1000];

    public GetDepthSnapshotQueryValidator()
    {
        RuleFor(q => q.Symbol).NotEmpty();
        RuleFor(q => q.Depth)
            .Must(d => AllowedDepths.Contains(d))
            .WithMessage($"Depth must be one of: {string.Join(",", AllowedDepths)}");
    }
}

public sealed class GetDepthSnapshotQueryHandler
    : IRequestHandler<GetDepthSnapshotQuery, Result<DepthSnapshotDto>>
{
    private readonly IApplicationDbContext _db;

    public GetDepthSnapshotQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<Result<DepthSnapshotDto>> Handle(GetDepthSnapshotQuery request, CancellationToken ct)
    {
        var symbolVo = Symbol.From(request.Symbol);
        var snapshot = await _db.OrderBookSnapshots
            .AsNoTracking()
            .Where(s => s.Symbol == symbolVo)
            .OrderByDescending(s => s.CapturedAt)
            .FirstOrDefaultAsync(ct);

        if (snapshot is null)
        {
            return Result<DepthSnapshotDto>.NotFound($"Snapshot for {symbolVo} not available yet.");
        }

        var bids = JsonSerializer.Deserialize<List<DepthLevelDto>>(snapshot.BidsJson) ?? [];
        var asks = JsonSerializer.Deserialize<List<DepthLevelDto>>(snapshot.AsksJson) ?? [];

        var trimmedBids = bids.Take(request.Depth).ToList();
        var trimmedAsks = asks.Take(request.Depth).ToList();

        return Result.Success(new DepthSnapshotDto(
            snapshot.Symbol.Value,
            snapshot.LastUpdateId,
            trimmedBids,
            trimmedAsks,
            snapshot.CapturedAt));
    }
}
