# Backend Design — BinanceBot

**Durum:** One-shot master plan adim 3/6 (backend-dev) + adim 6 final sentez patch'leri. Tasarim belgesi; `src/**` ve `tests/**` icine hicbir kod yazilmaz. Implementasyon `Program.cs` startup hook snippet i + `appsettings.json` template i ile sinirlidir.

**Girdiler:** [architecture-notes.md](./architecture-notes.md) (9 aggregate + BookTicker read-model + SystemEvent audit, ~47 slice, 27 event), [binance-research.md](./research/binance-research.md) (REST envanter, WS limitleri, resilience kalibi), [ADR 0001..0007](./adr/).

**Amac:** `docs/plan.md` bolum 2-6 icin backend teknik katman haritasini dondurmak; backend-dev feature slice'larina baslarken bu belgeye bakar.

---

## 1. Solution Layout

Mevcut iskelet (`src/BinanceBot.sln`): 4 runtime proje + 1 test proje; frontend ayri (backend-dev kapsaminda degil).

### 1.1 Proje Listesi ve Namespace Root

| Proje | Path | Namespace root | Rol |
|---|---|---|---|
| `BinanceBot.Domain` | `src/Domain/` | `BinanceBot.Domain` | Aggregate, entity, VO, domain event, invariant, port interface (domain service) |
| `BinanceBot.Application` | `src/Application/` | `BinanceBot.Application` | CQRS handler, validator, DTO, port interface (infra), notification handler, pipeline behavior |
| `BinanceBot.Infrastructure` | `src/Infrastructure/` | `BinanceBot.Infrastructure` | EF Core DbContext, migration, Binance REST/WS, Polly, BackgroundService, secret loader, SystemEvent audit tablo |
| `BinanceBot.Api` | `src/Api/` | `BinanceBot.Api` | ASP.NET Core host, Minimal API endpoint, DI composition root, middleware, Serilog, health check, boot-time guard, `ApiKeyAuthenticationHandler` (ADR-0007) |
| `BinanceBot.Tests` | `tests/Tests/` | `BinanceBot.Tests` | Tek test csproj; klasor bazinda layer ayrimi (`Domain/`, `Application/`, `Api/`) |

### 1.2 Dependency Grafigi (Clean Architecture)

```
Api.csproj
  -> Application.csproj
  -> Infrastructure.csproj (composition root — DI kaydi icin direkt)
  -> Domain.csproj (transitif)

Application.csproj
  -> Domain.csproj

Infrastructure.csproj
  -> Application.csproj  (port interface lerini implement eder)
  -> Domain.csproj
```

Domain hicbir sey import etmez. [architecture-notes.md §2](./architecture-notes.md) ile birebir hizali. Referans yon ihlali reviewer tarafindan reddedilir.

### 1.3 Alt Klasor Organizasyonu

#### `BinanceBot.Domain`

```
Domain/
  Common/                                   IAggregateRoot, IDomainEvent, Entity, ValueObject base
  MarketData/
    Klines/                                 Kline aggregate + events + VOs (OhlcvValues, KlineInterval)
    Trades/                                 Trade aggregate (insert-only)
    Depths/                                 Depth aggregate + DepthLevel entity + events
  Instruments/                              Instrument aggregate + SymbolFilters VO + events
  Orders/                                   Order aggregate + OrderFill entity + ClientOrderId VO + events
  Positions/                                Position aggregate + events
  Strategies/                               Strategy aggregate + StrategySignal entity + StrategyParameters VO + events
  RiskProfiles/                             RiskProfile aggregate + Percentage VO + events
  BacktestRuns/                             BacktestRun aggregate + BacktestTrade + BacktestMetrics VOs + events
  Shared/                                   Symbol, Price, Quantity, DateRange (cross-aggregate VO'lar)
  Abstractions/                             IClock, IStrategyEvaluator (domain service port)
```

#### `BinanceBot.Application`

```
Application/
  Common/
    Behaviors/                              ValidationBehavior, LoggingBehavior (MediatR pipeline)
    Exceptions/                             BaseApplicationException (sadece programmer error)
    Mappings/                               AutoMapper profile (opsiyonel; MVP manuel projection)
    Models/                                 PagedResult<T>, CursorResult<T>
  Abstractions/
    Persistence/                            IApplicationDbContext, IUnitOfWork, IReadDbContext
    Binance/                                IBinanceMarketData, IBinanceTrading, IBinanceAccount, IBinanceWsSubscriber, IBinanceTimeSource
    Observability/                          ISystemEventWriter (SystemEvent audit yazici)
    Services/                               IStopLossPlacer, ICurrentUser, IClock proxy
  MarketData/
    Klines/
      Commands/IngestKline/                 Command + Handler + Validator
      Queries/GetLatestKlines/
    Trades/Commands/RecordTrade/
    Depths/
      Commands/SyncDepthSnapshot/
      Queries/GetDepthSnapshot/
      Queries/GetBookTicker/
    Queries/GetMarketSummary/               (F1 eklendi — 3 sembol agrega)
  Instruments/
    Commands/RefreshSymbolFilters/
    Commands/ListSymbol/
    Commands/HaltSymbol/
    Queries/GetSymbolFilters/
    Queries/ListActiveSymbols/
  Orders/
    Commands/PlaceOrder/
    Commands/CancelOrder/
    Commands/RegisterOrderFill/
    Queries/GetOrderByClientId/
    Queries/ListOpenOrders/
    Queries/ListOrderHistory/
  Positions/
    Commands/OpenPosition/
    Commands/UpdatePosition/
    Commands/ClosePosition/
    Commands/MarkToMarket/
    Queries/ListPositions/                  (F3 — open+closed tek slice; eski GetOpenPositions supersede)
    Queries/GetPositionPnl/
    Queries/GetTodayPnl/                    (F1 eklendi)
  Strategies/
    Commands/CreateStrategy/
    Commands/ActivateStrategy/
    Commands/DeactivateStrategy/
    Commands/UpdateStrategyParameters/
    Commands/EmitStrategySignal/
    Queries/ListStrategies/
    Queries/GetStrategyDetail/              (F1 eklendi)
    Queries/GetStrategySignals/
  RiskProfiles/
    Commands/UpdateRiskProfile/
    Commands/OverrideRiskCaps/
    Commands/ResetCircuitBreaker/
    Commands/RecordTradeOutcome/
    Queries/GetRiskProfile/
    Queries/GetCircuitBreakerStatus/
    Queries/GetDrawdownHistory/             (F1 eklendi)
  System/
    Queries/GetSystemStatus/                (F1 eklendi)
    Queries/TailSystemEvents/               (F1 eklendi — SystemEvents tablosundan okur)
  BacktestRuns/
    Commands/StartBacktest/
    Commands/RecordBacktestTrade/
    Commands/CompleteBacktest/
    Queries/GetBacktestResult/
    Queries/ListBacktestRuns/
  Notifications/
    OrderFilled/                            PositionUpdaterHandler.cs, StopLossPlacementHandler.cs, RiskCheckHandler.cs, AuditLogHandler.cs
    PositionClosed/                         RiskTradeOutcomeRecorderHandler.cs, StrategyOutcomeRecorderHandler.cs
    CircuitBreakerTripped/                  StrategyDeactivatorHandler.cs, ActiveOrderCancelerHandler.cs, AlarmHandler.cs
    KlineClosed/                            StrategyEvaluatorHandler.cs
    DepthGapDetected/                       DepthResyncHandler.cs
    SymbolHalted/                           ActiveOrderCancelerHandler.cs
    StrategySignalEmitted/                  OrderPlacementHandler.cs
```

Slice klasoru = `Commands/<Action>/` altinda 3 dosya: `<Action>Command.cs`, `<Action>CommandHandler.cs`, `<Action>CommandValidator.cs`. DTO ayni slice icinde `<Name>Dto.cs` olarak yasar; global DTO klasoru yok.

#### `BinanceBot.Infrastructure`

