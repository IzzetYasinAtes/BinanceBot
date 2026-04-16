# Architecture Notes — BinanceBot

**Durum:** One-shot master plan adim 2/6 (architect) + adim 6 final sentez patch'leri. `docs/plan.md` bolum 2, 3, 4 icin ham materyal.
**Kapsam:** Aggregate envanteri, Clean Architecture layer haritasi, CQRS command/query envanteri, Domain event haritasi, acik tasarim sorulari.
**Referanslar:** [docs/adr/0001..0007](./adr/), [docs/research/binance-research.md](./research/binance-research.md), [docs/glossary.md](./glossary.md).

---

## 1. Aggregate Envanteri

DDD kurali: **bir aggregate = bir transaction siniri**. Her aggregate root tek referans noktasi; child entity/VO lar root uzerinden erisilir. Repository `IRepository<TAggregateRoot>` pattern; repository-per-entity **YASAK** (CLAUDE.md).

**Aggregate disi kayitlar (audit / read-model):**
- **BookTicker** — read-model (aggregate degil). `dbo.vw_book_ticker_latest` view veya denormalized tablo; WS `@bookTicker` event'inden upsert.
- **SystemEvent** — audit tablosu (aggregate degil). Append-only log, domain davranisi yok, aggregate invariant yok. `Infrastructure/Persistence/Tables/SystemEvent` + yazici arayuzu `ISystemEventWriter` Application abstraction. ADR-0006 §6.2 Kapi-3 (PaperTradeCompleted), circuit breaker trip/reset, admin override, WS supervisor planned/unplanned transition gibi sistemsel olaylari tutar. Okuma: `TailSystemEventsQuery` (frontend `/api/logs/tail` endpoint'ini bundan besler). Plan-review blocker F4 gerekcesi: lifecycle yok + mutate yok + davranis yok + TTL/rotation farkli -> aggregate adayi degil, **infra audit tablosu**.

### 1.1 MarketData (Bounded Context)

MarketData ingestion + read-model bounded context idir. Ici birden cok aggregate bulundurur.

#### Kline (AggregateRoot)

| Alan | Tip | Kategori |
|---|---|---|
| `Id` | `long` (identity) | Surrogate key |
| `Symbol` | `Symbol` VO | Natural key #1 |
| `Interval` | `KlineInterval` enum | Natural key #2 |
| `OpenTime` | `DateTimeOffset` (UTC) | Natural key #3 |
| `CloseTime` | `DateTimeOffset` | |
| `Ohlcv` | `OhlcvValues` VO (Open/High/Low/Close/Volume/QuoteVolume) | Entity snapshot |
| `TradeCount` | `int` | |
| `Closed` | `bool` | Binance `k.x` flag |
| `UpdatedAt` | `DateTimeOffset` | Idempotency helper |

**Invariant:**
- `Closed == true` olunca bar immutable (sadece `Closed == false` state icindeyken `UpdateOngoing()` ile gun icinde degisim).
- `OpenTime` + `Interval` = `CloseTime` deterministik.
- `Ohlcv.High >= Ohlcv.Open && Ohlcv.High >= Ohlcv.Close` vb. sanity kurallari.

**Davranis metotlari:**
- `Kline.Ingest(Symbol, Interval, OpenTime, Ohlcv, Closed)` static factory — ilk yazim.
- `UpdateOngoing(OhlcvValues newOhlcv)` — sadece `Closed == false` iken izinli; aksi `Result.Invalid("closed kline is immutable")`.
- `MarkClosed(OhlcvValues finalOhlcv)` — state transition; `KlineClosed` domain event yayinlar.

**Transaction siniri:** Tek row upsert. `(Symbol, Interval, OpenTime)` UNIQUE index (bkz. [0003](./adr/0003-idempotent-handler-discipline.md)).

**Events:** `KlineIngested`, `KlineClosed`.

#### Trade (AggregateRoot — Karar gerekcesi asagida)

| Alan | Tip |
|---|---|
| `Id` | `long` |
| `Symbol` | `Symbol` VO |
| `TradeId` | `long` (Binance id) |
| `Price` | `Price` VO |
| `Qty` | `Quantity` VO |
| `QuoteQty` | `decimal` |
| `EventTime` | `DateTimeOffset` |
| `IsBuyerMaker` | `bool` |

**Karar: Trade ayri aggregate**, MarketData ic entity si degil. Gerekce:
- Lifecycle bagimsiz — Trade insert-only append log; Kline open/close cycle i ile bagimsiz.
- Transaction sinirlari farkli — Kline yazim ile Trade yazim ayni TX te olmak zorunda degil.
- Volume buyuk — ayri tablo + ayri TTL policy (30 gun rotation).

**Invariant:** `TradeId` Binance idempotent; UNIQUE `(Symbol, TradeId)`.

**Davranis:** `Trade.Record(...)` static factory. Insert-only, mutate yok.

**Events:** `TradeRecorded` (opsiyonel, default emit etmez — volume yuksek, MediatR bus u bogar. Sadece strateji abone olursa toggle).

#### Depth (AggregateRoot)

| Alan | Tip |
|---|---|
| `Symbol` | `Symbol` VO (PK) |
| `LastUpdateId` | `long` |
| `Bids` | `List<DepthLevel>` entity |
| `Asks` | `List<DepthLevel>` entity |
| `SnapshotFetchedAt` | `DateTimeOffset` |

`DepthLevel` (entity): `Price`, `Qty`, `Side`. Ayri aggregate degil, Depth in parcasi.

**Karar: Depth kendi aggregate i.** Gerekce: `Bids`/`Asks` butunluk (snapshot atomik uygulanir); cross-aggregate referans yok. Depth asli tamamen infrastructure-driven (WS diff event leri handler ile uygulanir), domain davranisi minimal.

**Invariant:**
- `LastUpdateId` monoton artan.
- `bids` descending fiyat, `asks` ascending.
- Snapshot resync sonrasi local book = snapshot. (binance-research.md §2.4)

**Davranis:**
- `Depth.ApplySnapshot(lastUpdateId, bids, asks)` — full reset.
- `Depth.ApplyDiff(firstUpdateId, finalUpdateId, bidDeltas, askDeltas)` — U/u validation. Gap varsa `Result.Invalid("gap detected, resync required")`.

**Events:** `DepthSnapshotRefreshed`, `DepthGapDetected` (supervisor bu event i alip snapshot cagirir).

#### BookTicker (Aggregate mi? Hayir — Read Model)

Karar: **BookTicker aggregate DEĞİL**, bir **read-model** olarak MarketData read-side tablosunda. Gerekce: Lifecycle i yok, mutate yok, sadece "son bid/ask" cache. Domain davranisi yok.
- MSSQL de `dbo.vw_book_ticker_latest` view'i veya denormalized tablo.
- WS `@bookTicker` event geldikce upsert edilir; domain event yayinlamaz.
- Read-side, Application `GetBookTickerQuery` ile okur.

(Not: E1 nit — "book-ticker farki bazli domain davranisi" senaryo tahmini silindi; lifecycle/domain davranisi ortaya cikarsa `docs/features/` slice'i ile ele alinir, burada kapsam disi.)

### 1.2 Instrument (Aggregate)

`Instrument` aggregate i Binance sembol metadata + filter'lar + listing state tutar.

| Alan | Tip |
|---|---|
| `Id` | `long` |
| `Symbol` | `Symbol` VO |
| `Status` | `InstrumentStatus` enum (`Trading`, `Halt`, `Break`, `Delisted`) |
| `BaseAsset` | `string` |
| `QuoteAsset` | `string` |
| `Filters` | `SymbolFilters` VO |
| `IsSpotTradingAllowed` | `bool` |
| `UpdatedAt` | `DateTimeOffset` |

**`Symbol` VO:** `BaseAsset + QuoteAsset`, case-insensitive normalize `BTCUSDT`.
**`SymbolFilters` VO:** `TickSize`, `StepSize`, `MinQty`, `MaxQty`, `MinNotional`, `MaxNotional`, `MinPrice`, `MaxPrice`, `PercentPriceByUp`, `PercentPriceByDown`, `MaxNumOrders`, `MaxNumAlgoOrders`. Immutable; `Instrument.UpdateFilters(newFilters)` ile replace.

**Invariant:** Bir `Symbol` icin tek row; UNIQUE `(Symbol)`.

**Davranis:**
- `Instrument.List(Symbol, BaseAsset, QuoteAsset, filters)` static factory — `SymbolListed` event.
- `UpdateFilters(newFilters)` — `SymbolFiltersUpdated` event.
- `Halt()` / `Resume()` / `Delist()` — state transitions + events.

**Events:** `SymbolListed`, `SymbolFiltersUpdated`, `SymbolHalted`, `SymbolDelisted`.

### 1.3 Order (Aggregate)

Bir `Order` Binance taraflı bir lifecycle i tek roottan yonetir.

| Alan | Tip |
|---|---|
| `Id` | `long` |
| `ClientOrderId` | `ClientOrderId` VO (ULID) — UNIQUE |
| `ExchangeOrderId` | `long?` (Binance in `orderId`) — UNIQUE nullable |
| `Symbol` | `Symbol` VO |
| `Side` | `OrderSide` enum (`Buy`, `Sell`) |
| `Type` | `OrderType` enum |
| `Status` | `OrderStatus` enum (`New`, `PartiallyFilled`, `Filled`, `Canceled`, `Rejected`, `Expired`) |
| `Quantity` | `Quantity` VO |
| `Price` | `Price?` VO |
| `StopPrice` | `Price?` VO |
| `TimeInForce` | `TimeInForce` enum |
| `FilledQty` | `Quantity` (cumulative) |
| `AverageFillPrice` | `Price?` |
| `Fills` | `List<OrderFill>` entity |
| `PlacedAt` | `DateTimeOffset` |
| `UpdatedAt` | `DateTimeOffset` |
| `StrategyId` | `StrategyId?` (reference, cross-aggregate sadece by id) |

`OrderFill` (entity): `ExchangeTradeId`, `Price`, `Qty`, `Commission`, `CommissionAsset`, `FilledAt`. UNIQUE `(OrderId, ExchangeTradeId)`.

**Invariant:**
- `ClientOrderId` UNIQUE (idempotency - bkz. [0003](./adr/0003-idempotent-handler-discipline.md)).
- `FilledQty <= Quantity`.
- `Status == Filled` ise `FilledQty == Quantity` ve en az 1 fill var.
- State machine: `New -> PartiallyFilled -> Filled` veya `New -> Canceled` veya `New -> Rejected`.

**Davranis:**
- `Order.Place(Symbol, Side, Type, Qty, Price, StopPrice, Tif, StrategyId)` — `OrderPlaced` event.
- `MarkAcknowledged(exchangeOrderId)` — Binance ACK alindi.
- `RecordFill(OrderFill fill)` — FilledQty guncelle, `OrderPartiallyFilled` veya `OrderFilled` event.
- `Cancel(reason)` — `OrderCanceled` event.
- `Reject(reason, exchangeErrorCode)` — `OrderRejected` event.

**Transaction siniri:** Order + Fill list ayni aggregate icinde atomik update.

**Events:** `OrderPlaced`, `OrderAcknowledged`, `OrderPartiallyFilled`, `OrderFilled`, `OrderCanceled`, `OrderRejected`.

### 1.4 Position (Aggregate)

Spot ta "position" ozel bir kavram — Binance Spot da native position kavrami yok (futures'ta var), biz kendi book-keeping imizi tutuyoruz. Bir sembol icin net pozisyon = tum alis - tum satis.

| Alan | Tip |
|---|---|
| `Id` | `long` |
| `Symbol` | `Symbol` VO |
| `Status` | `PositionStatus` enum (`Open`, `Closed`) |
| `Quantity` | `Quantity` VO (signed: buy + / sell -) |
| `AverageEntryPrice` | `Price` VO |
| `RealizedPnl` | `decimal` |
| `UnrealizedPnl` | `decimal` (current mark price ile turetilir; persisted cache) |
| `OpenedAt` | `DateTimeOffset` |
| `ClosedAt` | `DateTimeOffset?` |
| `StrategyId` | `StrategyId?` |

**Invariant:**
- Bir `Symbol` icin tek `Open` pozisyon: UNIQUE `(Symbol) WHERE Status = 'Open'` (MSSQL filtered index).
- `Status == Closed` ise `Quantity == 0`.
- `AverageEntryPrice` weighted-average formulu ile guncellenir.

**Davranis:**
- `Position.Open(Symbol, firstFill)` — `PositionOpened` event.
- `Increase(fill)` — same-side fill; avg-entry recompute; `PositionIncreased` event.
- `Reduce(fill)` — opposite-side fill; realize PnL; `PositionReduced` event.
- `Close(finalFill)` — qty->0; `PositionClosed` event + final RealizedPnl.
- `MarkToMarket(currentPrice)` — UnrealizedPnl recompute; event yaymadan cache guncelle.

**Events:** `PositionOpened`, `PositionIncreased`, `PositionReduced`, `PositionClosed`.

### 1.5 Strategy (Aggregate)

| Alan | Tip |
|---|---|
| `Id` | `StrategyId` VO |
| `Name` | `string` |
| `Type` | `StrategyType` enum (`Grid`, `TrendFollow`, `MeanReversion`) |
| `Parameters` | `StrategyParameters` VO (JSON-backed; tip-safe extractor metotlari) |
| `Symbols` | `List<Symbol>` |
| `IsActive` | `bool` |
| `Status` | `StrategyStatus` enum (`Draft`, `Active`, `Paused`, `DeactivatedBySystem`) |
| `ActivatedAt` | `DateTimeOffset?` |
| `DeactivatedAt` | `DateTimeOffset?` |
| `Signals` | `List<StrategySignal>` entity |

`StrategySignal` (entity): `Id`, `BarOpenTime`, `Symbol`, `SignalType` (`Buy`/`Sell`/`Hold`), `Confidence`, `EmittedAt`. UNIQUE `(StrategyId, BarOpenTime, Symbol)`.

**Invariant:**
- `IsActive == true` iff `Status == Active`.
- `Parameters` validasyonu strategy type icin: `Grid` icin `LevelCount`, `SpreadPct`, `LowerBound`, `UpperBound`; `TrendFollow` icin `FastMA`, `SlowMA`, `AtrPeriod`, `StopMultiplier`; `MeanReversion` icin `RsiPeriod`, `BbPeriod`, `BbStdDev`.

**Davranis:**
- `Strategy.Create(Name, Type, Parameters, Symbols)` static factory.
- `UpdateParameters(newParams)` — `Active` iken yasak; once `Pause` gerekir.
- `Activate()` — RiskProfile.CircuitBreakerStatus `Tripped` ise `Result.Invalid`. `StrategyActivated` event.
- `Deactivate(reason)` — manual veya sistem (`reason in [UserRequest, CircuitBreaker, ConsecutiveLosses]`). `StrategyDeactivated` event.
- `EmitSignal(BarOpenTime, Symbol, SignalType, Confidence)` — UNIQUE check; `StrategySignalEmitted` event.

**Events:** `StrategyActivated`, `StrategyDeactivated`, `StrategyParametersUpdated`, `StrategySignalEmitted`.

### 1.6 RiskProfile (Aggregate)

Global tek-row aggregate. Uygulama acilista default bir tane seed eder.

| Alan | Tip |
|---|---|
| `Id` | `long` (sabit 1, singleton) |
| `RiskPerTradePct` | `Percentage` VO (default 0.01 = 1%) |
| `MaxPositionSizePct` | `Percentage` VO (default 0.10) |
| `MaxGrossExposurePct` | `Percentage` VO (default 0.30) |
| `MaxDrawdown24hPct` | `Percentage` VO (default 0.05) |
| `MaxDrawdownAllTimePct` | `Percentage` VO (default 0.25) |
| `MaxConsecutiveLosses` | `int` (default 3) |
| `CircuitBreakerStatus` | `CircuitBreakerStatus` enum (`Healthy`, `Warning`, `Tripped`, `Cooldown`) |
| `CircuitBreakerTrippedAt` | `DateTimeOffset?` |
| `ConsecutiveLossCount` | `int` |

**Invariant:**
- `RiskPerTradePct <= 0.02` (hard cap; admin `OverrideCaps` komutu ile degistirilebilir).
- `MaxPositionSizePct <= 0.20`.
- `CircuitBreakerStatus == Tripped` iken yeni trade yasak.

**Davranis:**
- `UpdateLimits(newValues)` — cap validasyonu + `RiskLimitUpdatedEvent`.
- `RecordTradeOutcome(pnl)` — PnL negatifse `ConsecutiveLossCount++`, pozitifse reset. Esiklere gore `TripCircuitBreaker(reason)`.
- `TripCircuitBreaker(reason)` — `CircuitBreakerTrippedEvent` + `RiskLimitBreachedEvent`.
- `ResetCircuitBreaker()` — status `Cooldown`; 1h sonra `Healthy` (time-based transition ayri bg job).
- `OverrideCaps(newCaps, adminNote)` — audit-tracked.

**Events:** `RiskLimitUpdated`, `RiskLimitBreached`, `CircuitBreakerTripped`, `CircuitBreakerReset`.

### 1.7 BacktestRun (Aggregate)

| Alan | Tip |
|---|---|
| `Id` | `long` |
| `StrategyId` | `StrategyId` |
| `Period` | `DateRange` VO |
| `Symbols` | `List<Symbol>` |
| `InitialBalance` | `decimal` |
| `Parameters` | `StrategyParameters` VO (snapshot) |
| `Status` | `BacktestStatus` enum (`Queued`, `Running`, `Completed`, `Failed`) |
| `Trades` | `List<BacktestTrade>` entity |
| `FinalBalance` | `decimal?` |
| `Metrics` | `BacktestMetrics` VO (TotalPnl, SharpeRatio, MaxDrawdown, WinRate, TradeCount, AverageTradePnl) |

`BacktestTrade` (entity): `EntryTime`, `ExitTime`, `Symbol`, `Side`, `EntryPrice`, `ExitPrice`, `Qty`, `Pnl`, `Reason`.

**Invariant:**
- `Status == Completed` iff `Metrics != null`.
- `Parameters` immutable post-start.

**Davranis:**
- `BacktestRun.Schedule(StrategyId, period, symbols, initialBalance, parameters)` — `BacktestStarted` event.
- `RecordTrade(trade)` — Running iken izinli.
- `Complete(metrics, finalBalance)` — `BacktestCompleted` event.
- `Fail(reason)` — `BacktestFailed` event.

**Events:** `BacktestStarted`, `BacktestTradeRecorded` (opsiyonel, volume icin toggle), `BacktestCompleted`, `BacktestFailed`.

---

## 2. Clean Architecture Layer Haritasi

**Bagimlilik yonu:** `Api -> Application -> Domain <- Infrastructure`. Domain hicbir sey import etmez. Infrastructure Domain'in abstract larini implement eder.

| Layer | Sorumluluk | Icerir | YASAK |
|---|---|---|---|
| **Domain** (`src/Domain`) | Aggregate, Entity, ValueObject, DomainEvent, Invariant kontrol, domain service interface leri | Aggregate roots (Kline, Instrument, Order, Position, Strategy, RiskProfile, BacktestRun, Trade, Depth), VO lar (Symbol, Price, Quantity, OhlcvValues, SymbolFilters, ClientOrderId, Percentage, DateRange, StrategyParameters), enum lar (OrderSide, OrderType, TimeInForce, KlineInterval, CircuitBreakerStatus, StrategyType, ...), domain event records, `IAggregateRoot` marker, `IClock` / `IStrategyEvaluator` interfaces | EF Core, MediatR, System.Net.Http, FluentValidation, ASP.NET, DI container — **hic bir sey import etmez**. Ardalis.Result kullanimi kabul (value-type library, domain-agnostic). |
| **Application** (`src/Application`) | Use case orkestrasyonu: Command/Query handler lar, Validator lar, DTO -> domain mapping, Port interface leri, Domain event handler (`INotificationHandler`) | MediatR handler lar (`{Action}{Aggregate}Command/Query` + `Handler`), FluentValidation validator lar, Port interface leri (`IBinanceMarketData`, `IBinanceTrading`, `IBinanceAccount`, `IInstrumentRepository`, `IBinanceWsSubscriber`, `IClock`, `IStrategyEvaluator`, `IStopLossPlacer`, `ISystemEventWriter`), notification handler lar, `Result<T>` return tipi | Concrete EF Core DbContext, concrete HttpClient, `Microsoft.Data.SqlClient`, WebSocket kutuphaneleri |
| **Infrastructure** (`src/Infrastructure`) | Persistence, external API, adapter implementasyonlar, BackgroundService lar | `AppDbContext` (EF Core), `IEntityTypeConfiguration` ler, `{Aggregate}Repository` implementasyonlar (aggregate-per-repo), `BinanceMarketDataClient` + `BinanceTradingClient` + `BinanceAccountClient` (IHttpClientFactory), `BinanceWsSupervisor` (BackgroundService), `DepthBookSynchronizer`, `StopLossPlacer`, `StrategyEvaluator`, `SystemEventWriter`, `Polly` resilience pipeline config, migration lar, seed service | Domain e ters bagimlilik (Domain, Infrastructure'i bilmez). Api layer'a bagimli olmak. |
| **Api** (`src/Api`) | HTTP endpoint, DI composition root, middleware, DTO marshalling, auth, logging, boot-time guards | Minimal API endpoints veya Controllers, `Program.cs` DI register + `app.Run()`, `StartupSecretValidator`, `StartupBinanceEnvironmentGuard`, `ApiKeyAuthenticationHandler` (ADR-0007), CORS, global exception handler, serilog config, health checks | Domain'e direkt erisim (sadece Application command/query uzerinden). EF DbContext'i direkt kullanmak. |

### 2.1 Proje Referans Grafigi

```
Api.csproj
  -> Application.csproj
  -> Infrastructure.csproj
  -> Domain.csproj (transitif)

Application.csproj
  -> Domain.csproj

Infrastructure.csproj
  -> Application.csproj (port interface leri icin)
  -> Domain.csproj
```

Api Infrastructure a **direkt referans** tutar cunku DI container da concrete registration yapar (`services.AddDbContext<AppDbContext>()`, `services.AddHostedService<BinanceWsSupervisor>()`). Bu, Clean Architecture kitabindaki "Composition Root" kalibidir.

### 2.2 Test Layer

`tests/Tests.csproj` — her layer a test projesi bolunecek mi? MVP: tek csproj, namespace bazli klasor (`Tests/Domain/...`, `Tests/Application/...`, `Tests/Infrastructure/...`, `Tests/Api/...`). Bolunme MVP **NOT_IN_SCOPE**; fleet buyurse Faz-2 ADR konusu (E2 nit kayit altinda).

---

## 3. CQRS Command/Query Envanteri

Her slice MediatR in `IRequest<Result>` veya `IRequest<Result<TDto>>` formatindadir. `record` syntax i + primary constructor. Handler path: `src/Application/<Aggregate>/<Commands|Queries>/<Name>{Command|Query}Handler.cs`. Validator: `<Name>{Command|Query}Validator.cs`. Auth: `[Authorize(Policy = "...")]` endpoint duzeyinde (Api katmaninda); Application handler kendi auth kontrolu yapmaz (DI ile `ICurrentUser` alir).

**Not:** `[Auth]` sutununda `Anon` = anonymous izinli (public), `User` = kimlik dogrulamali herhangi bir kullanici, `Admin` = admin rolu (ADR-0007: `X-Admin-Key` header; frontend admin UI yok), `Internal` = sadece internal komponent (Supervisor/handler lar — endpoint ile acik degil).

### 3.1 MarketData

| Slice | Tip | Handler path | Record signature | Validator kurallari | Auth |
|---|---|---|---|---|---|
| `IngestKlineCommand` | Cmd | `Application/MarketData/Commands/IngestKline/IngestKlineCommandHandler.cs` | `record IngestKlineCommand(Symbol Symbol, KlineInterval Interval, DateTimeOffset OpenTime, DateTimeOffset CloseTime, OhlcvValues Ohlcv, int TradeCount, bool Closed) : IRequest<Result>` | Symbol non-empty; Interval defined enum; OpenTime < CloseTime; Ohlcv.High >= Ohlcv.Low; Volume >= 0 | `Internal` (WS supervisor tetikler) |
| `SyncDepthSnapshotCommand` | Cmd | `Application/MarketData/Commands/SyncDepthSnapshot/SyncDepthSnapshotCommandHandler.cs` | `record SyncDepthSnapshotCommand(Symbol Symbol, int Limit) : IRequest<Result>` | Symbol non-empty; Limit in [5,10,20,50,100,500,1000,5000] | `Internal` |
| `RecordTradeCommand` | Cmd | `Application/MarketData/Commands/RecordTrade/...` | `record RecordTradeCommand(Symbol Symbol, long TradeId, Price Price, Quantity Qty, decimal QuoteQty, DateTimeOffset EventTime, bool IsBuyerMaker) : IRequest<Result>` | TradeId > 0; Price > 0; Qty > 0 | `Internal` |
| `GetLatestKlinesQuery` | Qry | `Application/MarketData/Queries/GetLatestKlines/...` | `record GetLatestKlinesQuery(Symbol Symbol, KlineInterval Interval, int Count) : IRequest<Result<IReadOnlyList<KlineDto>>>` | Count in [1..1000] | `User` |
| `GetDepthSnapshotQuery` | Qry | `Application/MarketData/Queries/GetDepthSnapshot/...` | `record GetDepthSnapshotQuery(Symbol Symbol, int Depth) : IRequest<Result<DepthSnapshotDto>>` | Depth in [5,10,20,50,100] | `User` |
| `GetBookTickerQuery` | Qry | `Application/MarketData/Queries/GetBookTicker/...` | `record GetBookTickerQuery(Symbol Symbol) : IRequest<Result<BookTickerDto>>` | Symbol non-empty | `User` |
| **`GetMarketSummaryQuery`** (F1 eklendi) | Qry | `Application/MarketData/Queries/GetMarketSummary/...` | `record GetMarketSummaryQuery(IReadOnlyList<Symbol> Symbols) : IRequest<Result<IReadOnlyList<MarketSummaryDto>>>` | Symbols 1..10 arasi; her biri non-empty | `User` |

`MarketSummaryDto`: `{ Symbol, LastPrice, PriceChange24h, PriceChangePct24h, Volume24h, BestBid, BestAsk }` — 3 sembol icin read-model projection (BookTicker + 24hr ticker agrega).

### 3.2 Instrument

| Slice | Tip | Record signature | Validator | Auth |
|---|---|---|---|---|
| `RefreshSymbolFiltersCommand` | Cmd | `record RefreshSymbolFiltersCommand() : IRequest<Result<int>>` (refreshed count) | — | `Admin` (veya cron internal) |
| `ListSymbolCommand` | Cmd | `record ListSymbolCommand(Symbol Symbol, string BaseAsset, string QuoteAsset, SymbolFilters Filters) : IRequest<Result>` | Symbol unique; Filters valid | `Admin` |
| `HaltSymbolCommand` | Cmd | `record HaltSymbolCommand(Symbol Symbol, string Reason) : IRequest<Result>` | Symbol exists, Status == Trading | `Admin` |
| `GetSymbolFiltersQuery` | Qry | `record GetSymbolFiltersQuery(Symbol Symbol) : IRequest<Result<SymbolFiltersDto>>` | Symbol non-empty | `User` |
| `ListActiveSymbolsQuery` | Qry | `record ListActiveSymbolsQuery() : IRequest<Result<IReadOnlyList<InstrumentDto>>>` | — | `User` |

### 3.3 Order

| Slice | Tip | Record signature | Validator | Auth |
|---|---|---|---|---|
| `PlaceOrderCommand` | Cmd | `record PlaceOrderCommand(Symbol Symbol, OrderSide Side, OrderType Type, Quantity Qty, Price? LimitPrice, Price? StopPrice, TimeInForce Tif, StrategyId? StrategyId) : IRequest<Result<PlacedOrderDto>>` | Symbol exists; Qty > 0 (stepSize multiple); LimitPrice tickSize multiple; MIN_NOTIONAL; RiskProfile gate (bkz. ADR 0005); TimeInForce gerekli ise (LIMIT type) | `User` (manuel) veya `Internal` (strateji) |
| `CancelOrderCommand` | Cmd | `record CancelOrderCommand(ClientOrderId ClientOrderId) : IRequest<Result>` | Order exists, Status in [New, PartiallyFilled] | `User`/`Internal` |
| `RegisterOrderFillCommand` | Cmd | `record RegisterOrderFillCommand(ClientOrderId ClientOrderId, long ExchangeTradeId, Price Price, Quantity Qty, decimal Commission, string CommissionAsset, DateTimeOffset FilledAt) : IRequest<Result>` | Order exists; Qty <= (Order.Qty - FilledQty) | `Internal` (user-data stream handler) |
| `GetOrderByClientIdQuery` | Qry | `record GetOrderByClientIdQuery(ClientOrderId ClientOrderId) : IRequest<Result<OrderDto>>` | ClientOrderId format valid | `User` |
| `ListOpenOrdersQuery` | Qry | `record ListOpenOrdersQuery(Symbol? Symbol) : IRequest<Result<IReadOnlyList<OrderDto>>>` | — | `User` |
| `ListOrderHistoryQuery` | Qry | `record ListOrderHistoryQuery(Symbol? Symbol, DateTimeOffset? From, DateTimeOffset? To, int Skip, int Take) : IRequest<Result<PagedResult<OrderDto>>>` | Take in [1..200] | `User` |

### 3.4 Position

| Slice | Tip | Record signature | Validator | Auth |
|---|---|---|---|---|
| `OpenPositionCommand` | Cmd | `record OpenPositionCommand(Symbol Symbol, OrderFill Fill, StrategyId? StrategyId) : IRequest<Result>` | No open position for Symbol; Fill.Qty > 0 | `Internal` (OrderFilledNotification handler tetikler) |
| `UpdatePositionCommand` | Cmd | `record UpdatePositionCommand(Symbol Symbol, OrderFill Fill) : IRequest<Result>` | Open position exists | `Internal` |
| `ClosePositionCommand` | Cmd | `record ClosePositionCommand(Symbol Symbol, string Reason) : IRequest<Result<ClosedPositionDto>>` | Open position exists | `User`/`Internal` |
| `MarkToMarketCommand` | Cmd | `record MarkToMarketCommand(Symbol Symbol, Price CurrentPrice) : IRequest<Result>` | Position open | `Internal` (bookTicker handler) |
| **`ListPositionsQuery`** (F3 supersede GetOpenPositionsQuery) | Qry | `record ListPositionsQuery(PositionStatus Status, Symbol? Symbol, DateTimeOffset? From, DateTimeOffset? To) : IRequest<Result<IReadOnlyList<PositionDto>>>` | Status defined enum; From<To | `User` |
| `GetPositionPnlQuery` | Qry | `record GetPositionPnlQuery(Symbol Symbol) : IRequest<Result<PositionPnlDto>>` | — | `User` |
| **`GetTodayPnlQuery`** (F1 eklendi) | Qry | `Application/Positions/Queries/GetTodayPnl/...` | `record GetTodayPnlQuery() : IRequest<Result<TodayPnlDto>>` | — | `User` |

`TodayPnlDto`: `{ RealizedToday, UnrealizedTotal, OpenPositionCount, ClosedTodayCount }`; closed positions today (UTC gun) realized + open unrealized toplam.

Not: `GetOpenPositionsQuery()` **supersede edildi**; yerine `ListPositionsQuery(Status=Open, ...)` tek slice.

### 3.5 Strategy

| Slice | Tip | Record signature | Validator | Auth |
|---|---|---|---|---|
| `CreateStrategyCommand` | Cmd | `record CreateStrategyCommand(string Name, StrategyType Type, StrategyParameters Parameters, IReadOnlyList<Symbol> Symbols) : IRequest<Result<StrategyId>>` | Type-specific parameter schema; Symbols non-empty | `Admin` |
| `ActivateStrategyCommand` | Cmd | `record ActivateStrategyCommand(StrategyId Id) : IRequest<Result>` | Strategy in Draft/Paused; RiskProfile healthy | `Admin` |
| `DeactivateStrategyCommand` | Cmd | `record DeactivateStrategyCommand(StrategyId Id, string Reason) : IRequest<Result>` | Strategy Active | `Admin`/`Internal` (auto-deact) |
| `UpdateStrategyParametersCommand` | Cmd | `record UpdateStrategyParametersCommand(StrategyId Id, StrategyParameters NewParams) : IRequest<Result>` | Strategy not Active (must pause first) | `Admin` |
| `EmitStrategySignalCommand` | Cmd | `record EmitStrategySignalCommand(StrategyId Id, DateTimeOffset BarOpenTime, Symbol Symbol, SignalType SignalType, decimal Confidence) : IRequest<Result>` | UNIQUE `(StrategyId, BarOpenTime, Symbol)` | `Internal` |
| `ListStrategiesQuery` | Qry | `record ListStrategiesQuery(StrategyStatus? Status) : IRequest<Result<IReadOnlyList<StrategyDto>>>` | — | `User` |
| **`GetStrategyDetailQuery`** (F1 eklendi) | Qry | `Application/Strategies/Queries/GetStrategyDetail/...` | `record GetStrategyDetailQuery(StrategyId Id) : IRequest<Result<StrategyDetailDto>>` | Id non-empty ULID | `User` |
| `GetStrategySignalsQuery` | Qry | `record GetStrategySignalsQuery(StrategyId Id, DateTimeOffset From, DateTimeOffset To) : IRequest<Result<IReadOnlyList<StrategySignalDto>>>` | From < To | `User` |

`StrategyDetailDto`: `StrategyDto` alanlari + `RecentSignals` (son 50 signal) + parametre schema.

### 3.6 RiskProfile

| Slice | Tip | Record signature | Validator | Auth |
|---|---|---|---|---|
| `UpdateRiskProfileCommand` | Cmd | `record UpdateRiskProfileCommand(Percentage? RiskPerTradePct, Percentage? MaxPositionSizePct, Percentage? MaxDrawdown24hPct, int? MaxConsecutiveLosses) : IRequest<Result>` | RiskPerTrade <= 0.02; MaxPosition <= 0.20 | `Admin` |
| `OverrideRiskCapsCommand` | Cmd | `record OverrideRiskCapsCommand(Percentage RiskPerTradeCap, Percentage MaxPositionCap, string AdminNote) : IRequest<Result>` | AdminNote non-empty; Cap > 0 | `Admin` |
| `ResetCircuitBreakerCommand` | Cmd | `record ResetCircuitBreakerCommand(string AdminNote) : IRequest<Result>` | Status in [Tripped, Cooldown] | `Admin` |
| `RecordTradeOutcomeCommand` | Cmd | `record RecordTradeOutcomeCommand(decimal Pnl) : IRequest<Result>` | — | `Internal` (PositionClosed handler) |
| `GetRiskProfileQuery` | Qry | `record GetRiskProfileQuery() : IRequest<Result<RiskProfileDto>>` | — | `User` |
| `GetCircuitBreakerStatusQuery` | Qry | `record GetCircuitBreakerStatusQuery() : IRequest<Result<CircuitBreakerStatusDto>>` | — | `User` |
| **`GetDrawdownHistoryQuery`** (F1 eklendi) | Qry | `Application/RiskProfiles/Queries/GetDrawdownHistory/...` | `record GetDrawdownHistoryQuery(int Days) : IRequest<Result<IReadOnlyList<DrawdownPointDto>>>` | Days in [1..365] | `User` |

`DrawdownPointDto`: `{ Date, Equity, Drawdown, DrawdownPct }` — gunluk zaman serisi.

### 3.7 BacktestRun

| Slice | Tip | Record signature | Validator | Auth |
|---|---|---|---|---|
| `StartBacktestCommand` | Cmd | `record StartBacktestCommand(StrategyId StrategyId, DateRange Period, IReadOnlyList<Symbol> Symbols, decimal InitialBalance, StrategyParameters Parameters) : IRequest<Result<long>>` (run id) | Period.From < Period.To; InitialBalance > 0; Parameters schema | `Admin` |
| `RecordBacktestTradeCommand` | Cmd | `record RecordBacktestTradeCommand(long RunId, BacktestTrade Trade) : IRequest<Result>` | RunId exists, Status Running | `Internal` |
| `CompleteBacktestCommand` | Cmd | `record CompleteBacktestCommand(long RunId, BacktestMetrics Metrics, decimal FinalBalance) : IRequest<Result>` | Status Running | `Internal` |
| `GetBacktestResultQuery` | Qry | `record GetBacktestResultQuery(long RunId) : IRequest<Result<BacktestResultDto>>` | — | `User` |
| `ListBacktestRunsQuery` | Qry | `record ListBacktestRunsQuery(StrategyId? StrategyId, BacktestStatus? Status, int Skip, int Take) : IRequest<Result<PagedResult<BacktestRunSummaryDto>>>` | Take in [1..100] | `User` |

### 3.8 System / Observability (F1 eklendi)

| Slice | Tip | Record signature | Validator | Auth |
|---|---|---|---|---|
| **`GetSystemStatusQuery`** | Qry | `Application/System/Queries/GetSystemStatus/...` | `record GetSystemStatusQuery() : IRequest<Result<SystemStatusDto>>` | — | `User` |
| **`TailSystemEventsQuery`** | Qry | `Application/System/Queries/TailSystemEvents/...` | `record TailSystemEventsQuery(DateTimeOffset? Since, string? Level, int Limit) : IRequest<Result<SystemEventTailDto>>` | Limit in [1..500]; Level in [Info, Warning, Error] (nullable) | `User` |

`SystemStatusDto`: `{ Env: "testnet"|"mainnet", AllowMainnet: bool, CircuitBreaker: string, WsSupervisorHeartbeatAt: ISO8601, AppVersion: string, LastMigration: string, PaperTradeCompleted: bool }`.

`SystemEventTailDto`: `{ Events: IReadOnlyList<{Timestamp, Level, Source, Message, Payload}>, NextSince: ISO8601 }` — `SystemEvent` audit tablosundan okur (Serilog dosya tail'i YASAK; SystemEvents tek kaynak).

### 3.9 Toplam Slice Sayimi (Plan.md §4 icin)

- 7 MarketData (6 eski + 1 GetMarketSummary) 
- 5 Instrument 
- 6 Order 
- 7 Position (4 Cmd + 3 Qry: ListPositions, GetPositionPnl, GetTodayPnl) — not: `GetOpenPositionsQuery` supersede edildi, sayi toplamda 1 artti
- 8 Strategy (5 Cmd + 3 Qry: ListStrategies, GetStrategyDetail, GetStrategySignals)
- 7 RiskProfile (4 Cmd + 3 Qry)
- 5 BacktestRun
- 2 System (GetSystemStatus, TailSystemEvents)

**Toplam = 47 slice** (eski 36'dan plan-review blocker F1-F3 cozumu sonrasi artti: +4 yeni query, +1 ListPositions genislemesi eski yerine ama sayilmiyor, +1 GetStrategyDetail, +2 System, +1 GetMarketSummary, +1 GetTodayPnl, +1 GetDrawdownHistory → net +11, toplam 47 yakin; plan.md §4'te tablo tek tek).

---

## 4. Domain Event Haritasi

Domain event ler `record` (immutable), past-tense adlandirma, `IDomainEvent` marker ile isaretli. Aggregate root `RaiseDomainEvent(event)` metodu kullanir; `SaveChangesAsync` cagrilmadan once / sonra MediatR `IPublisher` tarafindan dispatch. Transactional outbox `NOT_IN_SCOPE` simdilik (Faz-2 ADR-0008); in-memory MediatR bus.

### 4.1 Event Listesi ve Handler Haritasi

| Aggregate | Event | Payload (onemli alanlar) | Handler(lar) | Cross-Aggregate? |
|---|---|---|---|---|
| Kline | `KlineIngestedEvent` | Symbol, Interval, OpenTime, Closed | `StrategyEvaluatorHandler` (Closed==true ise) | Strategy aggregate ile konusur — ayri handler |
| Kline | `KlineClosedEvent` | Symbol, Interval, CloseTime, FinalOhlcv | `StrategyEvaluatorHandler` | - |
| Depth | `DepthSnapshotRefreshedEvent` | Symbol, LastUpdateId | `DepthReadModelProjectorHandler` | - |
| Depth | `DepthGapDetectedEvent` | Symbol | `DepthResyncHandler` (supervisor e resync tetikler) | - |
| Instrument | `SymbolListedEvent` | Symbol, BaseAsset, QuoteAsset | `WsSubscriptionHandler` (stream ekler) | - |
| Instrument | `SymbolFiltersUpdatedEvent` | Symbol, Filters | `OrderPlacementFilterCacheInvalidatorHandler` | Order placement cache refresh |
| Instrument | `SymbolHaltedEvent` | Symbol, Reason | `ActiveOrderCancelerHandler` | Order aggregate — acik order lar iptal |
| Order | `OrderPlacedEvent` | OrderId, ClientOrderId, Symbol, Side, Type, Qty | `BinanceOrderSubmitterHandler` (REST call), `AuditLogHandler` | Infrastructure side-effect |
| Order | `OrderAcknowledgedEvent` | OrderId, ExchangeOrderId | `AuditLogHandler` | - |
| Order | `OrderPartiallyFilledEvent` | OrderId, FilledQty, AverageFillPrice | `PositionUpdaterHandler`, `AuditLogHandler` | Position aggregate |
| Order | `OrderFilledEvent` | OrderId, Symbol, Side, Qty, AverageFillPrice, StrategyId | `PositionUpdaterHandler`, `StopLossPlacementHandler` (entry ise), `RiskCheckHandler`, `AuditLogHandler` | Position + RiskProfile + new Order |
| Order | `OrderCanceledEvent` | OrderId, Reason | `AuditLogHandler` | - |
| Order | `OrderRejectedEvent` | OrderId, Reason, ExchangeErrorCode | `StrategyErrorHandler`, `AuditLogHandler` | Strategy — pause if multiple rejects |
| Position | `PositionOpenedEvent` | Symbol, Qty, AverageEntryPrice, StrategyId | `PnlRecalcHandler` | - |
| Position | `PositionIncreasedEvent` | Symbol, NewQty, NewAverageEntry | `PnlRecalcHandler` | - |
| Position | `PositionReducedEvent` | Symbol, RealizedPnlDelta | `PnlRecalcHandler`, `RiskTradeOutcomeRecorderHandler` | RiskProfile — RecordTradeOutcome |
| Position | `PositionClosedEvent` | Symbol, RealizedPnl, StrategyId | `PnlRecalcHandler`, `RiskTradeOutcomeRecorderHandler`, `StrategyOutcomeRecorderHandler` | RiskProfile + Strategy |
| Strategy | `StrategyActivatedEvent` | StrategyId, Type, Symbols | `WsSubscriptionHandler` (ek stream varsa), `AuditLogHandler` | - |
| Strategy | `StrategyDeactivatedEvent` | StrategyId, Reason | `ActiveOrderCancelerHandler`, `AuditLogHandler` | Order aggregate |
| Strategy | `StrategySignalEmittedEvent` | StrategyId, Symbol, SignalType, BarOpenTime | `OrderPlacementHandler` (buy/sell ise) | Order aggregate |
| Strategy | `StrategyParametersUpdatedEvent` | StrategyId, OldParams, NewParams | `AuditLogHandler` | - |
| RiskProfile | `RiskLimitUpdatedEvent` | Field, OldValue, NewValue, AdminNote | `AuditLogHandler` | - |
| RiskProfile | `RiskLimitBreachedEvent` | LimitType, ActualValue, ThresholdValue | `CircuitBreakerTripperHandler` | Self (aynı aggregate next step) |
| RiskProfile | `CircuitBreakerTrippedEvent` | Reason, TrippedAt | `StrategyDeactivatorHandler`, `ActiveOrderCancelerHandler`, `AlarmHandler` | Tum Active Strategy lar + acik order lar |
| RiskProfile | `CircuitBreakerResetEvent` | ResetAt, AdminNote | `AuditLogHandler` | - |
| BacktestRun | `BacktestStartedEvent` | RunId, StrategyId, Period | `BacktestRunnerHandler` (baslatır worker) | - |
| BacktestRun | `BacktestCompletedEvent` | RunId, Metrics | `BacktestReportGeneratorHandler` | - |
| BacktestRun | `BacktestFailedEvent` | RunId, Reason | `AlarmHandler` | - |

`AuditLogHandler` her biri `ISystemEventWriter.Write(SystemEventLevel, source, payload)` cagrir — SystemEvent audit tablosuna yazar (aggregate degil, bkz. §1 bas). `AlarmHandler` Serilog Warning/Error + SystemEvent + opsiyonel webhook (MVP'de webhook NOT_IN_SCOPE).

### 4.2 Cross-Aggregate Coordination Ornekleri

**A. OrderFilled -> Position + RiskProfile (iki ayri TX)**

```
1. ExchangeUserDataStream event -> RegisterOrderFillCommand
2. Order aggregate: Order.RecordFill(fill) -> OrderFilledEvent raise
3. TX commit (Order aggregate)
4. MediatR publish OrderFilledEvent
5. PositionUpdaterHandler: UpdatePositionCommand -> Position aggregate (ayri TX)
6. Position.Reduce(fill) -> PositionReducedEvent raise (close durumunda PositionClosedEvent)
7. TX commit (Position)
8. MediatR publish PositionReducedEvent
9. RiskTradeOutcomeRecorderHandler: RecordTradeOutcomeCommand -> RiskProfile (ayri TX)
```

Her adim ayri TX; her handler idempotent (inbox guard ile — bkz. [0003](./adr/0003-idempotent-handler-discipline.md)).

**B. CircuitBreaker Tripped -> multi-fanout**

CircuitBreakerTrippedEvent tek yayinlanir; 3 ayri handler abone:
- `StrategyDeactivatorHandler`: tum Active strategy leri DeactivateStrategyCommand ile durdurur.
- `ActiveOrderCancelerHandler`: tum open order lari CancelOrderCommand.
- `AlarmHandler`: structured log + SystemEvent audit kaydi + opsiyonel webhook.

Her biri bagimsiz TX, bagimsiz retry.

**C. StopLossTriggered (Server-Side) — Bot Perspektifi**

Server-side `STOP_LOSS_LIMIT` tetiklendiginde bot icin bu **OrderFilledEvent** olarak gelir (user-data stream `executionReport` ile). Yeni bir `StopLossTriggeredEvent` yaratma; zaten `OrderFilledEvent` + `OrderType == StopLossLimit` flag i ile handler ayristirilabilir.

---

## 5. Acik Tasarim Sorulari — Cevaplanmis + Faz-2 Kaydi

Plan.md'nin sonunda cozulen + NOT_IN_SCOPE kaydi:

1. **Read-model separation** — Ayri read-model tablo/view (ornegin `vw_latest_book_ticker`, `vw_open_positions_pnl`). MVP: denormalized tablo/view tek sema icinde (Dbo.vw_*). Ileri ayrim Faz-2 ADR konusu (NOT_IN_SCOPE MVP).
2. **Transactional outbox** — MediatR in-memory bus. MVP'de kabul; Faz-2 **ADR-0008** transactional outbox pattern.
3. **User auth model** — **COZULDU (ADR-0007):** Solo-dev local kiosk mode; `X-Admin-Key` header Swagger/.http uzerinden; frontend admin UI yok. Production'a gecerken **ADR-0009** (JWT + CSRF).
4. **Backtest worker hosting** — MVP: ayni host icinde `IHostedService`. Veri hacmi patlarsa ayri worker process Faz-2 (NOT_IN_SCOPE).
5. **Strategy engine implementasyonu** — `IStrategyEvaluator` abstract port; concrete MVP'de C# indikator kutuphanesi (`Skender.Stock.Indicators` aday — backend-dev belirler). Python shelled-out reddedildi (deployment karmasasi).
6. **Frontend real-time push** — **COZULDU:** SignalR **NOT_IN_SCOPE** (MVP polling yeterli; push gereksinimi ortaya cikarsa **ADR-0009** degil ayri **ADR-0009-alt** degil de bu ayri kapsam — plan.md §4.1'de SignalR icin Faz-2 ADR yeri acik).
7. **Position mark-to-market frekansi** — BookTicker event her gelince mi, debounce 1s mi? MVP: **debounce 1s** (volume agir).
8. **Audit log** — **COZULDU:** `SystemEvent` audit tablosu (aggregate degil); yazici `ISystemEventWriter` Application abstraction. `TailSystemEventsQuery` okur.
9. **Clock source** — `IClock.UtcNow` uretim icin `SystemClock`, test icin fake. Binance server time drift `ClockSyncWorker` (saatte 1 cagri).
10. **Database provider migration** — MSSQL monolitik; MSSQL-ozel T-SQL minimize. PostgreSQL gecis Faz-2 **ADR-0010** (NOT_IN_SCOPE MVP).

---

## 6. Plan.md Icin Cikarim Ozeti

- **Section 2 (Mimari):** Clean Architecture 4 layer + bagimlilik yonu; aggregate envanteri (9 aggregate + BookTicker read-model + SystemEvent audit) + VO katalogu; ADR 0001-0007 referanslari.
- **Section 3 (Teknik Kararlar):** 7 ADR + Faz-2 ADR kayitlari.
- **Section 4 (CQRS Slice Katalogu):** ~47 slice (architecture-notes §3); handler fanout grafigi (§4).
- **Section 5+ (Uygulama Yol Haritasi):** Sprint haritasi plan.md §14'te.

Kaynaklar:
- [docs/adr/0001-0007](./adr/)
- [docs/research/binance-research.md](./research/binance-research.md)
- [docs/glossary.md](./glossary.md)
- [Microsoft — DDD oriented microservices](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/)
- [jasontaylordev/CleanArchitecture](https://github.com/jasontaylordev/CleanArchitecture)
- [ardalis/CleanArchitecture](https://github.com/ardalis/CleanArchitecture)
