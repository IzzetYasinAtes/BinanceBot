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

        // ADR-0012 §12.10: confirms the PositionClosedEvent dispatch chain reached this
        // handler — needed for Loop 5 t30 CB-bug audit (was the chain even firing?).
        _logger.LogInformation(
            "CB-AUDIT PositionClosed handler entered pos={PosId} mode={Mode} pnl={Pnl} reason={Reason}",
            notification.PositionId, notification.Mode, notification.RealizedPnl, notification.Reason);

        var totalRealised = await db.Positions
            .AsNoTracking()
            .Where(p => p.Mode == notification.Mode && p.Status == PositionStatus.Closed)
            .SumAsync(p => (decimal?)p.RealizedPnl, cancellationToken) ?? 0m;

        var openUnrealised = await db.Positions
            .AsNoTracking()
            .Where(p => p.Mode == notification.Mode && p.Status == PositionStatus.Open)
            .SumAsync(p => (decimal?)p.UnrealizedPnl, cancellationToken) ?? 0m;

        var equity = totalRealised + openUnrealised;

        await mediator.Send(
            new RecordTradeOutcomeCommand(notification.Mode, notification.RealizedPnl, equity),
            cancellationToken);

        _logger.LogInformation("Trade outcome recorded: pos={PosId} pnl={Pnl} equity={Equity}",
            notification.PositionId, notification.RealizedPnl, equity);
    }
}
