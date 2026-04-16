# BinanceBot — Master Plan

**Tarih:** 2026-04-17
**Durum:** One-shot master plan adim 6/6 (architect final sentez). Reviewer 7 blocker + 14 nit raporu uygulandi; 7 blocker'in tamami kararlastirildi (ADR/patch/karar), 14 nit "Bilinen Bosluk + Faz-2 Kayit" bolumunde izlenir.
**Referanslar:** [research/binance-research.md](./research/binance-research.md), [architecture-notes.md](./architecture-notes.md), [backend-design.md](./backend-design.md), [frontend-design.md](./frontend-design.md), [plan-review-notes.md](./plan-review-notes.md), [adr/0001..0007](./adr/), [glossary.md](./glossary.md), [CLAUDE.md](../CLAUDE.md).
**Dil:** TR karar dili; teknik identifier + kod + link EN.

---

## 1. Baglam + Amac

### 1.1 Neden BinanceBot

BinanceBot; solo-dev'in Binance Spot uzerinde BTC/USDT, ETH/USDT, BNB/USDT sembollerini izleyen, araştırdığı stratejilerle testnet'te trading yapabilen, operasyonel saglik ve risk disiplinini her katmanda goz onunde tutan **kişisel trading bot + arastirma + AI workspace demo** projesidir. Uc amac ic ice:

- **Kişisel trading:** Testnet'te paper-trade + canli testnet order'lariyla grid, trend, mean-reversion stratejilerini dogrulamak; olgunlastığında production'a gecisi kasitli bir ADR (ADR-0009) engel aracina baglamak.
- **Arastirma:** Binance REST/WS sozlesmeleri, Polly resilience, DDD+Clean+CQRS uretim kodu, EF Core Code First migration + domain event fanout, Vue 3 CDN MPA arayuzu gibi modern .NET/web ekosisteminin production-grade kirpintilarini gercek bir is domain'i uzerinde pismek.
- **AI workspace demo:** 7 agent + 30 skill + 3 MCP server + hook loglama disiplinine sahip bir repo, insan-AI ortak gelistirme deneyimini hayata gecirir. Her adim `.ai-trace/decisions.jsonl` + `handoffs.jsonl` ile audit altinda.

### 1.2 Nihai Basari Metrigi

- **30 gun testnet kesintisiz data ingestion** (BTCUSDT/ETHUSDT/BNBUSDT 1m kline + bookTicker + depth; WS reconnect/replay disiplinli; bar kaybi ≤ %0.1).
- **3 strateji paper-trade sonuclari** (S5 sonunda):
  - Grid (range-bound BTC/USDT): Sharpe > 0.5, MaxDD ≤ %5.
  - Trend-following (EMA+ADX+ATR BTC/ETH): Win-rate > %30, R-ratio ≥ 1.5, MaxDD ≤ %20.
  - Mean-reversion (RSI+BB): Win-rate > %55, MaxDD ≤ %15.
- **S6 sonu 30 gun testnet live trade:** tum order'lar server-side `STOP_LOSS_LIMIT` ile korumali; gerceklesen MDD < %5; circuit breaker trip cagrilari 100% tetiklendi; SystemEvent audit 100% kayit.

### 1.3 Is Degeri

- Solo-dev is akisinda test-first + audit-first trading disiplinin oturmasi.
- Defansif risk yonetimi (RiskProfile + circuit breaker + pre-trade 5-adim gate + ADR-0006 testnet-first + ADR-0007 admin-auth kiosk) ile production'a vaktinden önce geçişi fiziksel olarak zorlastirmak.
- Her karar, her agent davranisi, her handoff tam audit trail — tekrar uretilebilir, retrospektife uygun.

**Kaynak kararlar:** ADR-0001..0007. `.ai-trace/decisions.jsonl` seq#TBD-log-gelince-guncel.

---

## 2. Domain Modeli

### 2.1 Aggregate Envanteri (9)

| # | Aggregate | Sorumluluk | Temel Davranislar | Domain Events |
|---|---|---|---|---|
| 1 | `Kline` | Bir sembol + interval + openTime icin OHLCV bar; `Closed==true` olunca immutable. | `Ingest`, `UpdateOngoing`, `MarkClosed` | `KlineIngested`, `KlineClosed` |
| 2 | `Trade` | Tekil alis-satis (insert-only append log); volume yuksek, TTL 30 gun. | `Record` (static factory) | `TradeRecorded` (opsiyonel; default off) |
| 3 | `Depth` | Symbol basina order book; snapshot+diff resync (U/u validation). | `ApplySnapshot`, `ApplyDiff` | `DepthSnapshotRefreshed`, `DepthGapDetected` |
| 4 | `Instrument` | Binance sembol metadata + filter'lar + listing state. | `List`, `UpdateFilters`, `Halt`, `Resume`, `Delist` | `SymbolListed`, `SymbolFiltersUpdated`, `SymbolHalted`, `SymbolDelisted` |
| 5 | `Order` | Binance lifecycle (New → PartiallyFilled → Filled/Canceled/Rejected); ClientOrderId UNIQUE idempotent. | `Place`, `MarkAcknowledged`, `RecordFill`, `Cancel`, `Reject` | `OrderPlaced`, `OrderAcknowledged`, `OrderPartiallyFilled`, `OrderFilled`, `OrderCanceled`, `OrderRejected` |
| 6 | `Position` | Symbol basina net pozisyon; open/closed; weighted avg entry; realized/unrealized PnL. | `Open`, `Increase`, `Reduce`, `Close`, `MarkToMarket` | `PositionOpened`, `PositionIncreased`, `PositionReduced`, `PositionClosed` |
| 7 | `Strategy` | Strateji config + status + signal emission; RiskProfile circuit breaker'a bagli activate. | `Create`, `Activate`, `Deactivate`, `UpdateParameters`, `EmitSignal` | `StrategyActivated`, `StrategyDeactivated`, `StrategyParametersUpdated`, `StrategySignalEmitted` |
| 8 | `RiskProfile` | Singleton (Id=1); risk caps + circuit breaker state machine; trade outcome kaydi. | `UpdateLimits`, `RecordTradeOutcome`, `TripCircuitBreaker`, `ResetCircuitBreaker`, `OverrideCaps` | `RiskLimitUpdated`, `RiskLimitBreached`, `CircuitBreakerTripped`, `CircuitBreakerReset` |
| 9 | `BacktestRun` | Tarihsel strateji simulasyonu; trade recording + metrics (Sharpe, MaxDD, WinRate). | `Schedule`, `RecordTrade`, `Complete`, `Fail` | `BacktestStarted`, `BacktestCompleted`, `BacktestFailed` |

### 2.2 Aggregate Disi Kayitlar

- **`BookTicker`** — **read-model** (aggregate degil). `dbo.vw_book_ticker_latest`; WS `@bookTicker` event'inden upsert. Domain davranisi yok; lifecycle yok. `GetBookTickerQuery` okur.
- **`SystemEvent`** — **audit tablosu** (aggregate degil). Append-only; `Infrastructure/Persistence/Tables/SystemEvent` POCO. `ISystemEventWriter` Application abstraction. `TailSystemEventsQuery` okur (`/api/logs/tail`). Blocker F4 gerekcesi: lifecycle yok, mutate yok, aggregate invariant yok, TTL farkli -> aggregate adayi degil.

### 2.3 Value Object Kataloğu

`Symbol`, `Price`, `Quantity`, `OhlcvValues`, `SymbolFilters`, `ClientOrderId` (ULID), `Percentage`, `DateRange`, `StrategyParameters` (JSON-backed, tip-safe extractor). Domain immutable; `Equals` field-by-field.

### 2.4 Ubiquitous Language

Tum isim kararlari [docs/glossary.md](./glossary.md)'ye kilitli. Yeni term dogarsa once glossary'ye, sonra koda.

**Kaynak kararlar:** ADR-0003 (idempotent handler), architecture-notes.md §1; `.ai-trace/decisions.jsonl` seq#TBD-log-gelince-guncel.

---

## 3. Clean Architecture Layer Haritasi

### 3.1 4 Layer Sorumluluk Tablosu

| Layer | Sorumluluk | Icerir | YASAK |
|---|---|---|---|
| **Domain** (`src/Domain`) | Aggregate + Entity + VO + DomainEvent + invariant kontrol + domain service port | 9 aggregate root, VO'lar, enum'lar, event record'lari, `IAggregateRoot`/`IClock`/`IStrategyEvaluator` marker | EF Core, MediatR, HttpClient, ASP.NET, FluentValidation, DI — **hicbir sey import etmez** (Ardalis.Result istisna). |
| **Application** (`src/Application`) | Use case orkestrasyonu: Command/Query handler, Validator, DTO, Port interface, `INotificationHandler` | MediatR handler + FluentValidation + port (`IApplicationDbContext`, `IBinanceMarketData`, `IBinanceTrading`, `IBinanceAccount`, `IBinanceWsSubscriber`, `IClock`, `IStopLossPlacer`, `ISystemEventWriter`), pipeline behaviors | Concrete DbContext, concrete HttpClient, `Microsoft.Data.SqlClient`, WebSocket kutuphaneleri |
| **Infrastructure** (`src/Infrastructure`) | Persistence + external API + adapter + BackgroundService | `ApplicationDbContext` + `IEntityTypeConfiguration`, `{Aggregate}Repository` (aggregate-per-repo), `BinanceMarketDataClient`/`BinanceTradingClient`/`BinanceAccountClient`, `BinanceWsSupervisor`, `UserDataStreamReconnector`, `SystemEventWriter`, Polly pipeline, migration'lar, seed | Domain'e ters bagimlilik; Api layer'a bagimli olmak |
| **Api** (`src/Api`) | HTTP endpoint + DI composition root + middleware + DTO marshalling + auth + logging + boot-time guards | Minimal API endpoint'leri, `Program.cs` DI + `app.Run()`, `StartupSecretValidator`, `BinanceEnvironmentGuard`, `ApiKeyAuthenticationHandler` (ADR-0007), CORS, `GlobalExceptionHandler`, Serilog, health checks | Domain'e direkt erisim; `DbContext`'i direkt kullanmak |

