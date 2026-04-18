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
        // session.
        //
        // Loop 13 boot fix: switched from GetEquityAsync (MTM = balance + unrealized)
        // to GetRealizedEquityAsync (cash balance only). Loop 12 surfaced that the
        // close-time snapshot was still latching MTM peaks (e.g. peak=$169 on a
        // session that ended at $106 cash) because at the moment PositionClosed fires
        // there can still be other open positions contributing unrealized PnL.
        // EquityPeakTrackerService already uses realized-only (Loop 6/7/9/10/11) —
        // this aligns the close-time snapshot with the periodic ratchet so the
        // peak/current pair is comparable. ADR-0008 §8.4 / ADR-0011 §11.3.
        var equity = await equityProvider.GetRealizedEquityAsync(notification.Mode, cancellationToken);

        await mediator.Send(
            new RecordTradeOutcomeCommand(notification.Mode, notification.RealizedPnl, equity),
            cancellationToken);

        _logger.LogInformation("Trade outcome recorded: pos={PosId} pnl={Pnl} equity={Equity}",
            notification.PositionId, notification.RealizedPnl, equity);
    }
}
