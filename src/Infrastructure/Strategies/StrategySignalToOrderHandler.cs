using BinanceBot.Application.Orders.Commands.PlaceOrder;
using BinanceBot.Domain.Orders;
using BinanceBot.Domain.Strategies.Events;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BinanceBot.Infrastructure.Strategies;

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

        var clientOrderId = $"sig-{notification.StrategyId}-{notification.BarOpenTime.ToUnixTimeSeconds()}";
        var side = notification.Direction == Domain.Strategies.StrategySignalDirection.Long
            ? OrderSide.Buy
            : OrderSide.Sell;

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
            DryRun: true);

        var result = await mediator.Send(cmd, cancellationToken);
        if (!result.IsSuccess)
        {
            _logger.LogWarning("Signal dry-run order rejected for strategy {Id} {Symbol}: {Errors}",
                notification.StrategyId, notification.Symbol, string.Join(";", result.Errors));
        }
        else
        {
            _logger.LogInformation("Dry-run order placed from signal: strategy {Id} {Symbol} {Cid}",
                notification.StrategyId, notification.Symbol, clientOrderId);
        }
    }
}