```
Infrastructure/
  Persistence/
    ApplicationDbContext.cs                 IApplicationDbContext implementasyonu
    ApplicationDbContextFactory.cs          Design-time factory (dotnet ef CLI icin)
    Configurations/                         IEntityTypeConfiguration<T> — aggregate basina 1 dosya
      KlineConfiguration.cs
      TradeConfiguration.cs
      DepthConfiguration.cs
      InstrumentConfiguration.cs
      OrderConfiguration.cs
      PositionConfiguration.cs
      StrategyConfiguration.cs
      RiskProfileConfiguration.cs
      BacktestRunConfiguration.cs
      SystemEventConfiguration.cs           (audit tablosu — aggregate degil, plain table)
    Tables/
      SystemEvent.cs                        POCO (aggregate degil): Id, Timestamp, Level, Source, Type, Message, PayloadJson, CorrelationId
    Interceptors/                           DomainEventDispatchInterceptor, AuditInterceptor
    Migrations/                             EF CLI cikisi
    Repositories/                           aggregate-per-repository — KlineRepository, OrderRepository, ...
    Seed/                                   DefaultRiskProfileSeeder, InstrumentSeeder (testnet boot)
  Observability/
    SystemEventWriter.cs                    ISystemEventWriter impl — DbContext'e SystemEvent ekler
  Binance/
    Rest/
      BinanceMarketDataClient.cs            /api/v3/klines, /api/v3/depth, /api/v3/exchangeInfo — implements IBinanceMarketData
      BinanceTradingClient.cs               /api/v3/order, /api/v3/order/test — implements IBinanceTrading
      BinanceAccountClient.cs               /api/v3/account, /api/v3/myTrades — implements IBinanceAccount
      Handlers/
        HmacSignatureDelegatingHandler.cs   timestamp + HMAC-SHA256 imza
        RetryAfterAwareHandler.cs           429/418 Retry-After parser
        ClockOffsetHandler.cs               timestamp = now - offset
        WeightTrackerHandler.cs             X-MBX-USED-WEIGHT-1M log + metric
      Models/                               REST response DTO + mapping helpers
    Ws/
      BinanceWsSupervisor.cs                BackgroundService + reconnect state machine
      BinanceWsConnection.cs                ClientWebSocket wrapper + ping/pong timer
      HeartbeatManager.cs                   20s server ping / 60s pong timeout (client-initiated ping YOK)
      DepthBookSynchronizer.cs              snapshot + diff resync
      UserDataStreamKeepalive.cs            25dk PUT listenKey
      UserDataStreamReconnector.cs          reconnect: yeni listenKey + subscribe
      Channels/
        WsEventChannel.cs                   Channel<WsEvent> factory (bounded, DropOldest, 10_000)
      Messages/                             WS event DTO + deserialize
  Workers/
    ClockSyncWorker.cs                      saatte 1 /api/v3/time
    SymbolFiltersRefresher.cs               gunde 1 /api/v3/exchangeInfo
    StrategyEvaluatorWorker.cs              KlineClosed event driven
    RiskCircuitBreakerMonitor.cs            dakikada 1 MaxDrawdown / consecutive-losses
  Options/                                  BinanceOptions, RiskOptions, PersistenceOptions + IValidateOptions<T>
  Logging/                                  CorrelationIdMiddleware (Api'da cagrilacak helper)
  DependencyInjection/                      InfrastructureServiceCollectionExtensions.AddInfrastructure(...)
```

#### `BinanceBot.Api`

```
Api/
  Program.cs                                Composition root + startup hook
  Endpoints/
    MarketDataEndpoints.cs                  MapGroup("/api")
    InstrumentEndpoints.cs
    OrderEndpoints.cs
    PositionEndpoints.cs
    StrategyEndpoints.cs
    RiskProfileEndpoints.cs
    BacktestEndpoints.cs
    SystemEndpoints.cs                      /api/system/status, /api/logs/tail (F1)
    HealthEndpoints.cs                      /health/live, /health/ready
  Middleware/
    CorrelationIdMiddleware.cs
    GlobalExceptionHandler.cs               IExceptionHandler (programmer error only)
  Authentication/
    ApiKeyAuthenticationHandler.cs          X-Admin-Key header (ADR-0007); AllowMainnet=true iken 403
  Contracts/                                HTTP request/response DTO (command/query icin adapter)
  Configuration/
    StartupSecretValidator.cs               boot-time ApiKey/Secret/AdminApiKey/ConnectionString kontrolu
    BinanceEnvironmentGuard.cs              testnet-first guard (ADR 0004 + 0006)
  appsettings.json                          template — prod deger yok, connection string BOS
  appsettings.Development.json              dev override (local connection string placeholder)
```

#### `BinanceBot.Tests`

Tek csproj, klasor bazinda ayirma:

```
tests/Tests/
  Domain/
    Klines/KlineInvariantTests.cs
    Orders/OrderStateMachineTests.cs
    Positions/PositionAverageEntryTests.cs
    RiskProfiles/CircuitBreakerTests.cs
  Application/
    MarketData/IngestKlineHandlerTests.cs
    Orders/PlaceOrderHandlerTests.cs
    ...
  Infrastructure/
    Persistence/ApplicationDbContextTests.cs
    Binance/HmacSignatureHandlerTests.cs
  Api/
    Endpoints/OrderEndpointsIntegrationTests.cs  (WebApplicationFactory + Testcontainers MSSQL)
  Fixtures/                                 WebAppFixture, FakeClock, InMemoryBinanceClient
```

---

## 2. NuGet Paket Listesi + Versiyon Pin

.NET 10 stable uyumlu son stable versiyonlar. Semver **pin** — caret/tilde kullanilmayacak, aynen yazilacak. Surum guncelleme reviewer onayli.

| Paket | Version | Proje | Rol |
|---|---|---|---|
| `MediatR` | `12.4.1` | Application | CQRS request/response dispatch (12.x tek paket, DI extension dahil) |
| `FluentValidation` | `11.11.0` | Application | Validator type'lari |
| `FluentValidation.DependencyInjectionExtensions` | `11.11.0` | Api | `services.AddValidatorsFromAssembly(...)` |
| `Ardalis.Result` | `10.1.0` | Application, Domain | `Result<T>`, `Result.Invalid/NotFound/Error` |
| `Ardalis.Result.AspNetCore` | `10.1.0` | Api | `.ToMinimalApiResult()` mapping |
| `Microsoft.EntityFrameworkCore.SqlServer` | `9.0.0` | Infrastructure | MSSQL provider (.NET 10 toolchain, EF Core 9 stable) |
| `Microsoft.EntityFrameworkCore.Design` | `9.0.0` | Infrastructure, Api | Design-time migrations |
| `Microsoft.EntityFrameworkCore.Tools` | `9.0.0` | Api | `dotnet ef` host |
| `Microsoft.Extensions.Http.Resilience` | `9.0.0` | Infrastructure | Polly v8 wrapped (`AddStandardResilienceHandler`) |
| `Polly` | `8.5.0` | Infrastructure | Low-level strategy (WS supervisor) |
| `Microsoft.Extensions.Hosting` | `9.0.0` | Infrastructure | `BackgroundService` tabani |
| `Microsoft.Extensions.Http` | `9.0.0` | Infrastructure | `IHttpClientFactory` |
| `Microsoft.Extensions.Options.DataAnnotations` | `9.0.0` | Infrastructure | `[Required]` validator kombine |
| `Serilog.AspNetCore` | `8.0.3` | Api | Request logging + structured sink |
| `Serilog.Sinks.Console` | `6.0.0` | Api | Console sink |
| `Serilog.Sinks.File` | `6.0.0` | Api | Rolling file sink (7 gun retention) |
| `Serilog.Enrichers.Environment` | `3.0.1` | Api | Machine/user enricher |
| `Swashbuckle.AspNetCore` | `7.2.0` | Api | OpenAPI + SwaggerUI (dev only) |
| `AspNetCore.HealthChecks.SqlServer` | `9.0.0` | Api | `/health/ready` DB check |
| `AspNetCore.HealthChecks.UI.Client` | `9.0.0` | Api | `UIResponseWriter` JSON format |
| `NetEscapades.AspNetCore.SecurityHeaders` | `0.24.0` | Api | CSP + HSTS — **baseline (MVP zorunlu)**; paket major <1 stabilite notu plan.md bilinen-bosluk'ta |

**Test projesi (tests/Tests):**

| Paket | Version | Rol |
|---|---|---|
| `Microsoft.NET.Test.Sdk` | `17.12.0` | Test harness |
| `xunit` | `2.9.2` | Test framework |
| `xunit.runner.visualstudio` | `2.8.2` | VS runner |
| `FluentAssertions` | `6.12.2` | Assertion DSL |
| `NSubstitute` | `5.3.0` | Port/interface mock |
| `Microsoft.AspNetCore.Mvc.Testing` | `9.0.0` | `WebApplicationFactory<T>` |
| `Testcontainers.MsSql` | `4.1.0` | MSSQL integration test |
| `Microsoft.EntityFrameworkCore.InMemory` | `9.0.0` | Handler-level test icin fast in-memory DB |

**Direktif:** `Directory.Packages.props` + `ManagePackageVersionsCentrally=true` ile merkezi versiyon — her csproj sadece `<PackageReference Include="..." />`. Reviewer bunu zorlar.

---

## 3. Otomatik Migration Startup Hook (ZORUNLU)

[ADR 0001](./adr/0001-auto-migration-on-startup.md) geregi uygulama boot oldugunda `Database.MigrateAsync()` cagrilir; migration hata verirse process exit code 1 ile iner. Sebep: "DB-drift'e iki ayri deployment path izin verme" karari.

Asagidaki snippet `src/Api/Program.cs`'in `app.RunAsync()` oncesi kisminin **tam hali**. Copy-paste hazir; degisim reviewer icin goz onunde:

```csharp
// src/Api/Program.cs
using BinanceBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ... bkz. bolum 8 (Configuration) ve 9 (Logging) — Serilog, DI, Options burada eklenir.

var app = builder.Build();

// --- Startup hook: otomatik migration (ADR 0001) ---
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        logger.LogInformation("Migration started.");
        await db.Database.MigrateAsync();
        var applied = await db.Database.GetAppliedMigrationsAsync();
        logger.LogInformation(
            "Migration completed. AppliedMigrations={Count}",
            applied.Count());
    }
    catch (Exception ex)
    {
        logger.LogCritical(ex, "Migration failed; application boot aborted.");
        return 1; // boot failure — orchestrator restart policy devreye girer
    }
}
// -------------------------------------------------

// endpoints, middleware — bkz. bolum 11
await app.RunAsync();
return 0;
```

