using Ardalis.Result;
using BinanceBot.Application.Abstractions;
using BinanceBot.Application.Abstractions.Binance;
using BinanceBot.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BinanceBot.Application.Balances.Queries.GetBalances;

public sealed record GetBalancesQuery() : IRequest<Result<IReadOnlyList<BalanceDto>>>;

public sealed record BalanceDto(
    TradingMode Mode,
    string ModeName,
    decimal StartingBalance,
    decimal CurrentBalance,
    decimal Equity,
    Guid IterationId,
    int ResetCount,
    DateTimeOffset StartedAt,
    DateTimeOffset? LastResetAt,
    bool Available,
    bool Blocked,
    string? StatusReason);

public sealed class GetBalancesQueryHandler
    : IRequestHandler<GetBalancesQuery, Result<IReadOnlyList<BalanceDto>>>
{
    private readonly IApplicationDbContext _db;
    private readonly IBinanceCredentialsProvider _credentials;

    public GetBalancesQueryHandler(IApplicationDbContext db, IBinanceCredentialsProvider credentials)
    {
        _db = db;
        _credentials = credentials;
    }

    public async Task<Result<IReadOnlyList<BalanceDto>>> Handle(
        GetBalancesQuery request, CancellationToken ct)
    {
        var rows = await _db.VirtualBalances
            .AsNoTracking()
            .OrderBy(b => b.Id)
            .ToListAsync(ct);

        var hasTestnet = _credentials.HasTestnetCredentials();

        var dtos = rows.Select(b => new BalanceDto(
            Mode: b.Mode,
            ModeName: b.Mode.ToString(),
            StartingBalance: b.StartingBalance,
            CurrentBalance: b.CurrentBalance,
            Equity: b.Equity,
            IterationId: b.IterationId,
            ResetCount: b.ResetCount,
            StartedAt: b.StartedAt,
            LastResetAt: b.LastResetAt,
            Available: b.Mode switch
            {
                TradingMode.Paper => true,
                TradingMode.LiveTestnet => hasTestnet,
                TradingMode.LiveMainnet => false,
                _ => false,
            },
            Blocked: b.Mode == TradingMode.LiveMainnet,
            StatusReason: b.Mode switch
            {
                TradingMode.LiveMainnet => "adr_0006",
                TradingMode.LiveTestnet when !hasTestnet => "no_credentials",
                _ => null,
            })).ToList();

        return Result.Success<IReadOnlyList<BalanceDto>>(dtos);
    }
}