### 3.2 Dependency Rule + Proje Referans Grafigi

```
Api.csproj
  -> Application.csproj
  -> Infrastructure.csproj (composition root — DI kaydi icin direkt)
  -> Domain.csproj (transitif)

Application.csproj
  -> Domain.csproj

Infrastructure.csproj
  -> Application.csproj  (port interface'lerini implement eder)
  -> Domain.csproj
```

- Domain hicbir sey import etmez.
- Ok yonu ihlali reviewer tarafindan reddedilir.
- Api Infrastructure'a direkt referans tutar — Clean Architecture "Composition Root" kalibi.

### 3.3 Klasor Agaci Ozeti

```
src/
  Domain/          Common, MarketData/{Klines,Trades,Depths}, Instruments, Orders, Positions, Strategies, RiskProfiles, BacktestRuns, Shared, Abstractions
  Application/     Common/{Behaviors,Exceptions,Mappings,Models}, Abstractions/{Persistence,Binance,Observability,Services}, MarketData, Instruments, Orders, Positions, Strategies, RiskProfiles, BacktestRuns, System, Notifications
  Infrastructure/  Persistence/{Configurations,Tables,Interceptors,Migrations,Repositories,Seed}, Observability, Binance/{Rest,Ws}, Workers, Options, Logging, DependencyInjection
  Api/             Endpoints, Middleware, Authentication, Contracts, Configuration, Program.cs, appsettings.json
  Frontend/        js, css, pages, vendor (if offline)
tests/Tests/       Domain, Application, Infrastructure, Api, Fixtures
```

Detay: [backend-design.md §1.3](./backend-design.md), [frontend-design.md §6](./frontend-design.md).

**Kaynak kararlar:** jasontaylordev/CleanArchitecture + ardalis/CleanArchitecture iskeletleri; `.ai-trace/decisions.jsonl` seq#TBD-log-gelince-guncel.

---

## 4. CQRS Command/Query Envanteri (47 Slice)

Her slice MediatR `IRequest<Result>` / `IRequest<Result<TDto>>`. 3-dosya kalibi (`<Action>Command.cs` + `<Action>CommandHandler.cs` + `<Action>CommandValidator.cs`; DTO slice icinde). Validator pipeline behavior (`ValidationBehavior`) tetikler.

**Auth seviyeleri:** `Anon` (public), `User` (read-only API — MVP auth layer yok, localhost bind), `Admin` (`X-Admin-Key`, ADR-0007), `Internal` (WS supervisor/notification handler/worker).

### 4.1 Aggregate Bazli Slice Listesi

#### MarketData (7)
| Slice | Tur | File path | Ana validator kurali | Auth |
|---|---|---|---|---|
| `IngestKlineCommand` | Cmd | `Application/MarketData/Klines/Commands/IngestKline/` | Symbol 3-12 upper; OpenTime<CloseTime; OHLCV sanity | `Internal` |
| `RecordTradeCommand` | Cmd | `Application/MarketData/Trades/Commands/RecordTrade/` | TradeId>0; Qty/Price>0 | `Internal` |
| `SyncDepthSnapshotCommand` | Cmd | `Application/MarketData/Depths/Commands/SyncDepthSnapshot/` | Limit in [5..5000] | `Internal` |
| `GetLatestKlinesQuery` | Qry | `Application/MarketData/Klines/Queries/GetLatestKlines/` | Count in [1..1000] | `User` |
| `GetDepthSnapshotQuery` | Qry | `Application/MarketData/Depths/Queries/GetDepthSnapshot/` | Depth in [5,10,20,50,100] | `User` |
| `GetBookTickerQuery` | Qry | `Application/MarketData/Depths/Queries/GetBookTicker/` | Symbol non-empty | `User` |
| `GetMarketSummaryQuery` (F1) | Qry | `Application/MarketData/Queries/GetMarketSummary/` | Symbols 1..10, non-empty | `User` |

#### Instrument (5)
| Slice | Tur | File path | Ana validator kurali | Auth |
|---|---|---|---|---|
| `RefreshSymbolFiltersCommand` | Cmd | `Application/Instruments/Commands/RefreshSymbolFilters/` | — | `Admin` |
| `ListSymbolCommand` | Cmd | `Application/Instruments/Commands/ListSymbol/` | Symbol unique; filters valid | `Admin` |
| `HaltSymbolCommand` | Cmd | `Application/Instruments/Commands/HaltSymbol/` | Status==Trading | `Admin` |
| `GetSymbolFiltersQuery` | Qry | `Application/Instruments/Queries/GetSymbolFilters/` | Symbol non-empty | `User` |
| `ListActiveSymbolsQuery` | Qry | `Application/Instruments/Queries/ListActiveSymbols/` | — | `User` |

#### Order (6)
| Slice | Tur | File path | Ana validator kurali | Auth |
|---|---|---|---|---|
| `PlaceOrderCommand` | Cmd | `Application/Orders/Commands/PlaceOrder/` | stepSize+tickSize+MIN_NOTIONAL+RiskProfile gate (ADR-0005) | `User`/`Internal` |
| `CancelOrderCommand` | Cmd | `Application/Orders/Commands/CancelOrder/` | Status in [New,PartiallyFilled] | `User`/`Internal` |
| `RegisterOrderFillCommand` | Cmd | `Application/Orders/Commands/RegisterOrderFill/` | Qty ≤ remaining | `Internal` |
| `GetOrderByClientIdQuery` | Qry | `Application/Orders/Queries/GetOrderByClientId/` | ULID format | `User` |
| `ListOpenOrdersQuery` | Qry | `Application/Orders/Queries/ListOpenOrders/` | — | `User` |
| `ListOrderHistoryQuery` | Qry | `Application/Orders/Queries/ListOrderHistory/` | Take in [1..200] | `User` |

#### Position (7 — `ListPositions` F3 supersede; `GetTodayPnl` F1 yeni)
| Slice | Tur | File path | Ana validator kurali | Auth |
|---|---|---|---|---|
| `OpenPositionCommand` | Cmd | `Application/Positions/Commands/OpenPosition/` | No open for Symbol; Fill.Qty>0 | `Internal` |
| `UpdatePositionCommand` | Cmd | `Application/Positions/Commands/UpdatePosition/` | Open exists | `Internal` |
| `ClosePositionCommand` | Cmd | `Application/Positions/Commands/ClosePosition/` | Open exists | `User`/`Internal` |
| `MarkToMarketCommand` | Cmd | `Application/Positions/Commands/MarkToMarket/` | Position open | `Internal` |
| `ListPositionsQuery` (F3) | Qry | `Application/Positions/Queries/ListPositions/` | Status enum; From<To | `User` |
| `GetPositionPnlQuery` | Qry | `Application/Positions/Queries/GetPositionPnl/` | — | `User` |
| `GetTodayPnlQuery` (F1) | Qry | `Application/Positions/Queries/GetTodayPnl/` | — | `User` |

#### Strategy (8 — `GetStrategyDetail` F1 yeni)
| Slice | Tur | File path | Ana validator kurali | Auth |
|---|---|---|---|---|
| `CreateStrategyCommand` | Cmd | `Application/Strategies/Commands/CreateStrategy/` | Type-specific params; Symbols non-empty | `Admin` |
| `ActivateStrategyCommand` | Cmd | `Application/Strategies/Commands/ActivateStrategy/` | Draft/Paused; RiskProfile healthy | `Admin` |
| `DeactivateStrategyCommand` | Cmd | `Application/Strategies/Commands/DeactivateStrategy/` | Status==Active | `Admin`/`Internal` |
| `UpdateStrategyParametersCommand` | Cmd | `Application/Strategies/Commands/UpdateStrategyParameters/` | Status!=Active | `Admin` |
| `EmitStrategySignalCommand` | Cmd | `Application/Strategies/Commands/EmitStrategySignal/` | UNIQUE(StrategyId,BarOpenTime,Symbol) | `Internal` |
| `ListStrategiesQuery` | Qry | `Application/Strategies/Queries/ListStrategies/` | — | `User` |
| `GetStrategyDetailQuery` (F1) | Qry | `Application/Strategies/Queries/GetStrategyDetail/` | Id non-empty ULID | `User` |
| `GetStrategySignalsQuery` | Qry | `Application/Strategies/Queries/GetStrategySignals/` | From<To | `User` |