**Not:** `Program.cs`'in dosya duzeyinde `return` yapabilmesi icin `Main` kaynakli template yerine **top-level statements** kullanilir (dotnet 10 default `webapi`). Exit code 1 ile donus orchestrator'un (Docker/K8s/systemd) restart policy'sini tetikler. Integration test'lerde `WebApplicationFactory<T>`'in `InMemory` DB kullandigi path'te bu hook'tan kacmak icin `builder.Environment.IsEnvironment("IntegrationTest")` guard'i eklenir; integration test kurulumu MSSQL Testcontainer ile calisirsa hook aynen calisir.

**Ek disiplin (ADR 0001):**
- `Database.EnsureCreatedAsync()` YASAK — migration mekanizmasini by-pass eder.
- Migration adlandirma: `dotnet ef migrations add <YYYYMMDDHHmm>_<ChangeName> -p src/Infrastructure -s src/Api`.
- Migration rollback prod'da yasak; forward-only. Gerekirse yeni migration ile fix. MVP'de rollback YOK; geri donus icin DB drop + fresh migrate (production olmadigindan kabul).

---

## 4. Binance REST Client

### 4.1 Named HttpClient + AddStandardResilienceHandler

`IHttpClientFactory` ile **tek** named client `"binance-rest"`; typed client'lar `IHttpClientFactory.CreateClient("binance-rest")` uzerinden yaratilir ve paylasilir handler pipeline'indan gecer. `AddStandardResilienceHandler` Polly v8'in retry + circuit breaker + timeout + rate limiter katmanlarini saran opinionated preset; parametreler [binance-research.md §1.2](./research/binance-research.md) rate limit ve 429/418 davranis gercegine gore overridelanir.

Snippet (Infrastructure DI — `AddInfrastructure` extension icinde — SADECE config ornegi, feature kodu degil):

```csharp
services
    .AddHttpClient("binance-rest", (sp, client) =>
    {
        var opts = sp.GetRequiredService<IOptions<BinanceOptions>>().Value;
        client.BaseAddress = new Uri(opts.RestBaseUrl);
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("BinanceBot/1.0");
    })
    .AddHttpMessageHandler<ClockOffsetHandler>()
    .AddHttpMessageHandler<HmacSignatureDelegatingHandler>()
    .AddHttpMessageHandler<RetryAfterAwareHandler>()
    .AddHttpMessageHandler<WeightTrackerHandler>()
    .AddStandardResilienceHandler(options =>
    {
        options.Retry.MaxRetryAttempts = 4;
        options.Retry.BackoffType = DelayBackoffType.Exponential;
        options.Retry.UseJitter = true;
        options.Retry.Delay = TimeSpan.FromMilliseconds(500);

        options.CircuitBreaker.MinimumThroughput = 10;
        options.CircuitBreaker.FailureRatio = 0.5;
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
        options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(10);

        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(30);
    });
```

### 4.2 Delegating Handler Zinciri (Outer -> Inner)

Handler zinciri `AddHttpMessageHandler` cagri sirasina gore dis -> ic (ilk eklenen disa yakin). Binance icin guvenli sira:

1. `ClockOffsetHandler` — en dista, `timestamp` query parametresini enjekte eder (imza hesaplamadan once).
2. `HmacSignatureDelegatingHandler` — payload'i (query + body + `timestamp`) HMAC-SHA256 ile imzalar; `signature` param + `X-MBX-APIKEY` header ekler.
3. `RetryAfterAwareHandler` — 429/418 yanitinda `Retry-After` header'i okur ve `DelayHint` property'sine yazar; `AddStandardResilienceHandler`'in retry strategy'si bu hint'e gore `OnRetry` delay'ini uzatir.
4. `WeightTrackerHandler` — en icte, response header'larini okur: `X-MBX-USED-WEIGHT-1M`, `X-MBX-ORDER-COUNT-10S`, `X-MBX-ORDER-COUNT-1D`. `ILogger<WeightTrackerHandler>` ile structured log; esik (kullanim > %80) asilinca `IBinanceWeightBudget` servisine sinyal verir.

[binance-research.md §1.2](./research/binance-research.md): 429 ignore edilirse 418 -> IP ban 2 dakikadan 3 gune kadar. Bu yuzden `RetryAfterAwareHandler` 429/418'i `ValidateResponse` ile transient kabul eder ama 418 durumunda retry sayisi 1'e kapsar + alarm.

### 4.3 Clock Sync

Boot'ta `GET /api/v3/time` -> `serverTime - localTime = offset`. `IBinanceTimeSource` bu offset'i thread-safe tutar (`Interlocked.Exchange`). `ClockSyncWorker` (bkz. bolum 12) saatte 1 refresh; drift > 2s -> alarm. `HmacSignatureDelegatingHandler` her imzali istekte `timestamp = DateTimeOffset.UtcNow.AddMilliseconds(-offset).ToUnixTimeMilliseconds()`; `recvWindow=5000`.

### 4.4 Weight Tracker + Throttle

`BinanceWeightBudget`:
- Token-bucket (IP: 6000 weight/min — Binance default; UID order: 100/10s, 200000/day).
- `TryConsume(weight, cancellationToken)` async; token yoksa queue ve bekler veya `Result.Error("rate-limited")` ile caller'a dondurur.
- Her REST cagrisindan once kabul edilmis weight otomatik dusurulur; response'taki gercek `X-MBX-USED-WEIGHT-1M` ile correction.
- Esik 1200/min `X-MBX-USED-WEIGHT-1M` → soft pause + alarm.

### 4.5 Typed Client'lar — Segregated Interface Pattern (ISP)

Infrastructure tarafinda 3 ayri sinif; Application tarafina 3 ayri segregated interface expose edilir. **"Tek super-interface" anti-patterni YOK** (reviewer blocker B1 cozumu):

- **`IBinanceMarketData`**: `GetKlinesAsync`, `GetDepthSnapshotAsync`, `GetExchangeInfoAsync`, `GetServerTimeAsync`, `Get24hrTickerAsync`. Unsigned endpoint'ler agirlikli.
- **`IBinanceTrading`**: `PlaceOrderAsync`, `TestOrderAsync`, `CancelOrderAsync`, `GetOrderAsync`, `GetAllOrdersAsync`, `CreateListenKeyAsync`, `KeepaliveListenKeyAsync`, `CloseListenKeyAsync`. Signed endpoint'ler.
- **`IBinanceAccount`**: `GetAccountAsync`, `GetMyTradesAsync`. Signed.

**Kural:** Her handler ctor'da sadece ihtiyac duydugu interface'i alir. Ornekler:
- `PlaceOrderCommandHandler(IBinanceTrading trading, ...)` — `IBinanceMarketData` almaz.
- `GetLatestKlinesQueryHandler(IBinanceMarketData market, ...)` — `IBinanceTrading` almaz.
- `GetAccountQueryHandler(IBinanceAccount account, ...)` — digerlerini almaz.

