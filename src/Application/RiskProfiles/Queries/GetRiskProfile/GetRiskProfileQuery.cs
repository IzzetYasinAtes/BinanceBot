using Ardalis.Result;
using BinanceBot.Application.Abstractions;
using BinanceBot.Application.RiskProfiles.Queries;
using BinanceBot.Domain.Common;
using BinanceBot.Domain.RiskProfiles;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BinanceBot.Application.RiskProfiles.Queries.GetRiskProfile;

public sealed record GetRiskProfileQuery() : IRequest<Result<RiskProfileDto>>;

public sealed class GetRiskProfileQueryHandler
    : IRequestHandler<GetRiskProfileQuery, Result<RiskProfileDto>>
{
    private readonly IApplicationDbContext _db;

    public GetRiskProfileQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<Result<RiskProfileDto>> Handle(GetRiskProfileQuery request, CancellationToken ct)
    {
        var r = await _db.RiskProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(rp => rp.Id == RiskProfile.IdFor(TradingMode.Paper), ct);

        if (r is null)
        {
            return Result<RiskProfileDto>.NotFound("Risk profile singleton missing (seed failed?).");
        }

        return Result.Success(new RiskProfileDto(
            r.RiskPerTradePct,
            r.MaxPositionSizePct,
            r.MaxDrawdown24hPct,
            r.MaxDrawdownAllTimePct,
            r.MaxConsecutiveLosses,
            r.RiskPerTradeCap,
            r.MaxPositionCap,
            r.CapsAdminNote,
            r.ConsecutiveLosses,
            r.CurrentDrawdownPct,
            r.RealizedPnl24h,
            r.RealizedPnlAllTime,
            r.PeakEquity,
            r.UpdatedAt));
    }
}
