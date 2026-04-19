using BinanceBot.Domain.SystemEvents.Events;
using MediatR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BinanceBot.Infrastructure.SystemEvents;

internal sealed class AppLifecycleHostedService : IHostedService
{
    private readonly IPublisher _publisher;
    private readonly IHostEnvironment _env;
    private readonly ILogger<AppLifecycleHostedService> _logger;
    private readonly string _hostName;

    public AppLifecycleHostedService(
        IPublisher publisher,
        IHostEnvironment env,
        ILogger<AppLifecycleHostedService> logger)
    {
        _publisher = publisher;
        _env = env;
        _logger = logger;
        _hostName = Environment.MachineName;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _publisher.Publish(
                new AppStartedEvent(_hostName, _env.EnvironmentName),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AppStartedEvent publish failed");
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _publisher.Publish(
                new AppStoppingEvent(_hostName),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AppStoppingEvent publish failed");
        }
    }
}