Gerekce: test izolasyonu + method grubunun signed/unsigned ayrimi + ISP (handler ilgisiz surface'i gormez). Uc Infrastructure sinifi (`BinanceMarketDataClient`, `BinanceTradingClient`, `BinanceAccountClient`) paylasilan named HttpClient `"binance-rest"` uzerinden calisir.

---

## 5. WebSocket Supervisor

[ADR 0002](./adr/0002-binance-ws-supervisor-pattern.md) temel aliyor; [binance-research.md §2.3](./research/binance-research.md) limitlerine uyumlu. WS akisi producer/consumer; producer `ClientWebSocket`, consumer handler `Channel<WsEvent>`.

### 5.1 Sinif ve Component Haritasi

| Sinif | Sorumluluk |
|---|---|
| `BinanceWsSupervisor : BackgroundService` | Supervisor loop; reconnect state machine; `Channel<WsEvent>` sahibi |
| `BinanceWsConnection` | Tek `ClientWebSocket` wrapper; send/receive; close frame |
| `HeartbeatManager` | 20s server ping / 60s pong timeout; Binance sunucusunun ping'ine `ClientWebSocket` framework pong'u otomatik gonderir, **bot tarafi ek ping gondermez** — `ClientWebSocketOptions.KeepAliveInterval = TimeSpan.Zero` (client-initiated ping kapali) + local 75s watchdog |
| `SubscriptionPlanner` | Aktif Instrument/Strategy'den `streams` listesi uretir (combined URL kurar) |
| `DepthBookSynchronizer` | Snapshot + diff resync (binance-research.md §2.4) |
| `UserDataStreamKeepalive` | 25dk'da bir `PUT /api/v3/userDataStream?listenKey=...` |
| `UserDataStreamReconnector` | Reconnect: disconnect → new listenKey (POST) → subscribe → 25dk keepalive restart (C2 nit cozumu) |
| `WsEventDispatcher` | Channel consumer; stream adina gore `IMediator.Send` cagrisina map eder (`<s>@kline_1m` -> `IngestKlineCommand` vs.) |

### 5.2 Channel Konfigurasyonu

```csharp
var channelOptions = new BoundedChannelOptions(capacity: 10_000)
{
    FullMode = BoundedChannelFullMode.DropOldest,
    SingleReader = false,        // birden cok worker tuketebilir (dispatcher)
    SingleWriter = true          // tek supervisor yaziyor
};
var channel = Channel.CreateBounded<WsEvent>(channelOptions);
```

`DropOldest` secimi: piyasa verisi "en taze en degerli"; handler yavaslarsa en yasli event atilir, handler catch-up edemez. Kline aggregate `Closed==true` event'inin kaybi riski yuksekse (strateji tetiklemez), drop olayi `IDroppedEventMetric` ile sayilir ve alarm uretir.

### 5.3 Reconnect State Machine

```
                       +---------------+
                       |   Connect     |
                       +-------+-------+
                               |
                        connect OK
                               v
                       +---------------+
          +----------> | Subscribed    | <---------------+
          |            +-------+-------+                 |
          |    replay kline     |                        |
          |    warmup + depth   |                        |
          |    resync           |                        |
          |                     v                        |
          |            +---------------+                 |
          |  (planned) |     Pump      | (unplanned)     |
          |  23h near  +-+-----+-----+-+ ping miss/60s   |
          |            | |           | | no pong /       |
          |            | |           | | disconnect      |
          |            v |           | v                 |
          |    +---------+-+       +-+---------+         |
          |    | GracefulHot|       |  Backoff |         |
          |    | Swap (23h) |       +-----+----+         |
          |    +------+-----+             |              |
          |           |             exp + jitter         |
          |           |             (1,2,4,8,16,30)      |
          +-----------+--------------------+-------------+
```

**Transition tipleri (G2 nit cozumu — planned vs unplanned ayri):**
- **Planned:** `Pump -> GracefulHotSwap (23h preemptive)` — yeni baglanti acilir, subscribe olunca eski `WebSocketCloseStatus.NormalClosure` ile kapatilir (hot swap, slot gap yok).
- **Unplanned:** `Pump -> Backoff (ping miss / 60s no pong / network disconnect)` — Polly backoff ile geri don.

- Backoff: exponential `1s -> 30s cap`, `±20%` jitter.
- **Heartbeat**: sunucu 20s'de ping eder, `ClientWebSocket` framework pong'u otomatik verir. 60s pong yoksa -> server baglantiyi kapatir; biz `ReceiveAsync` `WebSocketState` degisimini yakalariz -> reconnect. Guvence icin local 75s timer da kosar; 75s iclerisinde hicbir frame gelmezse supervisor manuel close + reconnect. **Not (C1 nit):** `ClientWebSocketOptions.KeepAliveInterval = TimeSpan.Zero` — client-initiated ping gereksiz, server ping + framework pong yeter.

### 5.4 Depth Resync Akisi

[binance-research.md §2.4](./research/binance-research.md) algoritmasi aynen uygulanir:

1. Supervisor `<symbol>@depth@100ms` stream'e abone; her event'i buffer'a yazar + ilk event'in `U` degerini kaydeder.
2. `IBinanceMarketData.GetDepthSnapshotAsync(symbol, limit=5000)` cagir; `lastUpdateId` eline gelir.
3. `lastUpdateId < firstBufferedU` ise adim 1'e don (yeniden event bekle).
4. Buffer'da `u <= lastUpdateId` olan event'leri at.
5. Ilk kalan event'in `U <= lastUpdateId+1 <= u` olmali; degilse resync.
6. `Depth.ApplySnapshot(...)` -> seed; her event icin `Depth.ApplyDiff(U, u, ...)` cagir.
7. Sonraki event `U != prev_u + 1` ise `DepthGapDetectedEvent` raise; supervisor'un `DepthResyncHandler`'i 1'e donduren komut yayinlar.

`DepthBookSynchronizer` bu state'i sembol basina tutar (`ConcurrentDictionary<Symbol, DepthSyncState>`); supervisor birden fazla sembolde paralel calisabilir.

### 5.5 User Data Stream + Reconnect Disiplini (C2 Cozumu)

**Ilk baglanti:**
1. Trading yetkisi olan boot'ta `POST /api/v3/userDataStream` -> `listenKey` alinir.
2. `wss://.../<listenKey>` ayri `BinanceWsConnection` ile dinlenir (public stream'den ayri endpoint).
3. `UserDataStreamKeepalive` (PeriodicTimer-backed BackgroundService) **25dk**'da bir `PUT` (30dk expire 5dk buffer).

**Reconnect sonrasi (C2 nit — eksikligi cozuldu):**
1. Disconnect tespiti (ping miss, unplanned transition, preemptive 23h).
2. `UserDataStreamReconnector`: eski `listenKey` artik gecersiz olabilir. **Yeni** `POST /api/v3/userDataStream` cagir -> yeni `listenKey`.
3. Yeni `wss://.../<newListenKey>` endpoint'ine abone ol.
4. `UserDataStreamKeepalive` 25dk sayacini sifirla.
5. Replay: Arada kacmis `executionReport`/`outboundAccountPosition` event'leri REST fallback ile yakalamak icin `GET /api/v3/openOrders` + `GET /api/v3/account` cagrilir; local state ile reconcile.

**Event handler'lar:**
- `executionReport` -> `RegisterOrderFillCommand`.
- `outboundAccountPosition` -> bakiye snapshot cache (Position aggregate icin mark-to-market yardimcisi).
- `balanceUpdate` -> asset bakiye cache.
- `listStatus` -> OCO/OTO lifecycle.

### 5.6 Graceful Shutdown

Supervisor `stoppingToken` cancellation oldugunda:

1. Yeni istek kabul etmez; `channel.Writer.TryComplete()`.
2. `BinanceWsConnection.CloseAsync(WebSocketCloseStatus.NormalClosure, "shutdown", ...)`.
3. Channel drain: dispatcher reader loop `channel.Reader.Completion` await eder.
4. `Dispose` -> `ClientWebSocket.Dispose`.

### 5.7 Handler Idempotency

Her aggregate icin [ADR 0003](./adr/0003-idempotent-handler-discipline.md) kurali:

- Kline: UNIQUE `(Symbol, Interval, OpenTime)` + upsert; mukerrer event noop.
- Trade: UNIQUE `(Symbol, TradeId)`.
- Order fill: UNIQUE `(OrderId, ExchangeTradeId)`.
- Depth: `lastUpdateId` monoton; `U/u` validation.

Handler `Result.Success()` doner (noop), dedup edilmis event icin bile exception atmaz.

---

## 6. CQRS Slice Haritasi (Ornek Pattern)

Tam ~47 slice listesi [architecture-notes.md §3](./architecture-notes.md) tablosunda. Burada pattern'i **5 representative slice** uzerinden ornekliyoruz — kalan slice'lar ayni sablonu takip eder.

### 6.1 MarketData: `IngestKlineCommand`

**Dosyalar:**
- `src/Application/MarketData/Klines/Commands/IngestKline/IngestKlineCommand.cs`
- `src/Application/MarketData/Klines/Commands/IngestKline/IngestKlineCommandHandler.cs`
- `src/Application/MarketData/Klines/Commands/IngestKline/IngestKlineCommandValidator.cs`

**Record signature:**
```csharp
public sealed record IngestKlineCommand(
    Symbol Symbol,
    KlineInterval Interval,
    DateTimeOffset OpenTime,
    DateTimeOffset CloseTime,
    OhlcvValues Ohlcv,
    int TradeCount,
    bool Closed) : IRequest<Result>;
```

**Handler bagimliliklari:** `IApplicationDbContext db`, `IMediator mediator` (domain event dispatch icin), `ILogger<IngestKlineCommandHandler> logger`, `IClock clock`.

**Validator kurallari (FluentValidation):**
- `Symbol` non-empty, 3-12 karakter, uppercase.
- `Interval` defined enum value.
- `OpenTime < CloseTime`.
- `Ohlcv.High >= Max(Open, Close) && Ohlcv.Low <= Min(Open, Close)`.
- `Ohlcv.Volume >= 0`.
- `TradeCount >= 0`.

**Result<T> mapping:**
- Ok insert/upsert -> `Result.Success()` (HTTP 200 ama endpoint internal; WS supervisor caller'i dondur).
- Validator fail -> `Result.Invalid(IEnumerable<ValidationError>)` (HTTP 400 esdegeri — internal log).
- DbUpdateException (unique violation disi) -> `Result.Error("...")` (programmer error, circuit alarm).

### 6.2 Order: `PlaceOrderCommand`

**Dosyalar:**
- `src/Application/Orders/Commands/PlaceOrder/PlaceOrderCommand.cs`
- `src/Application/Orders/Commands/PlaceOrder/PlaceOrderCommandHandler.cs`
- `src/Application/Orders/Commands/PlaceOrder/PlaceOrderCommandValidator.cs`
- `src/Application/Orders/Commands/PlaceOrder/PlacedOrderDto.cs`

**Record signature:**
```csharp
public sealed record PlaceOrderCommand(
    Symbol Symbol,
    OrderSide Side,
    OrderType Type,
    Quantity Qty,
    Price? LimitPrice,
    Price? StopPrice,
    TimeInForce Tif,
    StrategyId? StrategyId) : IRequest<Result<PlacedOrderDto>>;
```

**Handler bagimliliklari:** `IApplicationDbContext db`, **`IBinanceTrading trading`** (segregated port — `IBinanceMarketData` veya `IBinanceAccount` almaz; B1 cozumu), `ISymbolFiltersCache filters`, `IRiskProfileGate risk`, `IClock clock`, `ILogger<PlaceOrderCommandHandler> logger`, `IPublisher events`.

**Validator kurallari:**
- Symbol var (`filters.TryGet(Symbol, out var f)` yoksa `Invalid`).
- `Qty` stepSize'in tam kati (`Qty.Value % f.StepSize == 0`, round-down applied upstream preferred).
- `LimitPrice` (LIMIT/LIMIT_MAKER type'inda zorunlu) `tickSize`'in tam kati.
- `MIN_NOTIONAL`: `(LimitPrice ?? lastTradePrice) * Qty >= f.MinNotional`.
- `PERCENT_PRICE_BY_SIDE` check (weighted avg relative).
- LIMIT / STOP_LOSS_LIMIT / TAKE_PROFIT_LIMIT icin `Tif != null`.
- `RiskProfileGate.Allow(...)` `false` donerse `Invalid("Risk limit breach: ...")` (ADR 0005 — bkz. bolum 7).

**Result<T> mapping:**
- Success -> `Result<PlacedOrderDto>.Success(dto)` -> HTTP 201 + Location header.
- Validator -> `Invalid` -> HTTP 400.
- Symbol halt veya not found -> `NotFound` -> HTTP 404.
- Binance reject (error code -2010) -> `Result.Conflict("Exchange rejected: ...")` -> HTTP 409.
- Network/unknown -> `Error` -> HTTP 500 (alarm).

### 6.3 Position: `ListPositionsQuery` (F3 supersede)

**Dosyalar:**
- `src/Application/Positions/Queries/ListPositions/ListPositionsQuery.cs`
- `src/Application/Positions/Queries/ListPositions/ListPositionsQueryHandler.cs`
- `src/Application/Positions/Queries/ListPositions/PositionDto.cs`

**Record signature:**
```csharp
public sealed record ListPositionsQuery(
    PositionStatus Status,
    Symbol? Symbol,
    DateTimeOffset? From,
    DateTimeOffset? To) : IRequest<Result<IReadOnlyList<PositionDto>>>;
```

**Handler bagimliliklari:** `IReadDbContext readDb` (AsNoTracking projection), `IClock clock`, `ILogger<ListPositionsQueryHandler> logger`.

**Validator:** Status defined enum; From<To (ikisi de varsa).

**Projection:** `readDb.Positions.AsNoTracking().Where(p => p.Status == query.Status)` + Symbol/From/To filtreli. `UnrealizedPnl` read-model cache (mark-to-market debounce 1s).

**Result mapping:** Always `Result.Success(list)` -> HTTP 200. Bos liste de success.

### 6.4 Strategy: `ActivateStrategyCommand`

**Dosyalar:** `src/Application/Strategies/Commands/ActivateStrategy/`

**Record signature:**
```csharp
public sealed record ActivateStrategyCommand(StrategyId Id) : IRequest<Result>;
```

**Handler bagimliliklari:** `IApplicationDbContext db`, `IRiskProfileReader risk`, `ILogger<ActivateStrategyCommandHandler> logger`.

**Validator:**
- `Id` not empty ULID.

**Handler akisi:**
1. `strategy = await db.Strategies.FindAsync(Id)` -> null ise `Result.NotFound()`.
2. `risk.GetCircuitBreakerStatus()` -> `Tripped` ise `Result.Invalid("Circuit breaker tripped")`.
3. `strategy.Activate()` -> `Result` doner; handler dogrudan return.
4. Domain event `StrategyActivatedEvent` raise; `SaveChangesAsync`; interceptor MediatR publish.

**Result mapping:**
- Success -> HTTP 204.
- NotFound -> 404.
- Invalid -> 400.

### 6.5 RiskProfile: `RecordTradeOutcomeCommand`

**Dosyalar:** `src/Application/RiskProfiles/Commands/RecordTradeOutcome/`

**Record signature:**
```csharp
public sealed record RecordTradeOutcomeCommand(decimal Pnl) : IRequest<Result>;
```

**Handler bagimliliklari:** `IApplicationDbContext db`, `ILogger<RecordTradeOutcomeCommandHandler> logger`.

**Validator:**
- `Pnl` finite decimal (NaN/Infinity yasak — decimal olduğu için zaten zorunlu).

**Handler akisi:**
1. `risk = await db.RiskProfiles.SingleAsync()` (singleton row id=1).
2. `risk.RecordTradeOutcome(Pnl)` -> invariantlar kontrol edilir, gerekirse `TripCircuitBreaker` event'i raise.
3. `SaveChangesAsync`.
4. `Result.Success()`.

**Result mapping:** Success -> HTTP 204 (veya internal-only; `PositionClosedEvent` handler'indan cagrilir).

### 6.6 Kalan Slice'lar Genel Pattern'i

Her slice ayni 3 dosyali yapi: `<Action>Command.cs` + `<Action>CommandHandler.cs` + `<Action>CommandValidator.cs`. Query slice'lari icin `<Name>Query.cs` + `<Name>QueryHandler.cs` + (opsiyonel) `<Name>QueryValidator.cs` + `<Dto>.cs`. architecture-notes.md §3'teki tablo her slice icin:

- File path (klasor ismi = slice action adi)
- Record signature
- Validator kurallari (ozet)
- Auth gereksinimi (`Anon` / `User` / `Admin` / `Internal`)

birebir aktardigimiz pattern'dir.

---

## 7. Result<T> Disiplini

`Ardalis.Result<T>` tum Application ve Domain public API'lerinde tek return tipi. `Result<T>`'nin built-in state'leri: `Ok`, `Invalid`, `NotFound`, `Error`, `Conflict`, `Unauthorized`, `Forbidden`, `CriticalError`.

### 7.1 Kullanim Kurallari

- **Handler kontrol akisi icin exception atmaz.** "Order bulunamadi" -> `Result<OrderDto>.NotFound()`. Exception sadece programmer error'larda (ornek: `ArgumentNullException` iceride, handler'in dogru cagrilmamasi).
- **Validator fail = `Result.Invalid`.** MediatR `ValidationBehavior` pipeline'i FluentValidation'i tetikler; validation fail durumunda handler'a **hic gidilmez**, direkt `Result.Invalid(List<ValidationError>)` dondurulur.
- **Domain method'lari da Result doner.** `Order.RecordFill(fill)` -> `Result` (qty asimi vs. invariant). Aggregate root exception atmaz.
- **Handler dis servis cagirirken:**
  - Binance 4xx -> `Result.Invalid` veya `Result.Conflict`.
  - Binance 5xx / network -> `Result.Error("...")` + ILogger.LogError.
  - Timeout -> `Result.Error`.
- **`Result.Success` void icin bile donulur** — handler signature'inda `Task<Result>` (non-generic) kullan.

### 7.2 Pipeline Behavior

`src/Application/Common/Behaviors/ValidationBehavior.cs`:

```csharp
public sealed class ValidationBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
    where TResponse : IResult
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken ct)
    {
        // 1. Tum IValidator<TRequest> instance'larini cagir.
        // 2. Failure varsa Result.Invalid(List<ValidationError>) olustur, TResponse'a reflection/generic factory ile doldur.
        // 3. Validator temizse next() cagir.
    }
}
```

Ayrica `LoggingBehavior` (request baslat/bitir log), `UnhandledExceptionBehavior` (son safety net) pipeline'a eklenir. `MediatRServiceConfiguration.AddBehavior<...>` sirasi: Logging -> Validation -> UnhandledException -> Handler.

### 7.3 HTTP Mapping

Api katmaninda Minimal API endpoint'leri `result.ToMinimalApiResult()` (Ardalis.Result.AspNetCore) ile `IResult` donusur. Default mapping:

| Result state | HTTP |
|---|---|
| `Ok` | 200 / 204 (body yoksa) |
| `Invalid` | 400 + `ValidationError[]` body |
| `NotFound` | 404 |
| `Conflict` | 409 |
| `Unauthorized` | 401 |
| `Forbidden` | 403 |
| `Error` | 500 + generic message (detay log'da) |
| `CriticalError` | 500 + alarm |

Custom mapping gerekirse `ResultMappingExtensions.cs` + per-endpoint translator.

### 7.4 Domain Event + Result

Aggregate method'u `Result` dondurse bile domain event'i `ok` branch'te `RaiseDomainEvent(...)` cagirir; fail branch'te event yok. Handler `SaveChangesAsync` oncesi interceptor event'leri toplar, commit sonrasi MediatR publish eder. Event publish'i fail ederse Result.Error donderilmez (MVP; transactional outbox Faz-2 ADR-0008).

---

## 8. Configuration

### 8.1 `appsettings.json` Template (Api projesinde)

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.EntityFrameworkCore.Database.Command": "Warning"
    }
  },
  "AllowedHosts": "localhost;127.0.0.1",
  "Cors": {
    "Origins": [ "http://localhost:5000", "https://localhost:5001" ]
  },
  "ConnectionStrings": {
    "Default": ""
  },
  "Binance": {
    "RestBaseUrl": "https://testnet.binance.vision",
    "WsBaseUrl": "wss://stream.testnet.binance.vision",
    "ApiKey": "",
    "ApiSecret": "",
    "AdminApiKey": "",
    "AllowMainnet": false,
    "RecvWindowMs": 5000,
    "ClockSyncIntervalMinutes": 60,
    "ListenKeyKeepaliveMinutes": 25,
    "PreemptiveReconnectAfterHours": 23,
    "PongTimeoutSeconds": 75
  },
  "Risk": {
    "RiskPerTradePct": 0.01,
    "MaxPositionSizePct": 0.10,
    "MaxGrossExposurePct": 0.30,
    "MaxDrawdown24hPct": 0.05,
    "MaxDrawdownAllTimePct": 0.25,
    "MaxConsecutiveLosses": 3
  },
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": { "Microsoft": "Warning", "System": "Warning" }
    },
    "WriteTo": [
      { "Name": "Console" },
      {
        "Name": "File",
        "Args": {
          "path": "logs/app-.log",
          "rollingInterval": "Day",
          "retainedFileCountLimit": 7,
          "formatter": "Serilog.Formatting.Compact.CompactJsonFormatter, Serilog.Formatting.Compact"
        }
      }
    ]
  }
}
```

**Degisiklikler (A2 + G1 + D2 cozumu):**
- `ConnectionStrings.Default` **bos string** (A2). `ValidateOnStart` bos ise reddeder. Kaza ile master DB'ye bagli kalma riski yok.
- `Binance.WsBaseUrl = "wss://stream.testnet.binance.vision"` — **`:9443` portu silindi** (G1). Research + ADR-0006 §6.1 uyumlu (default 443).
- `AllowedHosts = "localhost;127.0.0.1"` — MVP localhost kisitli (D2).
- `Cors.Origins` explicit liste — wildcard YOK.
- `AdminApiKey` (ADR-0007) user-secrets/env var ile doldurulur.

Secret alanlari (`Binance.ApiKey`, `Binance.ApiSecret`, `Binance.AdminApiKey`, `ConnectionStrings.Default`) **bos** veya placeholder; git'e asla prod deger girmez. [ADR 0004](./adr/0004-secret-management.md) + [0006](./adr/0006-testnet-first-policy.md) + [0007](./adr/0007-admin-auth-model.md) zorlayici.

### 8.2 Secret Loading

`Program.cs` (snippet — DI kurulumunun baslangici):

```csharp
builder.Configuration.AddEnvironmentVariables(prefix: "BINANCEBOT_");
if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddUserSecrets<Program>(optional: true);
}
```

- **Dev:** `dotnet user-secrets init --project src/Api` + `dotnet user-secrets set "Binance:ApiKey" "..."` (+ `Binance:AdminApiKey`, `ConnectionStrings:Default`).
- **Prod:** environment variable `BINANCEBOT_Binance__ApiKey`, `BINANCEBOT_Binance__ApiSecret`, `BINANCEBOT_Binance__AdminApiKey`, `BINANCEBOT_ConnectionStrings__Default`. Key Vault ilerde `AddAzureKeyVault()`.

### 8.3 Options Pattern + Validation

```csharp
builder.Services
    .AddOptions<BinanceOptions>()
    .Bind(builder.Configuration.GetSection("Binance"))
    .ValidateDataAnnotations()
    .Validate(o => !string.IsNullOrWhiteSpace(o.ApiKey), "Binance:ApiKey missing.")
    .Validate(o => !string.IsNullOrWhiteSpace(o.ApiSecret), "Binance:ApiSecret missing.")
    .Validate(o => !string.IsNullOrWhiteSpace(o.AdminApiKey), "Binance:AdminApiKey missing (ADR-0007).")
    .Validate(o => o.AdminApiKey.Length >= 32, "Binance:AdminApiKey must be >= 32 chars.")
    .Validate(o => !o.AllowMainnet || !o.RestBaseUrl.Contains("testnet", StringComparison.OrdinalIgnoreCase),
              "AllowMainnet=true but RestBaseUrl points to testnet.")
    .Validate(o => o.AllowMainnet || o.RestBaseUrl.Contains("testnet", StringComparison.OrdinalIgnoreCase),
              "Testnet-first: AllowMainnet=false requires testnet URL.")
    .Validate(o => o.AllowMainnet || o.WsBaseUrl.Contains("testnet", StringComparison.OrdinalIgnoreCase),
              "Testnet-first: AllowMainnet=false requires testnet WS URL.")
    .ValidateOnStart();

