using BinanceBot.Application.Abstractions;
using BinanceBot.Application.Strategies.Commands.EmitStrategySignal;
using BinanceBot.Application.Strategies.Evaluation;
using BinanceBot.Domain.MarketData;
using BinanceBot.Domain.MarketData.Events;
using BinanceBot.Domain.Strategies;
using BinanceBot.Domain.ValueObjects;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BinanceBot.Infrastructure.Strategies;

public sealed class StrategyEvaluationHandler : INotificationHandler<KlineClosedEvent>
{
    private const int HistoryBarLimit = 250;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StrategyEvaluationHandler> _logger;

    public StrategyEvaluationHandler(
        IServiceScopeFactory scopeFactory,
        ILogger<StrategyEvaluationHandler> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task Handle(KlineClosedEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation("KlineClosed received {Symbol} {Interval} {OpenTime}",
            notification.Symbol, notification.Interval, notification.OpenTime);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var registry = scope.ServiceProvider.GetRequiredService<StrategyEvaluatorRegistry>();

        var symbolVo = Symbol.From(notification.Symbol);
        var activeStrategies = await db.Strategies
            .AsNoTracking()
            .Where(s => s.Status == StrategyStatus.Active
                     && EF.Functions.Like(s.SymbolsCsv, "%" + notification.Symbol + "%"))
            .ToListAsync(cancellationToken);

        _logger.LogInformation("Active strategies for {Symbol}: {Count}",
            notification.Symbol, activeStrategies.Count);

        if (activeStrategies.Count == 0)
        {
            return;
        }

        var bars = await db.Klines
            .AsNoTracking()
            .Where(k => k.Symbol == symbolVo
                     && k.Interval == notification.Interval
                     && k.IsClosed
                     && k.OpenTime <= notification.OpenTime)
            .OrderByDescending(k => k.OpenTime)
            .Take(HistoryBarLimit)
            .ToListAsync(cancellationToken);

        _logger.LogInformation("Bar history for {Symbol}: {Count} bars", notification.Symbol, bars.Count);

        if (bars.Count == 0) return;
        bars.Reverse();

        foreach (var strategy in activeStrategies)
        {
            var configured = strategy.SymbolsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (!configured.Contains(notification.Symbol, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var evaluator = registry.Resolve(strategy.Type);
            if (evaluator is null)
            {
                _logger.LogWarning("No evaluator for strategy type {Type} (id={Id})",
                    strategy.Type, strategy.Id);
                continue;
            }

            StrategyEvaluation? eval;
            try
            {
                eval = await evaluator.EvaluateAsync(
                    strategy.Id, strategy.ParametersJson,
                    notification.Symbol, bars, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Evaluator {Type} threw on strategy {Id}",
                    strategy.Type, strategy.Id);
                continue;
            }

            if (eval is null) continue;

            var emit = new EmitStrategySignalCommand(
                strategy.Id,
                notification.Symbol,
                notification.OpenTime,
                eval.Direction.ToString(),
                eval.SuggestedQuantity,
                eval.SuggestedPrice,
                eval.SuggestedStopPrice,
                eval.ContextJson,
                SuggestedTakeProfit: eval.SuggestedTakeProfit);

            var result = await mediator.Send(emit, cancellationToken);
            if (!result.IsSuccess)
            {
                _logger.LogWarning("Signal emit failed for strategy {Id} {Symbol}: {Errors}",
                    strategy.Id, notification.Symbol, string.Join(";", result.Errors));
            }
        }
    }
}
