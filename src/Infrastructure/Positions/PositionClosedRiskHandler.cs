using BinanceBot.Application.Abstractions;
using BinanceBot.Application.Abstractions.Trading;
using BinanceBot.Application.RiskProfiles.Commands.RecordTradeOutcome;
using BinanceBot.Domain.Positions.Events;
using MediatR;
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
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var equityProvider = scope.ServiceProvider.GetRequiredService<IEquitySnapshotProvider>();

        // ADR-0012 §12.10: confirms the PositionClosedEvent dispatch chain reached this
        // handler — needed for Loop 5 t30 CB-bug audit (was the chain even firing?).
        _logger.LogInformation(
            "CB-AUDIT PositionClosed handler entered pos={PosId} mode={Mode} pnl={Pnl} reason={Reason}",
            notification.PositionId, notification.Mode, notification.RealizedPnl, notification.Reason);

        // Loop 5 t90 CB-BUG fix: previously equity was computed as
        //   totalRealisedPnl(closed) + totalUnrealisedPnl(open)
        // — this excludes the cash balance, so PeakEquity tracked PnL deltas (~$0.01)
        // instead of true equity (~$112). Drawdown formula then exploded
        // (peak=0.0125, current≈0 → ratio≈1.0+) and tripped the CB on a profitable
        // session. Source of truth for equity is VirtualBalance.Equity
        // (= CurrentBalance + unrealizedPnl, ADR-0008 §8.4 / ADR-0011 §11.3),
        // surfaced via IEquitySnapshotProvider.
        var equity = await equityProvider.GetEquityAsync(notification.Mode, cancellationToken);

        await mediator.Send(
            new RecordTradeOutcomeCommand(notification.Mode, notification.RealizedPnl, equity),
            cancellationToken);

        _logger.LogInformation("Trade outcome recorded: pos={PosId} pnl={Pnl} equity={Equity}",
            notification.PositionId, notification.RealizedPnl, equity);
    }
}