builder.Services
    .AddOptions<RiskOptions>()
    .Bind(builder.Configuration.GetSection("Risk"))
    .Validate(o => o.RiskPerTradePct > 0 && o.RiskPerTradePct <= 0.02, "RiskPerTradePct must be in (0, 0.02].")
    .Validate(o => o.MaxPositionSizePct > 0 && o.MaxPositionSizePct <= 0.20, "MaxPositionSizePct in (0, 0.20].")
    .ValidateOnStart();

// Connection string validation (A2 cozumu)
var cs = builder.Configuration.GetConnectionString("Default");
if (string.IsNullOrWhiteSpace(cs))
{
    throw new InvalidOperationException("ConnectionStrings:Default missing. Use user-secrets or env var.");
}
```

`ValidateOnStart()` boot-time fail-fast — validate fail -> `OptionsValidationException` -> Program.cs crash -> exit code 1 (ADR 0001 ile ayni ruh).

### 8.4 Testnet-First Guard

Ek `StartupBinanceEnvironmentGuard`:

```csharp
// Program.cs — Build'den sonra, migration oncesi
var binance = app.Services.GetRequiredService<IOptions<BinanceOptions>>().Value;
if (!binance.AllowMainnet)
{
    if (binance.ApiKey.Length < 20)
    {
        Log.Fatal("Testnet-first guard: ApiKey too short; suspicious.");
        return 1;
    }
    if (!binance.RestBaseUrl.Contains("testnet", StringComparison.OrdinalIgnoreCase))
    {
        Log.Fatal("Testnet-first guard: RestBaseUrl is not testnet but AllowMainnet=false.");
        return 1;
    }
}
```

[ADR 0006](./adr/0006-testnet-first-policy.md) detaylı validation checklist'i. Mainnet'e acma: config'de `AllowMainnet=true` + review + opsiyonel CI check + **ADR-0009** (JWT+CSRF; admin endpoint'ler production'da 403 oldugu icin — ADR-0007 §"nafile kapi").

---

## 9. Logging

### 9.1 Serilog Kurulumu

`Program.cs` en ustunde (builder yaratilmadan once):

```csharp
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .Enrich.WithMachineName()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog((ctx, sp, cfg) => cfg
        .ReadFrom.Configuration(ctx.Configuration)
        .ReadFrom.Services(sp)
        .Enrich.FromLogContext());
    // ... DI, options
    var app = builder.Build();
    app.UseSerilogRequestLogging(opts =>
    {
        opts.GetLevel = (httpCtx, elapsed, ex) =>
            ex != null ? LogEventLevel.Error :
            httpCtx.Response.StatusCode >= 500 ? LogEventLevel.Error :
            LogEventLevel.Information;
    });
    // ... migration hook (bolum 3)
    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Boot failure.");
    return 1;
}
finally { Log.CloseAndFlush(); }
return 0;
```

### 9.2 Correlation ID Middleware

Her HTTP istegi icin `X-Correlation-Id` header'i -> yoksa `Guid.NewGuid().ToString("N")` -> response'a yansit. LogContext'e push et:

```csharp
app.Use(async (ctx, next) =>
{
    var correlationId = ctx.Request.Headers["X-Correlation-Id"].FirstOrDefault()
                        ?? Guid.NewGuid().ToString("N");
    ctx.Response.Headers["X-Correlation-Id"] = correlationId;
    using (LogContext.PushProperty("CorrelationId", correlationId))
    {
        await next();
    }
});
```

WS supervisor + background worker'lar kendi CorrelationId'lerini uretir (her event loop'unda veya her event icin).

### 9.3 Scoped Logging

Binance REST cagrilari icin `WeightTrackerHandler` scope:

```csharp
using (logger.BeginScope(new Dictionary<string, object>
{
    ["Symbol"] = symbol,
    ["Endpoint"] = endpoint,
    ["Weight"] = weight
}))
{
    var response = await base.SendAsync(request, ct);
    logger.LogInformation("Binance REST ok. StatusCode={StatusCode} UsedWeight1m={UsedWeight} ResponseMs={ElapsedMs}",
        (int)response.StatusCode, usedWeight, sw.ElapsedMilliseconds);
    return response;
}
```

### 9.4 PII ve Secret Koruma

- **API key log'a yazilmaz.** `HmacSignatureDelegatingHandler` imza hesabi sirasinda gecici veriyi log'a yansitmaz; Serilog destructuring policy `[NotLogged]` attribute/marker kullanir.
- `Serilog.Policies.RedactingPolicy` custom destructuring — `ApiKey`, `ApiSecret`, `AdminApiKey`, `ConnectionString` property'leri `***` ile yer degistirir.
- HTTP request body log edilmez. Response body sadece hata durumunda kisaltilmis (max 1KB) log.

### 9.5 ILogger<T> Kurali

- Her handler/service'te constructor inject.
- **String concat yasak**, structured logging zorunlu: `logger.LogInformation("Order placed. Symbol={Symbol} Qty={Qty}", symbol, qty)`.
- Exception log: `logger.LogError(ex, "..." , args...)` — `ex` her zaman ilk argument.

### 9.6 SystemEvent Audit (F1 cozumu)

- Kritik is olaylari (OrderPlaced, OrderFilled, PositionClosed, CircuitBreakerTripped, CircuitBreakerReset, StrategyActivated, StrategyDeactivated, RiskOverride, PaperTradeCompleted, WsReconnect-planned/unplanned) Serilog'a ek olarak `SystemEvent` tablosuna yazilir.
- `ISystemEventWriter.Write(level, source, type, message, payload)` Application abstraction; `Infrastructure.Observability.SystemEventWriter` implement.
- Serilog dosya tail'i frontend tarafindan **okunmaz**. `TailSystemEventsQuery` → `SystemEvent` tablosu tek veri kaynagi.

---

## 10. Health Checks + Security Headers

`ASP.NET Core HealthChecks` + `AspNetCore.HealthChecks.SqlServer` + custom check'ler.

### 10.1 Registration

```csharp
builder.Services.AddHealthChecks()
    .AddSqlServer(
        connectionString: builder.Configuration.GetConnectionString("Default")!,
        healthQuery: "SELECT 1;",
        name: "mssql",
        failureStatus: HealthStatus.Unhealthy,
        tags: new[] { "ready", "db" })
    .AddCheck<WsSupervisorHealthCheck>(
        name: "ws-supervisor",
        failureStatus: HealthStatus.Degraded,
        tags: new[] { "ready", "ws" })
    .AddCheck<BinanceRestReachabilityCheck>(
        name: "binance-rest",
        failureStatus: HealthStatus.Degraded,
        tags: new[] { "ready", "rest" });