#### RiskProfile (7 — `GetDrawdownHistory` F1 yeni)
| Slice | Tur | File path | Ana validator kurali | Auth |
|---|---|---|---|---|
| `UpdateRiskProfileCommand` | Cmd | `Application/RiskProfiles/Commands/UpdateRiskProfile/` | RiskPerTrade ≤ 0.02; MaxPos ≤ 0.20 | `Admin` |
| `OverrideRiskCapsCommand` (F5 body) | Cmd | `Application/RiskProfiles/Commands/OverrideRiskCaps/` | AdminNote non-empty; Cap > 0 | `Admin` |
| `ResetCircuitBreakerCommand` | Cmd | `Application/RiskProfiles/Commands/ResetCircuitBreaker/` | Status in [Tripped,Cooldown] | `Admin` |
| `RecordTradeOutcomeCommand` | Cmd | `Application/RiskProfiles/Commands/RecordTradeOutcome/` | Pnl finite decimal | `Internal` |
| `GetRiskProfileQuery` | Qry | `Application/RiskProfiles/Queries/GetRiskProfile/` | — | `User` |
| `GetCircuitBreakerStatusQuery` | Qry | `Application/RiskProfiles/Queries/GetCircuitBreakerStatus/` | — | `User` |
| `GetDrawdownHistoryQuery` (F1) | Qry | `Application/RiskProfiles/Queries/GetDrawdownHistory/` | Days in [1..365] | `User` |

#### BacktestRun (5)
| Slice | Tur | File path | Ana validator kurali | Auth |
|---|---|---|---|---|
| `StartBacktestCommand` | Cmd | `Application/BacktestRuns/Commands/StartBacktest/` | Period valid; InitialBalance>0 | `Admin` |
| `RecordBacktestTradeCommand` | Cmd | `Application/BacktestRuns/Commands/RecordBacktestTrade/` | Status Running | `Internal` |
| `CompleteBacktestCommand` | Cmd | `Application/BacktestRuns/Commands/CompleteBacktest/` | Status Running | `Internal` |
| `GetBacktestResultQuery` | Qry | `Application/BacktestRuns/Queries/GetBacktestResult/` | — | `User` |
| `ListBacktestRunsQuery` | Qry | `Application/BacktestRuns/Queries/ListBacktestRuns/` | Take in [1..100] | `User` |

#### System / Observability (2 — F1 yeni)
| Slice | Tur | File path | Ana validator kurali | Auth |
|---|---|---|---|---|
| `GetSystemStatusQuery` (F1) | Qry | `Application/System/Queries/GetSystemStatus/` | — | `User` |
| `TailSystemEventsQuery` (F1) | Qry | `Application/System/Queries/TailSystemEvents/` | Limit in [1..500]; Level in [Info,Warning,Error] nullable | `User` |

### 4.2 Frontend-Aligned Queries Alt Bolumu (Blocker F1 Sonucu Eklenenler)

| Slice | Endpoint | Sayfa Kullanicisi |
|---|---|---|
| `GetMarketSummaryQuery` | `GET /api/market/summary?symbols=...` | Dashboard |
| `GetTodayPnlQuery` | `GET /api/positions/pnl/today` | Dashboard |
| `GetDrawdownHistoryQuery` | `GET /api/risk/drawdown-history?days=30` | Risk Profile |
| `TailSystemEventsQuery` | `GET /api/logs/tail` | Logs |
| `GetSystemStatusQuery` | `GET /api/system/status` | Testnet banner + footer |
| `GetStrategyDetailQuery` | `GET /api/strategies/{id}` | Strategy Config |
| `ListPositionsQuery` (F3 supersede) | `GET /api/positions?status=&symbol=&from=&to=` | Positions + Dashboard |

### 4.3 Internal-Only Slice'lar (HTTP endpoint yok)

