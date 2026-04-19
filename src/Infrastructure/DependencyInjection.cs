using BinanceBot.Application.Abstractions;
using BinanceBot.Application.Abstractions.Binance;
using BinanceBot.Application.Abstractions.Trading;
using BinanceBot.Application.Strategies.Evaluation;
using BinanceBot.Application.Strategies.Indicators;
using BinanceBot.Application.System.Queries.GetSystemStatus;
using BinanceBot.Application.SystemEvents;
using BinanceBot.Infrastructure.SystemEvents;
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
using BinanceBot.Infrastructure.Strategies.Indicators;
using BinanceBot.Infrastructure.Risk;
using BinanceBot.Infrastructure.Time;
using BinanceBot.Infrastructure.Trading;
using BinanceBot.Infrastructure.Trading.Paper;
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

        // ADR-0011 §11.5 + decision-sizing.md Commit 1: Paper-only slippage config.
        services.Configure<PaperFillOptions>(configuration.GetSection("PaperFill"));

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

        services.AddSingleton<IBinanceCredentialsProvider, BinanceCredentialsProvider>();
        services.AddSingleton<IPaperFillSimulator, PaperFillSimulator>();

        // ADR-0011 §11.3 — equity snapshot is per-request (DbContext is scoped).
        services.AddScoped<IEquitySnapshotProvider, EquitySnapshotProvider>();

        services.AddSingleton<BinanceStreamBus>();
        services.AddSingleton<IBinanceMarketStream>(sp => sp.GetRequiredService<BinanceStreamBus>());

        services.AddSingleton<BinanceWsSupervisor>();
        services.AddHostedService(sp => sp.GetRequiredService<BinanceWsSupervisor>());
        services.AddSingleton<IWsReadinessProbe>(sp => sp.GetRequiredService<BinanceWsSupervisor>());
        services.AddScoped<ISystemStatusProvider, SystemStatusProvider>();
        services.AddHostedService<ClockSyncWorker>();
        services.AddHostedService<SymbolFiltersRefresher>();
        services.AddScoped<IKlinePersister, KlinePersister>();
        // Backfill must register BEFORE KlineIngestionWorker so the hosted-service
        // start order is REST snapshot first, then WS persist (ADR-0009).
        services.AddHostedService<KlineBackfillWorker>();
        services.AddHostedService<KlineIngestionWorker>();
        services.AddHostedService<BookTickerIngestionWorker>();
        services.AddHostedService<DepthSnapshotWorker>();

        // ADR-0015 §15.5 — VWAP-EMA hybrid surface. MarketIndicatorService is a
        // hosted singleton (IHostedService) that owns the rolling 1m/1h buffers and
        // drains the shared kline Channel; the evaluator is a stateless consumer of
        // its snapshot API. PatternScalpingEvaluator + 14 detectors removed (ADR-0014
        // superseded).
        services.AddSingleton<MarketIndicatorService>();
        services.AddSingleton<IMarketIndicatorService>(sp => sp.GetRequiredService<MarketIndicatorService>());
        services.AddHostedService(sp => sp.GetRequiredService<MarketIndicatorService>());

        services.AddSingleton<IStrategyEvaluator, VwapEmaStrategyEvaluator>();
        // ADR-0018 §18.11 — MicroScalper evaluator registered alongside the deprecated
        // VwapEma evaluator; registry dispatches by StrategyType enum value.
        services.AddSingleton<IStrategyEvaluator, MicroScalperVwapEma30sEvaluator>();
        services.AddSingleton<StrategyEvaluatorRegistry>();

        services.AddOptions<StrategySeedOptions>()
            .Bind(configuration.GetSection(StrategySeedOptions.SectionName));
        services.AddHostedService<StrategySeeder>();

        // ADR-0011 §11.8 + decision-sizing.md Commit 9 — keep RiskProfile rows in sync
        // with appsettings on every boot (idempotent, no migration required).
        services.Configure<RiskProfileDefaultsOptions>(
            configuration.GetSection(RiskProfileDefaultsOptions.SectionName));
        services.AddHostedService<RiskProfileSeeder>();
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

        // ADR-0016 §16.9 — SystemEvents persistence pipe. Publisher scoped (shares
        // DbContext lifetime). MediatR scan yalnızca Application assembly'sini
        // tarar (bkz. Application/DependencyInjection.cs), Infrastructure
        // handler'ları explicit kaydedilir.
        services.AddScoped<ISystemEventPublisher, SystemEventPublisher>();
        services.AddHostedService<AppLifecycleHostedService>();
        services.AddTransient<MediatR.INotificationHandler<BinanceBot.Domain.SystemEvents.Events.AppStartedEvent>,
            BinanceBot.Infrastructure.SystemEvents.Handlers.AppStartedSystemEventHandler>();
        services.AddTransient<MediatR.INotificationHandler<BinanceBot.Domain.SystemEvents.Events.AppStoppingEvent>,
            BinanceBot.Infrastructure.SystemEvents.Handlers.AppStoppingSystemEventHandler>();
        services.AddTransient<MediatR.INotificationHandler<BinanceBot.Domain.SystemEvents.Events.IndicatorWarmupCompletedEvent>,
            BinanceBot.Infrastructure.SystemEvents.Handlers.IndicatorWarmupCompletedSystemEventHandler>();
        services.AddTransient<MediatR.INotificationHandler<BinanceBot.Domain.SystemEvents.Events.WsConnectionStateChangedEvent>,
            BinanceBot.Infrastructure.SystemEvents.Handlers.WsConnectionStateChangedSystemEventHandler>();
        services.AddTransient<MediatR.INotificationHandler<BinanceBot.Domain.Strategies.Events.StrategyActivatedEvent>,
            BinanceBot.Infrastructure.SystemEvents.Handlers.StrategyActivatedSystemEventHandler>();
        services.AddTransient<MediatR.INotificationHandler<BinanceBot.Domain.Strategies.Events.StrategyDeactivatedEvent>,
            BinanceBot.Infrastructure.SystemEvents.Handlers.StrategyDeactivatedSystemEventHandler>();
        services.AddTransient<MediatR.INotificationHandler<BinanceBot.Domain.Strategies.Events.StrategySignalEmittedEvent>,
            BinanceBot.Infrastructure.SystemEvents.Handlers.StrategySignalEmittedSystemEventHandler>();
        services.AddTransient<MediatR.INotificationHandler<BinanceBot.Domain.SystemEvents.Events.StrategySignalSkippedEvent>,
            BinanceBot.Infrastructure.SystemEvents.Handlers.StrategySignalSkippedSystemEventHandler>();
        services.AddTransient<MediatR.INotificationHandler<BinanceBot.Domain.Orders.Events.OrderPlacedEvent>,
            BinanceBot.Infrastructure.SystemEvents.Handlers.OrderPlacedSystemEventHandler>();
        services.AddTransient<MediatR.INotificationHandler<BinanceBot.Domain.Orders.Events.OrderFilledEvent>,
            BinanceBot.Infrastructure.SystemEvents.Handlers.OrderFilledSystemEventHandler>();
        services.AddTransient<MediatR.INotificationHandler<BinanceBot.Domain.Orders.Events.OrderCancelledEvent>,
            BinanceBot.Infrastructure.SystemEvents.Handlers.OrderCancelledSystemEventHandler>();
        services.AddTransient<MediatR.INotificationHandler<BinanceBot.Domain.Positions.Events.PositionOpenedEvent>,
            BinanceBot.Infrastructure.SystemEvents.Handlers.PositionOpenedSystemEventHandler>();
        services.AddTransient<MediatR.INotificationHandler<BinanceBot.Domain.Positions.Events.PositionClosedEvent>,
            BinanceBot.Infrastructure.SystemEvents.Handlers.PositionClosedSystemEventHandler>();
        services.AddTransient<MediatR.INotificationHandler<BinanceBot.Domain.RiskProfiles.Events.CircuitBreakerTrippedEvent>,
            BinanceBot.Infrastructure.SystemEvents.Handlers.CircuitBreakerTrippedSystemEventHandler>();

        // ADR-0012 §12.3: client-side stop monitor (30s tick), mode-agnostic (mainnet
        // skipped defensively).
        services.AddHostedService<StopLossMonitorService>();

        // Loop 10 take-profit fix: symmetric counterpart to StopLossMonitor — closes open
        // positions when mark price reaches the persisted Position.TakeProfit. Pre-Loop 10
        // strategies opened positions but only ever exited via stop or rare exit signals;
        // unrealised gains never converted to realised PnL, so equity peaked & bled.
        services.AddHostedService<TakeProfitMonitorService>();

        // Loop 7 bug #17: intraday peak-equity tracker (30s tick) so PeakEquity follows
        // the live equity stream — closes alone don't capture unrealized spikes (Loop 6
        // t30 $195 spike was lost, t90 trip computed against stale $99 peak).
        services.AddHostedService<EquityPeakTrackerService>();

        return services;
    }
}