```

### 10.2 Endpoint Map

```csharp
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false // hicbir check'i kosma; process yasiyorsa 200
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});
```

### 10.3 Security Headers (CSP + HSTS) — MVP Zorunlu

`NetEscapades.AspNetCore.SecurityHeaders` baseline (A4 nit cozumu — "opsiyonel" degil):

```csharp
var policyCollection = new HeaderPolicyCollection()
    .AddDefaultSecurityHeaders()
    .AddContentSecurityPolicy(b => b
        .AddDefaultSrc().Self()
        .AddScriptSrc().Self().From("https://cdn.jsdelivr.net").From("https://unpkg.com")
        .AddStyleSrc().Self().UnsafeInline()
        .AddImgSrc().Self().Data()
        .AddConnectSrc().Self())
    .AddStrictTransportSecurity(maxAge: TimeSpan.FromDays(365), includeSubDomains: true, preload: false)
    .AddFrameOptionsDeny();

app.UseSecurityHeaders(policyCollection);
```

Paket major < 1 stabilite notu: plan.md bilinen-bosluk'ta kayitli; API kirilabilir ama temel CSP/HSTS degerleri burada net.

### 10.4 Custom Check Davranislari

- **`WsSupervisorHealthCheck`**: `IBinanceWsSupervisorStatus.LastEventAt` -> `DateTimeOffset.UtcNow - last > 60s` ise `Degraded`; `last > 5min` ise `Unhealthy`.
- **`BinanceRestReachabilityCheck`**: `IBinanceRestClient.LastSuccessfulRequestAt` -> son 5 dakikada hic basarili istek yoksa `Degraded`; 15 dakikada hic yoksa `Unhealthy`.
- **`mssql` (builtin)**: basit ping; baglanti koparsa `Unhealthy`.

`/health/live` endpoint'i orchestrator'un liveness probe'u icin; `/health/ready` readiness probe. Prometheus/metrics endpoint ilerde (NOT_IN_SCOPE MVP).

---

## 11. API Endpoint Envanteri

Minimal API tercih edildi. `MapGroup("/api")` altinda aggregate basina ayri dosya. Auth: `X-Admin-Key` header tabanli `ApiKeyAuthenticationHandler` (ADR-0007); normal `User` endpoint'leri MVP'de same-origin + localhost bind ile acik (production'a gecerken ADR-0009).

### 11.1 Endpoint Tablosu (Nihai — Blocker F1-F5 Cozumu)

| Method | Route | Request | Response | Auth | Command/Query |
|---|---|---|---|---|---|
| `GET` | `/api/klines` | `?symbol&interval&count` | `Result<IReadOnlyList<KlineDto>>` | `User` | `GetLatestKlinesQuery` |
| `GET` | `/api/depth` | `?symbol&depth` | `Result<DepthSnapshotDto>` | `User` | `GetDepthSnapshotQuery` |
| `GET` | `/api/ticker/book` | `?symbol` | `Result<BookTickerDto>` | `User` | `GetBookTickerQuery` |
| **`GET`** | **`/api/market/summary`** | `?symbols=BTCUSDT,ETHUSDT,BNBUSDT` | `Result<IReadOnlyList<MarketSummaryDto>>` | `User` | **`GetMarketSummaryQuery`** (F1) |
| `GET` | `/api/instruments` | — | `Result<IReadOnlyList<InstrumentDto>>` | `User` | `ListActiveSymbolsQuery` |
| `GET` | `/api/instruments/{symbol}/filters` | — | `Result<SymbolFiltersDto>` | `User` | `GetSymbolFiltersQuery` |
| `POST` | `/api/instruments/refresh` | — | `Result<int>` | `Admin` | `RefreshSymbolFiltersCommand` |
| `POST` | `/api/instruments/{symbol}/halt` | `{ "reason": "..." }` | `Result` | `Admin` | `HaltSymbolCommand` |
| `POST` | `/api/orders` | `PlaceOrderRequest` | `Result<PlacedOrderDto>` | `User` | `PlaceOrderCommand` |
| `DELETE` | `/api/orders/{clientOrderId}` | — | `Result` | `User` | `CancelOrderCommand` |
| `GET` | `/api/orders/{clientOrderId}` | — | `Result<OrderDto>` | `User` | `GetOrderByClientIdQuery` |
| `GET` | `/api/orders/open` | `?symbol` | `Result<IReadOnlyList<OrderDto>>` | `User` | `ListOpenOrdersQuery` |
| `GET` | `/api/orders/history` | `?symbol&from&to&skip&take` | `Result<PagedResult<OrderDto>>` | `User` | `ListOrderHistoryQuery` |
| **`GET`** | **`/api/positions`** (F3 genisletildi) | `?status=open|closed&symbol&from&to` | `Result<IReadOnlyList<PositionDto>>` | `User` | **`ListPositionsQuery`** |
| `GET` | `/api/positions/{symbol}/pnl` | — | `Result<PositionPnlDto>` | `User` | `GetPositionPnlQuery` |
| **`GET`** | **`/api/positions/pnl/today`** | — | `Result<TodayPnlDto>` | `User` | **`GetTodayPnlQuery`** (F1) |
| `POST` | `/api/positions/{symbol}/close` | `{ "reason": "..." }` | `Result<ClosedPositionDto>` | `User` | `ClosePositionCommand` |
| `POST` | `/api/strategies` | `CreateStrategyRequest` | `Result<StrategyIdDto>` | `Admin` | `CreateStrategyCommand` |
| `GET` | `/api/strategies` | `?status` | `Result<IReadOnlyList<StrategyDto>>` | `User` | `ListStrategiesQuery` |
| **`GET`** | **`/api/strategies/{id}`** | — | `Result<StrategyDetailDto>` | `User` | **`GetStrategyDetailQuery`** (F1) |
| `POST` | `/api/strategies/{id}/activate` | — | `Result` | `Admin` | `ActivateStrategyCommand` |
| `POST` | `/api/strategies/{id}/deactivate` | `{ "reason": "..." }` | `Result` | `Admin` | `DeactivateStrategyCommand` |
| `PUT` | `/api/strategies/{id}/parameters` | `StrategyParametersDto` | `Result` | `Admin` | `UpdateStrategyParametersCommand` |
| `GET` | `/api/strategies/{id}/signals` | `?from&to` | `Result<IReadOnlyList<StrategySignalDto>>` | `User` | `GetStrategySignalsQuery` |
| `GET` | `/api/risk/profile` | — | `Result<RiskProfileDto>` | `User` | `GetRiskProfileQuery` |
| `GET` | `/api/risk/circuit-breaker` | — | `Result<CircuitBreakerStatusDto>` | `User` | `GetCircuitBreakerStatusQuery` |
| `PUT` | `/api/risk/profile` | `UpdateRiskProfileRequest` | `Result` | `Admin` | `UpdateRiskProfileCommand` |
| **`POST`** | **`/api/risk/override-caps`** (F2 — rename) | `{ "riskPerTradeCap": 0.015, "maxPositionCap": 0.15, "adminNote": "..." }` | `Result` | `Admin` | `OverrideRiskCapsCommand` |
| `POST` | `/api/risk/circuit-breaker/reset` | `{ "adminNote": "..." }` | `Result` | `Admin` | `ResetCircuitBreakerCommand` |
| **`GET`** | **`/api/risk/drawdown-history`** | `?days=30` | `Result<IReadOnlyList<DrawdownPointDto>>` | `User` | **`GetDrawdownHistoryQuery`** (F1) |
| `POST` | `/api/backtests` | `StartBacktestRequest` | `Result<long>` | `Admin` | `StartBacktestCommand` |
| `GET` | `/api/backtests/{id}` | — | `Result<BacktestResultDto>` | `User` | `GetBacktestResultQuery` |
| `GET` | `/api/backtests` | `?strategyId&status&skip&take` | `Result<PagedResult<BacktestRunSummaryDto>>` | `User` | `ListBacktestRunsQuery` |
| **`GET`** | **`/api/system/status`** | — | `Result<SystemStatusDto>` | `User` | **`GetSystemStatusQuery`** (F1) |
| **`GET`** | **`/api/logs/tail`** | `?since&level&limit=200` | `Result<SystemEventTailDto>` | `User` | **`TailSystemEventsQuery`** (F1 — SystemEvent tablosu) |
| `GET` | `/health/live` | — | 200 | `Anon` | — |
| `GET` | `/health/ready` | — | 200 / 503 | `Anon` | — |

**Toplam User/Admin endpoint = 36; + 2 health = 38 endpoint.** Blocker F1 (6 hayali endpoint eklendi: market/summary, pnl/today, drawdown-history, logs/tail, system/status, strategies/{id}), F2 (`/risk/profile/override` → `/risk/override-caps` rename), F3 (positions ListPositions supersede).

**Internal-only komutlar** (HTTP endpoint'i yok; WS supervisor / notification handler / background worker tetikler):
- `IngestKlineCommand`, `RecordTradeCommand`, `SyncDepthSnapshotCommand` (WS supervisor).
- `ListSymbolCommand` (SymbolFiltersRefresher).
- `RegisterOrderFillCommand` (UserDataStream).
- `OpenPositionCommand`, `UpdatePositionCommand`, `MarkToMarketCommand` (OrderFilled/BookTicker notification handler'lari).
- `EmitStrategySignalCommand` (StrategyEvaluatorWorker).
- `RecordTradeOutcomeCommand` (PositionClosed notification handler).
- `RecordBacktestTradeCommand`, `CompleteBacktestCommand` (BacktestRunnerHandler worker).

### 11.2 Endpoint Pattern (Minimal API)

Ornek (`src/Api/Endpoints/OrderEndpoints.cs`):

```csharp
public static class OrderEndpoints
{
    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/orders")
                       .WithTags("Orders")
                       .RequireAuthorization(); // User policy default

