using BinanceBot.Application.Abstractions;
using BinanceBot.Application.RiskProfiles.Commands.RecordTradeOutcome;
using BinanceBot.Domain.Positions;
using BinanceBot.Domain.Positions.Events;
using BinanceBot.Domain.RiskProfiles;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BinanceBot.Infrastructure.Positions;

public sealed class PositionClosedRiskHandler : INotificationHandler<PositionClosedEvent>
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PositionClosedRiskHandler> _logger;

    public PositionClosedRiskHandler(
        IServiceScopeFactory scopeFactory,
        ILogger<PositionClosedRiskHandler> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task Handle(PositionClosedEvent notification, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var totalRealised = await db.Positions
            .AsNoTracking()
            .Where(p => p.Status == PositionStatus.Closed)
            .SumAsync(p => (decimal?)p.RealizedPnl, cancellationToken) ?? 0m;

        var openUnrealised = await db.Positions
            .AsNoTracking()
            .Where(p => p.Status == PositionStatus.Open)
            .SumAsync(p => (decimal?)p.UnrealizedPnl, cancellationToken) ?? 0m;

        var equity = totalRealised + openUnrealised;

        await mediator.Send(
            new RecordTradeOutcomeCommand(notification.RealizedPnl, equity),
            cancellationToken);

        _logger.LogInformation("Trade outcome recorded: pos={PosId} pnl={Pnl} equity={Equity}",
            notification.PositionId, notification.RealizedPnl, equity);
    }
}
