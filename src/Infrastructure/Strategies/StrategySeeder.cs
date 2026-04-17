using BinanceBot.Application.Abstractions;
using BinanceBot.Application.Strategies.Commands.ActivateStrategy;
using BinanceBot.Application.Strategies.Commands.CreateStrategy;
using BinanceBot.Application.Strategies.Commands.UpdateStrategyParameters;
using BinanceBot.Domain.Strategies;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BinanceBot.Infrastructure.Strategies;

/// <summary>
/// appsettings'teki Strategies:Seed[] listesini boot'ta DB ile senkronize eder:
///   - DB'de ismiyle yoksa yaratır
///   - Varsa ParametersJson guncelse guncellenir
///   - Activate=true ise Active degilse aktif eder
/// Config = source of truth; DB sadece runtime state + history tutar.
/// </summary>
public sealed class StrategySeeder : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<StrategySeedOptions> _options;
    private readonly ILogger<StrategySeeder> _logger;

    public StrategySeeder(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<StrategySeedOptions> options,
        ILogger<StrategySeeder> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var seeds = _options.CurrentValue.Seed ?? [];
        if (seeds.Length == 0)
        {
            _logger.LogInformation("No strategy seed configured.");
            return;
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        foreach (var entry in seeds)
        {
            if (!Enum.TryParse<StrategyType>(entry.Type, true, out _))
            {
                _logger.LogWarning("Strategy seed '{Name}' skipped: unknown type '{Type}'",
                    entry.Name, entry.Type);
                continue;
            }

            var existing = await db.Strategies
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Name == entry.Name, cancellationToken);

            long id;
            if (existing is null)
            {
                var create = await mediator.Send(new CreateStrategyCommand(
                    entry.Name, entry.Type, entry.Symbols, entry.ParametersJson),
                    cancellationToken);

                if (!create.IsSuccess)
                {
                    _logger.LogWarning("Strategy seed '{Name}' create failed: {Errors}",
                        entry.Name, string.Join(";", create.Errors));
                    continue;
                }
                id = create.Value;
                _logger.LogInformation("Strategy seeded: {Name} (id={Id}, type={Type})",
                    entry.Name, id, entry.Type);
            }
            else
            {
                id = existing.Id;
                if (!string.Equals(existing.ParametersJson, entry.ParametersJson, StringComparison.Ordinal)
                    && existing.Status != StrategyStatus.Active)
                {
                    var upd = await mediator.Send(
                        new UpdateStrategyParametersCommand(id, entry.ParametersJson),
                        cancellationToken);
                    if (upd.IsSuccess)
                    {
                        _logger.LogInformation("Strategy params updated for {Name}", entry.Name);
                    }
                }
            }

            if (entry.Activate)
            {
                var act = await mediator.Send(new ActivateStrategyCommand(id), cancellationToken);
                if (act.IsSuccess)
                {
                    _logger.LogInformation("Strategy '{Name}' activated from config", entry.Name);
                }
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
