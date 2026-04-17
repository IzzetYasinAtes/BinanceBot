using BinanceBot.Application.Orders.Commands.PlaceOrder;
using BinanceBot.Domain.Common;
using BinanceBot.Domain.Orders;
using BinanceBot.Domain.Strategies.Events;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BinanceBot.Infrastructure.Strategies;

/// <summary>
/// Fan-out one strategy signal to three PlaceOrderCommand invocations — one per TradingMode
/// (Paper / LiveTestnet / LiveMainnet). Each gets a distinct ClientOrderId via mode suffix
/// (ADR-0008 §8.2). Failures in one mode must not block the others.
/// </summary>
public sealed class StrategySignalToOrderHandler : INotificationHandler<StrategySignalEmittedEvent>
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StrategySignalToOrderHandler> _logger;

    public StrategySignalToOrderHandler(
        IServiceScopeFactory scopeFactory,
        ILogger<StrategySignalToOrderHandler> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task Handle(StrategySignalEmittedEvent notification, CancellationToken cancellationToken)
    {
        if (notification.Direction == Domain.Strategies.StrategySignalDirection.Exit)
        {
            _logger.LogInformation("Exit signal ignored (close-position path pending S6)");
            return;
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var side = notification.Direction == Domain.Strategies.StrategySignalDirection.Long
            ? OrderSide.Buy
            : OrderSide.Sell;

        var barUnix = notification.BarOpenTime.ToUnixTimeSeconds();

        foreach (var mode in new[] { TradingMode.Paper, TradingMode.LiveTestnet, TradingMode.LiveMainnet })
        {
            var clientOrderId = $"sig-{notification.StrategyId}-{barUnix}-{mode.ToCidSuffix()}";

            var cmd = new PlaceOrderCommand(
                clientOrderId,
                notification.Symbol,
                side.ToString(),
                OrderType.Market.ToString(),
                TimeInForce.Ioc.ToString(),
                0.001m,
                null,
                null,
                notification.StrategyId,
                mode);

            try
            {
                var result = await mediator.Send(cmd, cancellationToken);
                if (!result.IsSuccess)
                {
                    _logger.LogWarning(
                        "Fan-out order rejected mode={Mode} strategy={Id} {Symbol}: {Errors}",
                        mode, notification.StrategyId, notification.Symbol,
                        string.Join(";", result.Errors));
                }
                else
                {
                    _logger.LogInformation(
                        "Fan-out order placed mode={Mode} strategy={Id} {Symbol} {Cid} status={Status}",
                        mode, notification.StrategyId, notification.Symbol, clientOrderId,
                        result.Value.Status);
                }
            }
            catch (Exception ex)
            {
                // One mode's failure must never cascade to the others.
                _logger.LogError(ex,
                    "Fan-out order exception mode={Mode} strategy={Id} {Symbol} {Cid}",
                    mode, notification.StrategyId, notification.Symbol, clientOrderId);
            }
        }
    }
}