`IngestKlineCommand`, `RecordTradeCommand`, `SyncDepthSnapshotCommand`, `ListSymbolCommand` (SymbolFiltersRefresher worker'dan), `RegisterOrderFillCommand`, `OpenPositionCommand`, `UpdatePositionCommand`, `MarkToMarketCommand`, `EmitStrategySignalCommand`, `RecordTradeOutcomeCommand`, `RecordBacktestTradeCommand`, `CompleteBacktestCommand`.

**Kaynak kararlar:** ADR-0003 (idempotent handler), architecture-notes.md §3, backend-design.md §11.1; `.ai-trace/decisions.jsonl` seq#TBD-log-gelince-guncel.

---

## 5. Database Schema + Otomatik Migration

### 5.1 ApplicationDbContext DbSet Listesi

Aggregate basina 1 DbSet + 1 `IEntityTypeConfiguration<T>` dosyasi + SystemEvent audit:

```csharp
public interface IApplicationDbContext
{
    DbSet<Kline> Klines { get; }
    DbSet<Trade> Trades { get; }
    DbSet<Depth> Depths { get; }
    DbSet<Instrument> Instruments { get; }
    DbSet<Order> Orders { get; }
    DbSet<Position> Positions { get; }
    DbSet<Strategy> Strategies { get; }
    DbSet<RiskProfile> RiskProfiles { get; }
    DbSet<BacktestRun> BacktestRuns { get; }
    DbSet<SystemEvent> SystemEvents { get; } // audit — aggregate degil
    Task<int> SaveChangesAsync(CancellationToken ct);
}
```

Configuration dosyalari: `Infrastructure/Persistence/Configurations/<Aggregate>Configuration.cs` (+ `SystemEventConfiguration.cs`).

### 5.2 UNIQUE Constraint Matrisi

| Aggregate / Tablo | UNIQUE | Gerekce |
|---|---|---|
| `Kline` | `(Symbol, Interval, OpenTime)` | Idempotent ingest (ADR-0003) |
| `Trade` | `(Symbol, TradeId)` | Binance idempotent |
| `Depth` | `(Symbol)` PK | Symbol basina tek row |
| `Instrument` | `(Symbol)` | Symbol metadata tekil |
| `Order` | `(ClientOrderId)` ULID; `(ExchangeOrderId)` nullable | Idempotent order (ADR-0003) |
| `OrderFill` (Order child) | `(OrderId, ExchangeTradeId)` | Idempotent fill |
| `Position` | `(Symbol) WHERE Status='Open'` filtered index | Tek acik pozisyon/sembol |
| `Strategy` | `(Name)` | Tekil strateji adi |
| `StrategySignal` (Strategy child) | `(StrategyId, BarOpenTime, Symbol)` | Bar basina tek sinyal |
| `RiskProfile` | `Id=1` singleton | Global tek risk profili |
| `BacktestRun` | — | Run id otogenerate |
| `BacktestTrade` (BacktestRun child) | `(RunId, SequenceNo)` | Run icinde sirali |
| `SystemEvent` | — (Id identity) | Append-only log |

### 5.3 Fluent API Konfigurasyon Yeri

`Infrastructure/Persistence/Configurations/<Aggregate>Configuration.cs` — `IEntityTypeConfiguration<T>` implement. `ApplicationDbContext.OnModelCreating` icinde `modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly)`.

### 5.4 Startup Hook Snippet

[backend-design.md §3](./backend-design.md) 1:1:

```csharp
var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        logger.LogInformation("Migration started.");
        await db.Database.MigrateAsync();
        var applied = await db.Database.GetAppliedMigrationsAsync();
        logger.LogInformation("Migration completed. AppliedMigrations={Count}", applied.Count());
    }
    catch (Exception ex)
    {
        logger.LogCritical(ex, "Migration failed; application boot aborted.");
        return 1; // exit code 1 -> orchestrator restart policy
    }
}

await app.RunAsync();
return 0;
```

**Exit code 1 semantics:** `OptionsValidationException` (ValidateOnStart fail), migration exception, testnet-first guard ihlali, secret eksikligi — hepsi exit 1 ile boot'u durdurur; orchestrator (Docker/K8s/systemd) restart policy devreye girer.

### 5.5 Migration Naming + Tooling

- Naming: `<YYYYMMDDHHmm>_<ChangeName>` (ornek `202604170900_AddKlineAggregate`).
- Komut: `dotnet ef migrations add <Name> -p src/Infrastructure -s src/Api`.
- Database update **manuel YASAK**; sadece startup hook uygular (ADR-0001).
- `Database.EnsureCreatedAsync()` YASAK.

### 5.6 Seed Data Policy

`ApplicationDbContext.OnModelCreating.HasData()` ile migration'a gomulur:

- **Instrument initial set:** `BTCUSDT`, `ETHUSDT`, `BNBUSDT` minimal metadata; ilk boot sonrasi `SymbolFiltersRefresher` gercek filter'lari cekip `UpdateFilters` ile tazeler.
- **RiskProfile singleton:** Id=1; default degerleri (`RiskPerTradePct=0.01`, `MaxPositionSizePct=0.10`, `MaxDrawdown24hPct=0.05`, `MaxDrawdownAllTimePct=0.25`, `MaxConsecutiveLosses=3`, `CircuitBreakerStatus=Healthy`).

### 5.7 Rollback Policy

**MVP'de rollback YOK; forward-only.** Geri donus icin DB drop + fresh migrate. Production olmadigindan (ADR-0006 testnet-first) kabul. Production'a gecerken ADR-0009 icinde rollback stratejisi (backup + revert migration) yeniden ele alinir.

**Kaynak kararlar:** ADR-0001 (auto-migration), ADR-0003 (UNIQUE matrisi); `.ai-trace/decisions.jsonl` seq#TBD-log-gelince-guncel.

---

## 6. Binance Entegrasyonu

### 6.1 REST Client Config + Polly

- Named HttpClient `"binance-rest"`; `BaseAddress = BinanceOptions.RestBaseUrl` (testnet default: `https://testnet.binance.vision`).
- 4 DelegatingHandler pipeline (dis->ic): `ClockOffsetHandler` → `HmacSignatureDelegatingHandler` → `RetryAfterAwareHandler` → `WeightTrackerHandler`.
- `AddStandardResilienceHandler` (Polly v8):

| Katman | Deger |
|---|---|
| `Retry.MaxRetryAttempts` | 4 |
| `Retry.BackoffType` | Exponential |
| `Retry.UseJitter` | true |
| `Retry.Delay` | 500ms baseline |
| `CircuitBreaker.MinimumThroughput` | 10 |
| `CircuitBreaker.FailureRatio` | 0.5 |
| `CircuitBreaker.SamplingDuration` | 30s |
| `CircuitBreaker.BreakDuration` | 10s |
| `AttemptTimeout.Timeout` | 10s |
| `TotalRequestTimeout.Timeout` | 30s |

Kod pinleri: [backend-design.md §4.1-4.2](./backend-design.md).

### 6.2 DelegatingHandler Tanimlari

| Handler | Is |
|---|---|
| `ClockOffsetHandler` | `timestamp` query param injekte eder; `IBinanceTimeSource.Offset` ile serverTime uyumlu. |
| `HmacSignatureDelegatingHandler` | Query+body+timestamp payload'u HMAC-SHA256 ile imzalar; `signature` param + `X-MBX-APIKEY` header; log'da `***` redact. |
| `RetryAfterAwareHandler` | 429/418 yanitinda `Retry-After` parser; `DelayHint` property'i Polly retry strategy'sine feed. |
| `WeightTrackerHandler` | `X-MBX-USED-WEIGHT-1M` + `X-MBX-ORDER-COUNT-10S/1D` okur; esik (1200/1m) asilirsa soft pause + alarm. |

### 6.3 WS Supervisor State Machine

ASCII diyagram (G2 cozumu — planned vs unplanned transition ayri):

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

- **Planned transition:** 23h uptime yaklasirken preemptive reconnect (hot swap: yeni baglanti subscribe olunca eskisi `NormalClosure`).
- **Unplanned transition:** ping miss / 60s no pong / network disconnect → Backoff → Connect.
- Backoff: `1s → 30s cap`, ±20% jitter.

### 6.4 Heartbeat + Reconnect Disiplini

- Server 20s'de ping eder; `ClientWebSocket` framework pong'u otomatik doner.
- `ClientWebSocketOptions.KeepAliveInterval = TimeSpan.Zero` — client-initiated ping YOK (C1 cozumu).
- Local 75s watchdog timer; hicbir frame gelmezse manuel close + reconnect.
- 23h preemptive reconnect (Binance 24h cap'i once).
- Depth U/u resync 7-adim (binance-research.md §2.4 + backend-design.md §5.4):
  1. Stream abone + buffer.
  2. `/api/v3/depth?limit=5000` snapshot.
  3. `lastUpdateId < firstBufferedU` → tekrar event bekle.
  4. Buffer'da `u ≤ lastUpdateId` drop.
  5. Ilk event `U ≤ lastUpdateId+1 ≤ u` degilse resync.
  6. `Depth.ApplySnapshot` + her event `ApplyDiff`.
  7. `U != prev_u+1` → gap → `DepthGapDetectedEvent` + supervisor resync.

### 6.5 User Data Stream Reconnect Akisi (C2 Cozumu)

1. Disconnect tespit (ping miss / 23h preemptive / network).
2. Eski `listenKey` gecersiz olabilir → `POST /api/v3/userDataStream` ile **yeni** `listenKey`.
3. Yeni `wss://.../<newListenKey>` endpoint'ine abone ol.
4. `UserDataStreamKeepalive` 25dk sayacini sifirla.
5. Replay: `GET /api/v3/openOrders` + `GET /api/v3/account` ile local state reconcile.
6. Event'ler: `executionReport` → `RegisterOrderFillCommand`; `outboundAccountPosition` → bakiye cache; `balanceUpdate`; `listStatus`.

### 6.6 Rate Limit Yonetimi

- Butceler: `REQUEST_WEIGHT` (6000/1m IP), `ORDERS` (100/10s + 200000/1d UID), `RAW_REQUESTS`.
- Her response `X-MBX-USED-WEIGHT-1M` header'i; 1200/min → soft pause (uyari + queue), 2400/min alarm.
- 429 → `Retry-After` ile backoff; 429 ignore → 418 (IP ban 2m → 3d).
- `BinanceWeightBudget` token-bucket; her REST cagrisindan once `TryConsume(weight)`.

### 6.7 Clock Sync

- Boot'ta `GET /api/v3/time` → `offset = serverTime - localTime`.
- `ClockSyncWorker` saatte 1 refresh; drift > 2s → alarm + SystemEvent.
- `HmacSignatureDelegatingHandler` her imzali istekte `timestamp = UtcNow - offset`; `recvWindow=5000`.

**Kaynak kararlar:** ADR-0002 (WS supervisor), ADR-0006 (testnet-first); `.ai-trace/decisions.jsonl` seq#TBD-log-gelince-guncel.

---

## 7. Trading Strategy Taslaklari

Research §7'de red-flag taramasindan temiz gecen ilk 3 strateji:

### 7.1 Grid (BTC/USDT Range-Bound)

| Alan | Deger |
|---|---|
| **Kosul** | ADX(14,1h) < 20 (range confirm); ATR(14,1h) < %1 × close; son 48h range %3 icinde |
| **Giris** | `LIMIT_MAKER` grid, ±3% width, 10-20 level; her level notional portfoyun %1'i |
| **Cikis** | Price breakout 2×ATR → tum acik emirler iptal + manuel review |
| **Stop-loss** | Aggregate loss %5 → kill-switch (Strategy.Deactivate by CircuitBreaker) |
| **Risk/trade** | %0.5 |
| **Red flag** | Leveraged grid %78 likidasyon; leverage YOK (spot). |
| **Uygun sembol** | BTCUSDT (likidite yeter); ETHUSDT opsiyonel |
| **Timeframe** | 1h kline entry kararinda |

### 7.2 Trend Following (EMA20/50 + ADX25 + ATR Stop)

| Alan | Deger |
|---|---|
| **Kosul** | EMA20 > EMA50 (cross up) + ADX(14) > 25 + yeni close EMA20 ustunde |
| **Giris** | Market/LIMIT_MAKER entry; timeframe 1h primary + 15m confirm |
| **Cikis** | EMA20 < EMA50 (trend donusu) veya trailing stop tetik |
| **Stop-loss** | Server-side `STOP_LOSS_LIMIT`; 2×ATR(14) mesafede; trailing stop pozitif PnL'de |
| **Risk/trade** | %1 |
| **Red flag** | Yatay piyasada whipsaw; ADX25 filtresi bu yuzden zorunlu. Win-rate %30-40, R-ratio yuksek |
| **Uygun sembol** | BTCUSDT, ETHUSDT |
| **Timeframe** | 1h + 15m |

### 7.3 Mean Reversion (RSI + Bollinger)

| Alan | Deger |
|---|---|
| **Kosul** | RSI(14) < 30 + close < BB_lower(20, 2σ); 1h EMA(200) duz veya yukari (filtre) |
| **Giris** | Limit entry (maker); |
| **Cikis** | Close BB_middle'a dondu veya RSI > 55; time-stop 12h |
| **Stop-loss** | `STOP_LOSS_LIMIT`; entry - 1.5×ATR(14) |
| **Risk/trade** | %0.75 |
| **Red flag** | Trend piyasasinda surekli yanlis sinyal; EMA200 dik asagi ise filtrele |
| **Uygun sembol** | Hepsi (BNB cap %50) |
| **Timeframe** | 15m + 1h filtre |

**Ortak disiplin:** Her emir `POST /api/v3/order/test` ile once validate (filter check); production-mode `AllowMainnet=true` bile olsa paper-trade tamamlanmadan live trade YOK. Red-flag taramasi `binance-trading-strategy-review` skill'iyle her strateji onerisi oncesi tekrar calistirilir.

**Kaynak kararlar:** binance-research.md §7; `.ai-trace/decisions.jsonl` seq#TBD-log-gelince-guncel.

---

## 8. Risk Yonetimi

### 8.1 Position Sizing Formulu

```
qty = (accountEquity × riskPerTradePct) / (entryPrice × stopDistancePct)
```

Ornek: `equity=$10k, riskPerTrade=%1, entry=$60000, stop=$58800 (−2%)` → `qty = (10000 × 0.01) / (60000 × 0.02) = 100 / 1200 = 0.0833 BTC`.

Formul `RiskProfileGate.Allow(...)` icinde `PlaceOrderCommand` validator'inda calistirilir; stepSize'e round-down + MIN_NOTIONAL kontrolu uygulanir.

### 8.2 STOP_LOSS_LIMIT Zorunlulugu

- **Client-side stop YASAK** (black swan — baglanti koparsa calismaz; May 19 2021 crash referansi).
- Her entry order ardindan otomatik `STOP_LOSS_LIMIT` emri; `IStopLossPlacer` port (Infrastructure: `BinanceTradingClient.PlaceOrderAsync` ile).
- Stop distance strateji konfigurasyonundan (Trend: 2×ATR; MeanReversion: 1.5×ATR; Grid: aggregate loss kill-switch).

### 8.3 Circuit Breaker Esikleri

| Metric | Esik | Tetik |
|---|---|---|
| MaxDrawdown 24h | %5 | Trip → tum Active strateji Deactivate + tum open order Cancel |
| MaxDrawdown all-time | %25 | Trip |
| Consecutive losses (strategy scope) | 3 ardardak | Strategy auto Deactivate (tek strateji scope) |
| Gross exposure | %30 ustu | Pre-trade Invalid |

Trip → `CircuitBreakerTrippedEvent` fanout: `StrategyDeactivatorHandler` + `ActiveOrderCancelerHandler` + `AlarmHandler` (SystemEvent + Serilog Error). Reset sadece admin (`ResetCircuitBreakerCommand` + AdminNote); `Cooldown` state 1h sonra `Healthy`.

### 8.4 Pre-Trade 5-Adim Gate (RiskProfile Invariant)

`RiskProfileGate.Allow(placeOrderCmd)` adimlari:

1. **CircuitBreakerStatus != Tripped** — yoksa `Invalid("Circuit breaker tripped")`.
2. **Position size ≤ MaxPositionSizePct × equity** — qty hesabi + filter round-down sonrasi.
3. **Gross exposure + yeni emir ≤ MaxGrossExposurePct × equity**.
4. **Symbol-specific filter** (stepSize, tickSize, MIN_NOTIONAL, PERCENT_PRICE_BY_SIDE).
5. **ConsecutiveLossCount < MaxConsecutiveLosses** (strategy scope) — yoksa Strategy Deactivate.

Hata turu → `Result.Invalid("Risk limit breach: ...")` → HTTP 400.

### 8.5 Testnet-First (ADR-0006)

- `BinanceOptions.AllowMainnet` default `false`.
- `AllowMainnet=false` ise URL **testnet.binance.vision** icermeli; degilse `ValidateOnStart` throw → exit 1.
- `AllowMainnet=true` ise URL **testnet icermemeli**; ayrica ADR-0007 admin endpoint'leri 403 doner (nafile kapi — prod'a gecis icin ADR-0009 kaçınılmaz).
- 3 Kapi: (1) AllowMainnet boot check; (2) PaperTradeCompleted flag; (3) ApiKey prefix/uzunluk heuristic.

**Kaynak kararlar:** ADR-0005, ADR-0006, ADR-0007; `.ai-trace/decisions.jsonl` seq#TBD-log-gelince-guncel.

---

## 9. API Endpoint Envanteri (Nihai — 38 Endpoint)

Blocker F1-F3 + F5 cozumu sonrasi tam tablo.

| # | Method | Route | Request | Response | Auth | Command/Query |
|---|---|---|---|---|---|---|
| 1 | GET | `/api/klines` | `?symbol&interval&count` | `Result<IReadOnlyList<KlineDto>>` | User | `GetLatestKlinesQuery` |
| 2 | GET | `/api/depth` | `?symbol&depth` | `Result<DepthSnapshotDto>` | User | `GetDepthSnapshotQuery` |
| 3 | GET | `/api/ticker/book` | `?symbol` | `Result<BookTickerDto>` | User | `GetBookTickerQuery` |
| 4 | GET | `/api/market/summary` (F1) | `?symbols=BTCUSDT,ETHUSDT,BNBUSDT` | `Result<IReadOnlyList<MarketSummaryDto>>` | User | `GetMarketSummaryQuery` |
| 5 | GET | `/api/instruments` | — | `Result<IReadOnlyList<InstrumentDto>>` | User | `ListActiveSymbolsQuery` |
| 6 | GET | `/api/instruments/{symbol}/filters` | — | `Result<SymbolFiltersDto>` | User | `GetSymbolFiltersQuery` |
| 7 | POST | `/api/instruments/refresh` | — | `Result<int>` | Admin | `RefreshSymbolFiltersCommand` |
| 8 | POST | `/api/instruments/{symbol}/halt` | `{ "reason": "..." }` | `Result` | Admin | `HaltSymbolCommand` |
| 9 | POST | `/api/orders` | `PlaceOrderRequest` | `Result<PlacedOrderDto>` | User | `PlaceOrderCommand` |
| 10 | DELETE | `/api/orders/{clientOrderId}` | — | `Result` | User | `CancelOrderCommand` |
| 11 | GET | `/api/orders/{clientOrderId}` | — | `Result<OrderDto>` | User | `GetOrderByClientIdQuery` |
| 12 | GET | `/api/orders/open` | `?symbol` | `Result<IReadOnlyList<OrderDto>>` | User | `ListOpenOrdersQuery` |
| 13 | GET | `/api/orders/history` | `?symbol&from&to&skip&take` | `Result<PagedResult<OrderDto>>` | User | `ListOrderHistoryQuery` |
| 14 | GET | `/api/positions` (F3) | `?status=open|closed&symbol&from&to` | `Result<IReadOnlyList<PositionDto>>` | User | `ListPositionsQuery` |
| 15 | GET | `/api/positions/{symbol}/pnl` | — | `Result<PositionPnlDto>` | User | `GetPositionPnlQuery` |
| 16 | GET | `/api/positions/pnl/today` (F1) | — | `Result<TodayPnlDto>` | User | `GetTodayPnlQuery` |
| 17 | POST | `/api/positions/{symbol}/close` | `{ "reason": "..." }` | `Result<ClosedPositionDto>` | User | `ClosePositionCommand` |
| 18 | POST | `/api/strategies` | `CreateStrategyRequest` | `Result<StrategyIdDto>` | Admin | `CreateStrategyCommand` |
| 19 | GET | `/api/strategies` | `?status` | `Result<IReadOnlyList<StrategyDto>>` | User | `ListStrategiesQuery` |
| 20 | GET | `/api/strategies/{id}` (F1) | — | `Result<StrategyDetailDto>` | User | `GetStrategyDetailQuery` |
| 21 | POST | `/api/strategies/{id}/activate` | — | `Result` | Admin | `ActivateStrategyCommand` |
| 22 | POST | `/api/strategies/{id}/deactivate` | `{ "reason": "..." }` | `Result` | Admin | `DeactivateStrategyCommand` |
| 23 | PUT | `/api/strategies/{id}/parameters` | `StrategyParametersDto` | `Result` | Admin | `UpdateStrategyParametersCommand` |
| 24 | GET | `/api/strategies/{id}/signals` | `?from&to` | `Result<IReadOnlyList<StrategySignalDto>>` | User | `GetStrategySignalsQuery` |
| 25 | GET | `/api/risk/profile` | — | `Result<RiskProfileDto>` | User | `GetRiskProfileQuery` |
| 26 | GET | `/api/risk/circuit-breaker` | — | `Result<CircuitBreakerStatusDto>` | User | `GetCircuitBreakerStatusQuery` |
| 27 | PUT | `/api/risk/profile` | `UpdateRiskProfileRequest` | `Result` | Admin | `UpdateRiskProfileCommand` |
| 28 | POST | `/api/risk/override-caps` (F2 rename) | `{ "riskPerTradeCap": 0.015, "maxPositionCap": 0.15, "adminNote": "..." }` | `Result` | Admin | `OverrideRiskCapsCommand` |
| 29 | POST | `/api/risk/circuit-breaker/reset` | `{ "adminNote": "..." }` | `Result` | Admin | `ResetCircuitBreakerCommand` |
| 30 | GET | `/api/risk/drawdown-history` (F1) | `?days=30` | `Result<IReadOnlyList<DrawdownPointDto>>` | User | `GetDrawdownHistoryQuery` |
| 31 | POST | `/api/backtests` | `StartBacktestRequest` | `Result<long>` | Admin | `StartBacktestCommand` |
| 32 | GET | `/api/backtests/{id}` | — | `Result<BacktestResultDto>` | User | `GetBacktestResultQuery` |
| 33 | GET | `/api/backtests` | `?strategyId&status&skip&take` | `Result<PagedResult<BacktestRunSummaryDto>>` | User | `ListBacktestRunsQuery` |
| 34 | GET | `/api/system/status` (F1) | — | `Result<SystemStatusDto>` | User | `GetSystemStatusQuery` |
| 35 | GET | `/api/logs/tail` (F1) | `?since&level&limit=200` | `Result<SystemEventTailDto>` | User | `TailSystemEventsQuery` |
| 36 | GET | `/health/live` | — | 200 | Anon | — |
| 37 | GET | `/health/ready` | — | 200/503 | Anon | — |
| 38 | POST | `/health/ready` | — | — | — | — (slot placeholder; yok) |

**Toplam aktif: 37** (35 CQRS + 2 health). Auth dagilimi: 20 User + 11 Admin + 2 Anon + 4 Internal-only (HTTP'de yok).

**Kaynak kararlar:** plan-review-notes.md F1-F5; backend-design.md §11.1; `.ai-trace/decisions.jsonl` seq#TBD-log-gelince-guncel.

---

## 10. Frontend Sayfa Envanteri

8 sayfa × state × API tablosu (frontend-design.md §7 ile tutarli).

| Sayfa | Durum Anahtarlari | Polling | API Endpoint'leri |
|---|---|---|---|
| **Dashboard** (`/index.html`) | `summary{BTC,ETH,BNB}, positions{open,count,unrealizedPnl}, pnl{realizedToday,unrealizedTotal}, health{api,wsAgeSec,circuitBreaker}` | 2s | `/api/market/summary`, `/api/positions?status=open`, `/api/positions/pnl/today`, `/api/health/ready`, `/api/risk/circuit-breaker` |
| **Kline'lar** (`/klines.html`) | `symbol, interval, klines[], indicators{rsi,bollinger}, chartInstance` | 2s | `/api/klines?symbol&interval&count=500` |
| **OrderBook** (`/orderbook.html`) | `symbol, depth{bids,asks,lastUpdateId}, spread{abs,bps}, liquidity{bidSum,askSum,imbalance}` | 1s | `/api/depth?symbol&depth=20` |
| **Positions** (`/positions.html`) | `open[], closed[], filter{from,to,symbol}, closing{}` | open 2s / closed 30s | `/api/positions?status=open`, `/api/positions?status=closed&from&to&symbol`, `POST /api/positions/{symbol}/close`, `/api/positions/{symbol}/pnl` |
| **Orders** (`/orders.html`) | `active[], history{items,total,skip,take}, filter{symbol,status,from,to}, canceling{}` | active 3s / history 30s | `/api/orders/open?symbol`, `/api/orders/history?*`, `DELETE /api/orders/{clientOrderId}`, `/api/orders/{clientOrderId}` |
| **Strategy Config** (`/strategies.html`) — **read-only** | `strategies[], selected, paramsForm, signals[]` | 10s, signals 5s | `/api/strategies?status`, `/api/strategies/{id}`, `/api/strategies/{id}/signals` |
| **Risk Profile** (`/risk.html`) — **read-only** | `profile, circuitBreaker, history[]` | 10s | `/api/risk/profile`, `/api/risk/circuit-breaker`, `/api/risk/drawdown-history?days=30` |
| **Logs** (`/logs.html`) | `lines[], filter{level}, follow, since` | 2s (delta) | `/api/logs/tail?since&level&limit` |

**Admin UI YOK (ADR-0007):** Strategy ve Risk sayfalari **read-only**. Activate/Deactivate/UpdateParameters/ResetCircuitBreaker/OverrideCaps/UpdateRiskProfile admin endpoint'leri **sadece Swagger "Authorize" + `X-Admin-Key` veya `tests/manual/*.http`** uzerinden cagrilir. Frontend'te admin key `localStorage`'a kopyalanmaz — XSS exfil riski sifirlanir.

**Ortak:** Testnet banner tum sayfalarda (`appStore.refreshEnv()` / 60s); `/api/system/status` veri kaynagi. Mainnet kirmizi + paper-trade/circuit-breaker uyari.

**Kaynak kararlar:** ADR-0007; frontend-design.md §2, §7; `.ai-trace/decisions.jsonl` seq#TBD-log-gelince-guncel.

---

## 11. Gozlemlenebilirlik

### 11.1 Serilog Kurulumu

- **Sinks:** Console + File (`logs/app-.log`, rolling Day, 7 gun retention).
- **Formatter:** `CompactJsonFormatter` structured JSON.
- **Enrich:** `FromLogContext`, `WithMachineName`, `WithProperty("App","BinanceBot")`.
- **Log levels:** Default `Information`; `Microsoft.AspNetCore` → `Warning`; `Microsoft.EntityFrameworkCore.Database.Command` → `Warning` (SQL log yok — PII riski).

### 11.2 Correlation ID Middleware

Her HTTP request `X-Correlation-Id` header'i okur; yoksa `Guid.NewGuid().ToString("N")` → response'a yansit. `LogContext.PushProperty("CorrelationId", ...)`. WS supervisor + background worker kendi CorrelationId'lerini uretir.

### 11.3 PII / Secret Redaction Policy

- `ApiKey`, `ApiSecret`, `AdminApiKey`, `ConnectionString` property'leri `***` ile destructure (`Serilog.Policies.RedactingPolicy`).
- HTTP request body log edilmez.
- Response body sadece hata durumunda + max 1KB log.
- `ILogger<T>` structured logging zorunlu — string concat YASAK.

### 11.4 SystemEvents Tablosu + `/api/logs/tail`

- `SystemEvent` POCO audit tablosu (aggregate degil — §2).
- `ISystemEventWriter.Write(level, source, type, message, payload)` Application abstraction; `AuditLogHandler` ve `AlarmHandler` tum kritik domain event'leri burasina yazar (OrderPlaced/Filled, PositionClosed, CircuitBreakerTripped/Reset, StrategyActivated/Deactivated, RiskOverride, PaperTradeCompleted, WsReconnect planned/unplanned).
- Frontend `/api/logs/tail` (`TailSystemEventsQuery`) → SystemEvents tablosu **tek kaynak**. Serilog dosya tail YASAK.

### 11.5 `.ai-trace` Entegrasyonu (agent-bus MCP)

- Her agent turn'u `append_decision` → `.ai-trace/decisions.jsonl`.
- PM handoff → `append_handoff` → `.ai-trace/handoffs.jsonl`.
- Kullanici notu → `/tell-pm` → `append_user_note` → `.ai-trace/user-notes.jsonl`.
- SubagentStop hook → `.ai-trace/subagent-stops/*.md`.
- Audit zincirinde decision ↔ handoff ↔ subagent-stop birebir eslesir. PM checkpoint'te `read_user_notes` + `get_task_state` ile durumu okur.

### 11.6 Dashboard Onerisi (Faz-2)

- Grafana / Seq gibi harici dashboard **NOT_IN_SCOPE MVP**. CompactJson Serilog sink Seq'e point edilebilir; gelecekte `GET /metrics` (Prometheus) endpoint Faz-2 ADR.

**Kaynak kararlar:** ADR-0003 (idempotent + audit), ADR-0007 (admin audit); `.ai-trace/decisions.jsonl` seq#TBD-log-gelince-guncel.

---

## 12. Guvenlik (OWASP ASP.NET Core Checklist)

| Madde | BinanceBot'ta Nasil Karsilaniyor |
|---|---|
| **Secret yonetimi** | ADR-0004 — user-secrets (dev) + env var (prod); `appsettings.json` placeholder/BOS; `ValidateOnStart` bos reddeder (A2 cozumu). `AdminApiKey` user-secrets zorunlu, min 32 byte. |
| **Testnet-first** | ADR-0006 — `AllowMainnet=false` default; 3 kapi (config + PaperTradeCompleted + ApiKey heuristic); boot-time guard throw+exit1. |
| **Admin auth** | ADR-0007 — solo-dev local kiosk; `X-Admin-Key` header sabit-zaman compare (`CryptographicOperations.FixedTimeEquals`); frontend admin UI YOK; AllowMainnet=true iken admin endpoint'ler 403 (nafile kapi — ADR-0009 gerektirir). |
| **SQL injection** | EF Core parameterized queries only; raw SQL YASAK. |
| **XSS** | Vue `{{ }}` interpolation otomatik escape; **`v-html` YASAK** (D4); user input HTML parse yok. |
| **CSRF** | MVP: cookie auth YOK, admin `X-Admin-Key` header only, otomatik gonderilmiyor → CSRF vektoru yok. Prod'a gecerken ADR-0009 JWT + double-submit CSRF. |
| **CORS** | MVP localhost bind; `AllowedHosts = "localhost;127.0.0.1"`; `Cors.Origins` explicit whitelist (appsettings.json D2 cozumu); wildcard YASAK. |
| **CSP + HSTS** | `NetEscapades.AspNetCore.SecurityHeaders` **baseline (zorunlu, opsiyonel degil — A4 cozumu)**: `default-src 'self'; script-src 'self' cdn.jsdelivr.net unpkg.com; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'`. HSTS `max-age=365d; includeSubDomains`. Paket major < 1 stabilite notu §15 bilinen bosluk'ta. |
| **DTO over-posting** | Explicit command mapping (`PlaceOrderRequest.ToCommand()`); FluentValidation whitelist (sadece izinli alanlar valide). |
| **Log leak** | API key `[NotLogged]` attribute / `***` redact; request body log edilmez; response body max 1KB + sadece hata. |
| **Clickjacking** | `X-Frame-Options: DENY` (SecurityHeaders policy). |
| **Dependency pinning** | NuGet `Directory.Packages.props` + `ManagePackageVersionsCentrally=true`; frontend CDN versiyon pin + SRI Faz-2. |

**Kaynak kararlar:** ADR-0004, ADR-0006, ADR-0007; OWASP Cheat Sheet; `.ai-trace/decisions.jsonl` seq#TBD-log-gelince-guncel.

---

## 13. Test Stratejisi

### 13.1 Test Projeleri (MVP Tek csproj)

`tests/Tests/` altinda klasor bazli ayrim: `Domain/`, `Application/`, `Infrastructure/`, `Api/`, `Fixtures/`. Fleet buyurse bolunme Faz-2 ADR.

### 13.2 Katmanlar

**Domain.Tests** (pure, mocksuz):
- Aggregate invariant: `KlineClosed==true after MarkClosed`, `Order.FilledQty ≤ Quantity`, `Position single-open per Symbol`.
- Davranis metodu: `Kline.UpdateOngoing` fail on closed, `Order.RecordFill` partial → `Filled`, `Position.Reduce` PnL formulu.
- Domain event uretimi: `RiskProfile.TripCircuitBreaker` → `CircuitBreakerTrippedEvent` raise.

**Application.Tests** (mocked, NSubstitute):
- Handler + Validator: `PlaceOrderCommandHandler` happy path + validator fail path; `ActivateStrategyCommandHandler` risk gate Tripped → `Invalid`.
- Result mapping: `Result.NotFound` → 404; `Invalid` → 400; `Error` → 500.
- `IBinanceTrading` mock'lanarak REST failure → `Result.Error`.

**Api.IntegrationTests** (`WebApplicationFactory<Program>` + `Testcontainers.MsSql`):
- Endpoint round-trip: `POST /api/orders` → DB'ye yazim → `GET /api/orders/{id}` veriyi dondurur.
- Migration apply: container baslaymca migration hook calisir + 0 hata.
- Ardalis.Result sarma: basari → `{ data: ... }`; hata → `{ errors: [...], status }`.
- Ephemeral DB: her test ayri schema veya Testcontainers shutdown (isolation).

**Playwright E2E** (tester agent senaryolari):
- Dashboard yuklenme: `index.html` acilir → testnet banner gorunur → 3 sembol card render → 2s icinde data gelir.
- Kline sayfasi: symbol switcher BTCUSDT→ETHUSDT → yeni veri + chart render.
- Pozisyon acma (dry-run): `POST /api/orders` `test=true` → 201 + order DB'de `New` status.
- Risk override flow: Swagger Authorize → `POST /api/risk/override-caps` → `GET /api/risk/profile` yeni cap gorunur.
- Testnet banner varligi: tum sayfalarda `<header.banner-testnet>` DOM'da mevcut.

### 13.3 Coverage Hedefleri

| Layer | Minimum |
|---|---|
| Domain | **≥ %90** |
| Application | **≥ %75** |
| Infrastructure | ≥ %50 (adapter-heavy) |
| Api | ≥ %60 (integration) |
| **Overall** | **≥ %60** |

### 13.4 Test Isimlendirme (BDD Style)

`<Subject>_<Condition>_<Expected>`:
- `Kline_WhenMarkClosedCalled_RaisesKlineClosedEvent`
- `PlaceOrderCommand_WhenRiskGateTripped_ReturnsInvalid`
- `OrderApi_WhenClientOrderIdDuplicate_Returns409Conflict`

**Kaynak kararlar:** ADR-0003 (idempotent test), CLAUDE.md tester disciplini; `.ai-trace/decisions.jsonl` seq#TBD-log-gelince-guncel.

---

## 14. Sprint Haritasi

6 sprint; her sprint done-definition + acceptance test + risk notu.

### S1 — Data Ingestion

- **Scope:** MarketData ingestion (Kline + Trade + Depth) + WS supervisor + kline persist + Instrument seed.
- **Done-definition:** `BinanceWsSupervisor` BackgroundService çalışır; `ClockSyncWorker` + `SymbolFiltersRefresher` boot'ta aktif; `KlineIngestedEvent` + `KlineClosedEvent` raise; DB'de 3 sembol × 1m kline sürekli insert/upsert.
- **Acceptance:** 24 saat kesintisiz BTCUSDT 1m kline DB'ye yazilir (bar kaybi ≤ %0.1); depth snapshot+diff resync U/u validation testi gecer; WS reconnect (network outage simulasyonu) sonrasi replay disiplini calisir.
- **Risk:** Testnet instability — backup fallback olarak `GET /api/v3/klines` REST warmup her boot'ta.

### S2 — Query API + Migration

- **Scope:** Order / Position / Strategy read endpoint'leri + auto-migration + `Result<T>` HTTP mapping + CQRS read-side tamami (17 User/Anon endpoint).
- **Done-definition:** `dotnet run` ile boot → migration tamam → `/health/ready` 200 → 17 endpoint Swagger'da; her sorgu `Result<T>` contract uyumlu yanit; `Testcontainers.MsSql` integration test'leri gecer.
- **Acceptance:** `GET /api/klines` + `/api/positions` + `/api/risk/profile` happy path 200; hata durumunda 400/404 Ardalis sarmasi dogru; migration drop+fresh boot 0 hata; `SystemEvent` audit tablosu ilk kayitlari atar.
- **Risk:** EF Core concurrency token eksikligi → race condition; ADR-0003 UNIQUE index bunu karsilar.

### S3 — UI (Vue CDN + 8 Sayfa + Polling)

- **Scope:** 8 HTML sayfa + importmap + `js/api.js`, `js/store.js`, `js/ui.js` + `css/style.css` + testnet banner.
- **Done-definition:** Her sayfa `/index.html`, `/klines.html`, `/orderbook.html`, `/positions.html`, `/orders.html`, `/strategies.html`, `/risk.html`, `/logs.html` browser'da acilir + polling calisir + hata banner render + loading skeleton.
- **Acceptance:** Dashboard 3 sembol data yukler; klines candlestick render; orderbook canli degisir; `v-html` kullanimi sifir (reviewer grep); admin aksiyonlar frontend'de yok; Playwright baseline senaryolari (5 adet §13.2) gecer.
- **Risk:** importmap CDN down → offline/vendor fallback plani (§3 frontend-design.md).

### S4 — Strategy Engine

- **Scope:** Strategy aggregate + `StrategyEvaluatorWorker` + 3 strateji signal uretimi + `StrategySignalEmittedEvent` → `OrderPlacementHandler`.
- **Done-definition:** `CreateStrategyCommand` + `ActivateStrategyCommand` + `EmitStrategySignalCommand` slice'lari; `IStrategyEvaluator` concrete (Skender.Stock.Indicators); KlineClosed event tetiklemesi ile signal DB'de olusur.
- **Acceptance:** 3 strateji (Grid/Trend/MeanRev) aktif; 1 gun calismadan sonra StrategySignal tablosuna en az 10 sinyal dusmus; `StrategySignalEmittedEvent` handler'lari `PlaceOrderCommand` tetiklemeyi hazir (dry-run modda kalir, gercek order atmaz).
- **Risk:** Indikator hesaplamasi look-ahead bias → backtest disiplini (§13 + research §6.4); partial bar sinyal yasak kontrol.

### S5 — Paper Trade (Dry-Run)

- **Scope:** Order place → `/api/v3/order/test` (Binance test endpoint) + `SystemEvents.PaperTradeCompleted` kaydi + kill-switch RiskCircuitBreaker live.
- **Done-definition:** `PlaceOrderCommandHandler` testnet'te `TestOrderAsync` cagrir (live `/order` degil); her strateji icin 30 gun paper-trade kayitli; walk-forward OOS raporu (Sharpe, MDD, WinRate).
- **Acceptance:** 30 gun testnet paper-trade + 3 strateji rapor (grid Sharpe > 0.5 MDD ≤ %5; trend win-rate > %30 MDD ≤ %20; mean-rev win-rate > %55 MDD ≤ %15); kill-switch simulasyon testi (force MaxDD %5.5) → tum strateji Deactivate + order Cancel → SystemEvent kaydi.
- **Risk:** Test endpoint response `/order/test` matching engine'e gitmez — fill simulasyonu backtest'te yapilir, paper-trade'de degil (clarify); 30 gun sabit sure: takvim bazli degil trade sayisi bazli (min 100 trade) alternatif ADR.

### S6 — Live Testnet Trade

- **Scope:** Gercek order place (testnet `POST /api/v3/order`) + RiskProfile circuit breaker live + audit log tam.
- **Done-definition:** `AllowMainnet=false` testnet'te canli order'lar; server-side `STOP_LOSS_LIMIT` her entry ile atilir; `CircuitBreakerTripped` tetiklendiginde 100% fanout.
- **Acceptance:** 30 gun testnet canli trade; MDD < %5; audit log 100% coverage (her order/position/strategy event SystemEvents'te); admin UI YOK (ADR-0007) — strategy activate/override Swagger/.http uzerinden yapildi.
- **Risk:** Testnet bakiye tukenmesi → aylik reset aware; ApiKey prod kacisi (ADR-0006 §6.3) — code review zorunlu; ADR-0009 hala yazilmadiysa mainnet kapisi acilmaz.

**Kaynak kararlar:** ADR-0001..0007; `.ai-trace/decisions.jsonl` seq#TBD-log-gelince-guncel.

---

## 15. Acik Sorular + Risk Kayitlari (Bilinen Bosluk + Faz-2)

### 15.1 Reviewer Nit Listesi (14 Adet — Known-Gap Registry)

| Nit | Bolum | Konu | Cozum Sinifi |
|---|---|---|---|
| A2 | A | `appsettings.json ConnectionStrings.Default` → bos string (cozuldu A2 patch) | **Plan'a alindi** |
| A3 | A | `localStorage.getItem('admin.key')` → ADR-0007 referansi (cozuldu A3 patch) | **Plan'a alindi** |
| A4 | A | `NetEscapades.SecurityHeaders` MVP zorunlu + paket major<1 stabilite notu | **Plan'a alindi + kayit** |
| B2 | B | `StrategyParameters` VO JSON-backed switch-by-type OCP riski → discriminated union Faz-2 | Known-gap |
| B3 | B | Strategy aggregate SRP patlamasi: `StrategySignal` ayri aggregate / append-only log olabilir | Known-gap |
| C1 | C | `KeepAliveInterval=TimeSpan.Zero` netlestirildi (cozuldu C1 patch) | **Plan'a alindi** |
| C2 | C | User Data Stream reconnect akisi eklendi (cozuldu C2 patch) | **Plan'a alindi** |
| D2 | D | CORS policy explicit `Cors.Origins` whitelist (cozuldu D2 patch) | **Plan'a alindi** |
| D3 | D | CSP header detay: `script-src 'self' cdn.jsdelivr.net unpkg.com` (cozuldu D3 patch) | **Plan'a alindi** |
| D4 | D | `v-html YASAK` disiplini acik yazildi (cozuldu D4 patch) | **Plan'a alindi** |
| E1 | E | `BookTicker aggregate-olmama` kesin karar; senaryo tahmini silindi | **Plan'a alindi** |
| E2 | E | "Fleet buyurse test csproj bolunme" → MVP scope disi, Faz-2 ADR konusu | Known-gap |
| E3 | E | "Ileride PostgreSQL'e gecis" → Faz-2 ADR-0010 konusu | Known-gap |
| G2 | G | WS state machine planned/unplanned transition ayri (cozuldu G2 patch) | **Plan'a alindi** |

### 15.2 Faz-2 ADR Beklenenleri

| ADR | Konu | Tetik |
|---|---|---|
| **0008** | Transactional outbox pattern | MediatR in-memory bus crash-recovery → yari-gonderilmis event kaybi ciddilesirse |
| **0009** | Production auth model (JWT + CSRF double-submit; OR cookie HttpOnly) | `AllowMainnet=true` icin onsart; admin endpoint'ler ADR-0007 nafile kapi reset |
| **0010** | PostgreSQL / TimescaleDB migration | MSSQL license / time-series performance ihtiyaci |
| **0011** | Multi-symbol + Futures ekleme | Spot sembol evreni genisler veya Funding arbitrage istenirse |
| **SignalR/SSE (ayri ADR)** | Real-time push katmani | Polling latency MVP yetmezligi |
| **Supply-chain/SRI** | importmap integrity + vendor commit plani | CDN compromise senaryosu |
| **Prometheus /metrics** | Dashboard metrics endpoint | Grafana/Seq dashboard gereksinimi |

### 15.3 Risk Kayitlari

- **Binance testnet kararliligi** — testnet zaman zaman kapanir; REST warmup ve WS reconnect disiplini bunu kisir; kritik test 30 gun sure zarfi uzatilabilir.
- **Solo-dev workspace dependency** — 7 agent + MCP + hook zinciri karmasik; tek kullanicili kullanimda degil ama tekil arizada zincir kopabilir. `.ai-trace` + handoffs zinciri reconvery saglar.
- **API key production kacisi** — ADR-0006 §6.3 kilit; ama kullanici yanlislikla mainnet key user-secrets'a koyarsa + `AllowMainnet=true` flag'i atarsa prod'a gidilir. Kontrol: code review zorunlu (reviewer agent blocker); hook prod ApiKey prefix sezinler → uyari.
- **Paper trade → live trade gecis** — S5 tamamlandiginda PaperTradeCompleted flag true; ama Strategy'nin paper-trade performansinin live'a garantisi yok (overfitting riski). Walk-forward OOS zorunlu.
- **Testnet bakiye aylik reset** — `API key korunur` ama bakiye sifirlanir; `SymbolFiltersRefresher` + account balance fetch boot'ta reconcile.

**Kaynak kararlar:** plan-review-notes.md §7 blocker + §14 nit; `.ai-trace/decisions.jsonl` seq#TBD-log-gelince-guncel.

---

## 16. Kaynaklar

### 16.1 Arastirma URL'leri

**Binance Resmi:**
- https://raw.githubusercontent.com/binance/binance-spot-api-docs/master/rest-api.md
- https://raw.githubusercontent.com/binance/binance-spot-api-docs/master/web-socket-streams.md
- https://raw.githubusercontent.com/binance/binance-spot-api-docs/master/user-data-stream.md
- https://raw.githubusercontent.com/binance/binance-spot-api-docs/master/filters.md
- https://raw.githubusercontent.com/binance/binance-spot-api-docs/master/enums.md
- https://raw.githubusercontent.com/binance/binance-spot-api-docs/master/errors.md
- https://developers.binance.com/docs/binance-spot-api-docs
- https://testnet.binance.vision/
- https://www.binance.com/en/fee/trading
- https://www.binance.com/en/academy/articles/what-are-binance-websocket-limits

**.NET Platform:**
- https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/
- https://learn.microsoft.com/en-us/dotnet/standard/threading/channels
- https://learn.microsoft.com/en-us/dotnet/core/resilience/http-resilience
- https://learn.microsoft.com/en-us/dotnet/core/extensions/workers
- https://learn.microsoft.com/en-us/dotnet/core/extensions/options
- https://learn.microsoft.com/en-us/aspnet/core/security/authentication/
- https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery

**Framework / Library:**
- https://github.com/jasontaylordev/CleanArchitecture
- https://github.com/ardalis/CleanArchitecture
- https://github.com/ardalis/Result
- https://github.com/jbogard/MediatR
- https://docs.fluentvalidation.net/
- https://github.com/freqtrade/freqtrade
- https://github.com/jesse-ai/jesse
- https://github.com/hummingbot/hummingbot
- https://github.com/ccxt/ccxt

**Akademik / Sektor:**
- https://www.sciencedirect.com/science/article/abs/pii/S1062940821000590
- https://assets.super.so/e46b77e7-ee08-445e-b43f-4ffd88ae0a0e/files/9c27aa78-9b14-4419-a53d-bc56fa9d43b2.pdf
- https://www.sciencedirect.com/science/article/pii/S154461232401537X
- https://www.mdpi.com/1911-8074/18/3/124
- https://acfr.aut.ac.nz/__data/assets/pdf_file/0009/686754/6b-Tim-Baumgartner-May19.pdf
- https://www.sciencedirect.com/science/article/abs/pii/S0950705124011110
- https://blog.quantinsti.com/walk-forward-optimization-introduction/

**Frontend:**
- https://vuejs.org/guide/quick-start.html#using-vue-from-cdn
- https://developer.mozilla.org/en-US/docs/Web/HTML/Element/script/type/importmap
- https://tradingview.github.io/lightweight-charts/
- https://date-fns.org/
- https://cheatsheetseries.owasp.org/cheatsheets/Cross_Site_Scripting_Prevention_Cheat_Sheet.html

### 16.2 ADR Referanslari

- [ADR-0001 — Auto Migration On Startup](./adr/0001-auto-migration-on-startup.md)
- [ADR-0002 — Binance WS Supervisor Pattern](./adr/0002-binance-ws-supervisor-pattern.md)
- [ADR-0003 — Idempotent Handler Discipline](./adr/0003-idempotent-handler-discipline.md)
- [ADR-0004 — Secret Management](./adr/0004-secret-management.md)
- [ADR-0005 — Risk Limit Policy](./adr/0005-risk-limit-policy.md)
- [ADR-0006 — Testnet-First Policy](./adr/0006-testnet-first-policy.md)
- [ADR-0007 — Admin Auth Model](./adr/0007-admin-auth-model.md) (bu plan ile yazildi)

### 16.3 Ic Doküman Referanslari

- [docs/research/binance-research.md](./research/binance-research.md)
- [docs/architecture-notes.md](./architecture-notes.md)
- [docs/backend-design.md](./backend-design.md)
- [docs/frontend-design.md](./frontend-design.md)
- [docs/plan-review-notes.md](./plan-review-notes.md)
- [docs/glossary.md](./glossary.md)
- [docs/CLAUDE.md](./CLAUDE.md)
- [CLAUDE.md (kök)](../CLAUDE.md)

### 16.4 `.ai-trace` Audit

- `.ai-trace/decisions.jsonl` — agent karar log'u (classifier outage: `seq#TBD-log-gelince-guncel`)
- `.ai-trace/handoffs.jsonl` — PM handoff log'u
- `.ai-trace/user-notes.jsonl` — kullanici notlari
- `.ai-trace/subagent-stops/*.md` — subagent stop hook ciktilari

**Not:** Classifier outage nedeniyle `.ai-trace/decisions.jsonl` seq numaralari bu plan'da "`seq#TBD-log-gelince-guncel`" placeholder'i ile kayitli; log'lar geri gelince PM manuel guncelleme yapar.
