using Ardalis.Result;
using BinanceBot.Application.Abstractions;
using BinanceBot.Application.System.Queries;
using MediatR;

namespace BinanceBot.Application.System.Queries.GetSystemStatus;

public sealed record GetSystemStatusQuery() : IRequest<Result<SystemStatusDto>>;

public interface ISystemStatusProvider
{
    string WsState { get; }
    bool TestnetOnly { get; }
    Task<(bool Up, string[] Pending)> GetDatabaseStateAsync(CancellationToken ct);
}

public sealed class GetSystemStatusQueryHandler
    : IRequestHandler<GetSystemStatusQuery, Result<SystemStatusDto>>
{
    private readonly IClock _clock;
    private readonly ISystemStatusProvider _provider;

    public GetSystemStatusQueryHandler(IClock clock, ISystemStatusProvider provider)
    {
        _clock = clock;
        _provider = provider;
    }

    public async Task<Result<SystemStatusDto>> Handle(GetSystemStatusQuery request, CancellationToken ct)
    {
        var (up, pending) = await _provider.GetDatabaseStateAsync(ct);

        return Result.Success(new SystemStatusDto(
            _provider.TestnetOnly,
            _provider.WsState,
            _clock.DriftMs,
            up,
            pending.Length,
            pending,
            _clock.UtcNow));
    }
}
