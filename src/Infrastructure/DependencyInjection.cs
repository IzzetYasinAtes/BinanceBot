using BinanceBot.Application.Abstractions;
using BinanceBot.Application.Abstractions.Binance;
using BinanceBot.Application.Strategies.Evaluation;
using BinanceBot.Application.System.Queries.GetSystemStatus;
using BinanceBot.Infrastructure.Binance;
using BinanceBot.Infrastructure.Binance.Handlers;
using BinanceBot.Infrastructure.Binance.Streams;
using BinanceBot.Infrastructure.Binance.Workers;
using BinanceBot.Infrastructure.Monitoring;
using BinanceBot.Infrastructure.Orders;
using BinanceBot.Infrastructure.Persistence;
using BinanceBot.Infrastructure.Positions;
using BinanceBot.Infrastructure.Strategies;
using BinanceBot.Infrastructure.Strategies.Evaluators;
using BinanceBot.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace BinanceBot.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<BinanceOptions>()
            .Bind(configuration.GetSection(BinanceOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

        services.AddDbContext<ApplicationDbContext>(options =>
        {
            options.UseSqlServer(connectionString, sql =>
            {
                sql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName);
                sql.EnableRetryOnFailure(
                    maxRetryCount: 5,
                    maxRetryDelay: TimeSpan.FromSeconds(10),
                    errorNumbersToAdd: null);
            });
        });

        services.AddScoped<IApplicationDbContext>(sp =>
            sp.GetRequiredService<ApplicationDbContext>());

        services.AddSingleton<BinanceClock>();
        services.AddSingleton<IClock>(sp => sp.GetRequiredService<BinanceClock>());

        services.AddTransient<RateLimitHeaderHandler>();
        services.AddTransient<ApiKeyHandler>();
        services.AddTransient<SignedRequestHandler>();

        services.AddHttpClient(BinanceMarketDataClient.HttpClientName, (sp, client) =>
            {
                var opts = sp.GetRequiredService<IOptionsMonitor<BinanceOptions>>().CurrentValue;
                client.BaseAddress = new Uri(opts.RestBaseUrl);
                client.Timeout = TimeSpan.FromMilliseconds(opts.RestTimeoutMs);
                client.DefaultRequestHeaders.Add("User-Agent", "BinanceBot/0.1");
            })
            .AddHttpMessageHandler<ApiKeyHandler>()
            .AddHttpMessageHandler<RateLimitHeaderHandler>()
            .AddResilienceHandler("binance-rest-resilience", builder =>
            {
                builder.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = 3,
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    Delay = TimeSpan.FromMilliseconds(250),
                    ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                        .Handle<HttpRequestException>()
                        .Handle<TaskCanceledException>()
                        .HandleResult(r => (int)r.StatusCode >= 500 || (int)r.StatusCode == 408),
                });

                builder.AddTimeout(TimeSpan.FromSeconds(15));
            });

        services.AddScoped<IBinanceMarketData>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            var http = factory.CreateClient(BinanceMarketDataClient.HttpClientName);
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<BinanceMarketDataClient>>();
            return new BinanceMarketDataClient(http, logger);
        });

        services.AddHttpClient(BinanceTradingClient.HttpClientName, (sp, client) =>
            {
                var opts = sp.GetRequiredService<IOptionsMonitor<BinanceOptions>>().CurrentValue;
                client.BaseAddress = new Uri(opts.RestBaseUrl);
                client.Timeout = TimeSpan.FromMilliseconds(opts.RestTimeoutMs);
                client.DefaultRequestHeaders.Add("User-Agent", "BinanceBot/0.1");
            })
            .AddHttpMessageHandler<ApiKeyHandler>()
            .AddHttpMessageHandler<SignedRequestHandler>()
            .AddHttpMessageHandler<RateLimitHeaderHandler>();

        services.AddScoped<IBinanceTrading>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            var http = factory.CreateClient(BinanceTradingClient.HttpClientName);
            var opts = sp.GetRequiredService<IOptionsMonitor<BinanceOptions>>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<BinanceTradingClient>>();
            return new BinanceTradingClient(http, opts, logger);
        });

        services.AddSingleton<BinanceStreamBus>();
        services.AddSingleton<IBinanceMarketStream>(sp => sp.GetRequiredService<BinanceStreamBus>());

        services.AddSingleton<BinanceWsSupervisor>();
        services.AddHostedService(sp => sp.GetRequiredService<BinanceWsSupervisor>());
        services.AddScoped<ISystemStatusProvider, SystemStatusProvider>();
        services.AddHostedService<ClockSyncWorker>();
        services.AddHostedService<SymbolFiltersRefresher>();
        services.AddHostedService<KlineIngestionWorker>();
        services.AddHostedService<BookTickerIngestionWorker>();
        services.AddHostedService<DepthSnapshotWorker>();

        services.AddSingleton<IStrategyEvaluator, GridEvaluator>();
        services.AddSingleton<IStrategyEvaluator, TrendFollowingEvaluator>();
        services.AddSingleton<IStrategyEvaluator, MeanReversionEvaluator>();
        services.AddSingleton<StrategyEvaluatorRegistry>();

        services.AddOptions<StrategySeedOptions>()
            .Bind(configuration.GetSection(StrategySeedOptions.SectionName));
        services.AddHostedService<StrategySeeder>();
        services.AddTransient<MediatR.INotificationHandler<BinanceBot.Domain.MarketData.Events.KlineClosedEvent>,
            StrategyEvaluationHandler>();
        services.AddTransient<MediatR.INotificationHandler<BinanceBot.Domain.Strategies.Events.StrategySignalEmittedEvent>,
            StrategySignalToOrderHandler>();
        services.AddTransient<MediatR.INotificationHandler<BinanceBot.Domain.Orders.Events.OrderFilledEvent>,
            OrderFilledPositionHandler>();
        services.AddTransient<MediatR.INotificationHandler<BinanceBot.Domain.Positions.Events.PositionClosedEvent>,
            PositionClosedRiskHandler>();
        services.AddTransient<MediatR.INotificationHandler<BinanceBot.Domain.RiskProfiles.Events.CircuitBreakerTrippedEvent>,
            BinanceBot.Infrastructure.RiskProfiles.CircuitBreakerTrippedHandler>();
        services.AddHostedService<MarkToMarketWorker>();

        return services;
    }
}