        group.MapPost("/", async (PlaceOrderRequest req, IMediator mediator, CancellationToken ct) =>
        {
            var cmd = req.ToCommand();
            var result = await mediator.Send(cmd, ct);
            return result.ToMinimalApiResult();
        })
        .WithName("PlaceOrder")
        .Produces<PlacedOrderDto>(StatusCodes.Status201Created)
        .ProducesValidationProblem(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status409Conflict);

        // ... DELETE, GET, etc.
        return app;
    }
}
```

Auth policy `User` -> default `RequireAuthorization()`; `Admin` -> `.RequireAuthorization("AdminPolicy")` (ADR-0007 `X-Admin-Key` scheme); `Anon` endpoint'lerde `AllowAnonymous()`.

---

## 12. BackgroundService Listesi

| Servis | Sorumluluk | Tetikleyici | Frekans |
|---|---|---|---|
| `BinanceWsSupervisor` | WS stream tuketimi + reconnect state machine + channel pumping | Boot'ta start; `stoppingToken`'a kadar calisir | Continuous |
| `UserDataStreamKeepalive` | `listenKey` keepalive PUT | Periodic timer | 25 dakika (30dk expire buffer) |
| `UserDataStreamReconnector` | Disconnect sonrasi yeni listenKey + subscribe + keepalive restart | Event-driven (UserDataStream disconnect) | On-demand |
| `ClockSyncWorker` | `GET /api/v3/time` -> local clock offset refresh | Periodic timer | 60 dakika (drift > 2s alarm) |
| `SymbolFiltersRefresher` | `GET /api/v3/exchangeInfo` -> Instrument aggregate filter update | Periodic timer | 24 saat (gunde 1) + on-demand `RefreshSymbolFiltersCommand` |
| `StrategyEvaluatorWorker` | Aktif Strategy'leri KlineClosed event basina evaluate, `EmitStrategySignalCommand` yayinla | Event-driven (`KlineClosedEvent` notification handler `Channel<KlineClosedEvent>`'e push; worker tuketir) | Realtime (per closed bar) |
| `RiskCircuitBreakerMonitor` | RiskProfile MaxDrawdown24h / MaxDrawdownAllTime / ConsecutiveLosses threshold kontrolu | Periodic timer | 1 dakika |
| `DepthSnapshotBootstrapper` | Aktif sembol icin baslangic snapshot + diff resync kurulumu (supervisor icinde `DepthBookSynchronizer` ile konusur) | Boot'ta + `DepthGapDetectedEvent` handler | On-demand |
| `BacktestRunnerWorker` | `BacktestStartedEvent` tetikli; zamanı gelince `BacktestRun` evaluate + trade record + metrics | Event-driven (queue) | Run basina |

### 12.1 Event-Driven Worker Kalibi

`StrategyEvaluatorWorker` ornek pattern:

1. `INotificationHandler<KlineClosedEvent>` -> event'i bounded channel'a yaz.
2. `BackgroundService.ExecuteAsync` -> channel'i oku, her event icin:
   - Aktif Strategy'leri bul (`ListStrategiesQuery(Status=Active)`).
   - Her strategy icin `IStrategyEvaluator.Evaluate(strategy, klineContext)` cagir.
   - Sinyal dondurse `EmitStrategySignalCommand` yayinla.
3. Hata olursa: loglar + next event'e gecer (bir strateji hatasi digerlerini durduramaz).

### 12.2 Periodic Worker Kalibi

`ClockSyncWorker` ornek pattern:

1. `PeriodicTimer(TimeSpan.FromMinutes(60))`.
2. `while (await timer.WaitForNextTickAsync(stoppingToken))`:
   - `marketData.GetServerTimeAsync()` -> `Result<DateTimeOffset>` (segregated `IBinanceMarketData`).
   - Success -> `IBinanceTimeSource.UpdateOffset(serverTime - DateTimeOffset.UtcNow)`.
   - Error -> log + next tick (don't crash).
3. `stoppingToken` cancel -> exit.

### 12.3 Graceful Shutdown

Tum worker'lar `ExecuteAsync(CancellationToken stoppingToken)` override eder; `stoppingToken` cancel edilince:
1. `PeriodicTimer.Dispose()`.
2. Acik HTTP istegi varsa timeout ile kapat.
3. Channel writer complete.
4. `return` -> host shutdown tamamlanır.

---

## Notlar ve Yasaklar

- **Sadece tasarim.** Bu belgenin hicbir bolumu `src/**` veya `tests/**` kodu yaratmaz. `Program.cs` snippet'i backend-dev feature-0'a baslarken birebir kopya edilecek referans.
- **Lazy loading:** EF Core config'inde `UseLazyLoadingProxies()` cagrilmayacak; explicit `Include()` veya CQRS read-model.
- **Exception for control flow:** YASAK. `Result<T>` tek oyuncu. Programmer error istisnalari `UnhandledExceptionBehavior` pipeline behavior'i + `IExceptionHandler` middleware'i yakalar.
- **Repository-per-entity:** YASAK. Aggregate root basina tek repository (Order, Position, ...). Read-model query handler'lari dogrudan `IReadDbContext` + `AsNoTracking` projection yapar.
- **Database-First:** YASAK. Migration'lar `Infrastructure/Persistence/Migrations/` altinda code-first.
- **Hardcoded secret:** YASAK. Tum secret'lar user-secrets (dev) + environment variable (prod). `appsettings.json` template'inde placeholder/bos (A2 cozumu).
- **`new HttpClient()` / static HttpClient:** YASAK. `IHttpClientFactory` + named client.
- **`.Result` / `.Wait()` / `async void`:** YASAK.
- **Soft delete:** YASAK. Gerekirse yeni ADR.
- **Tek super-interface `IBinanceRestClient`:** YASAK (B1). 3 segregated: `IBinanceMarketData`, `IBinanceTrading`, `IBinanceAccount`.

---

## Kaynaklar

- [architecture-notes.md](./architecture-notes.md) — aggregate/layer/CQRS/event haritasi (girdiler burdan)
- [binance-research.md](./research/binance-research.md) — REST/WS/resilience kosullari
- [ADR 0001 — Auto Migration On Startup](./adr/0001-auto-migration-on-startup.md)
- [ADR 0002 — Binance WS Supervisor Pattern](./adr/0002-binance-ws-supervisor-pattern.md)
- [ADR 0003 — Idempotent Handler Discipline](./adr/0003-idempotent-handler-discipline.md)
- [ADR 0004 — Secret Management](./adr/0004-secret-management.md)
- [ADR 0005 — Risk Limit Policy](./adr/0005-risk-limit-policy.md)
- [ADR 0006 — Testnet-First Policy](./adr/0006-testnet-first-policy.md)
- [ADR 0007 — Admin Auth Model](./adr/0007-admin-auth-model.md)
- [Microsoft — HTTP resilience (Polly v8)](https://learn.microsoft.com/en-us/dotnet/core/resilience/http-resilience)
- [Microsoft — System.Threading.Channels](https://learn.microsoft.com/en-us/dotnet/standard/threading/channels)
- [Microsoft — Worker Services](https://learn.microsoft.com/en-us/dotnet/core/extensions/workers)
- [Microsoft — Options pattern](https://learn.microsoft.com/en-us/dotnet/core/extensions/options)
- [Ardalis.Result](https://github.com/ardalis/Result)
- [MediatR](https://github.com/jbogard/MediatR)
- [FluentValidation](https://docs.fluentvalidation.net/)
- [jasontaylordev/CleanArchitecture](https://github.com/jasontaylordev/CleanArchitecture)
- [ardalis/CleanArchitecture](https://github.com/ardalis/CleanArchitecture)
