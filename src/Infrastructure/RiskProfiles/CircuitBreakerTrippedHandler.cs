using BinanceBot.Application.Abstractions;
using BinanceBot.Application.Strategies.Commands.DeactivateStrategy;
using BinanceBot.Domain.RiskProfiles.Events;
using BinanceBot.Domain.Strategies;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BinanceBot.Infrastructure.RiskProfiles;

public sealed class CircuitBreakerTrippedHandler : INotificationHandler<CircuitBreakerTrippedEvent>
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CircuitBreakerTrippedHandler> _logger;

    public CircuitBreakerTrippedHandler(
        IServiceScopeFactory scopeFactory,
        ILogger<CircuitBreakerTrippedHandler> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task Handle(CircuitBreakerTrippedEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogCritical("Circuit breaker TRIPPED: {Reason} drawdown={Drawdown:P2}",
            notification.Reason, notification.ObservedDrawdownPct);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var activeIds = await db.Strategies
            .AsNoTracking()
            .Where(s => s.Status == StrategyStatus.Active)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        foreach (var id in activeIds)
        {
            var result = await mediator.Send(
                new DeactivateStrategyCommand(id, $"circuit_breaker:{notification.Reason}"),
                cancellationToken);
            if (!result.IsSuccess)
            {
                _logger.LogWarning("Could not deactivate strategy {Id}: {Errors}",
                    id, string.Join(";", result.Errors));
            }
        }

        _logger.LogWarning("Circuit breaker kill-switch deactivated {Count} strategies", activeIds.Count);
    }
}
