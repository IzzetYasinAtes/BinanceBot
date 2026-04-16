# 0003. Idempotent Handler Disiplini

Date: 2026-04-16
Status: Accepted

## Context

Binance WS akisi + REST warmup hibrit mimarimiz (bkz. [0002](./0002-binance-ws-supervisor-pattern.md)) **at-least-once delivery** garantisi verir, **exactly-once** degil. Birden fazla kayit ayni event i tetikleyebilir:

- Reconnect sonrasi son N kline in REST warmup u -> WS de de ayni bar gelir (overlap).
- Preemptive reconnect penceresinde (~5 sn) ayni kline/bookTicker iki defa gelir.
- `executionReport` user-data stream'i crash-restart senaryosunda replay edilemez ama reconnect buffer ile ayni `clientOrderId` icin birden fazla event olabilir.
- Order `POST` suresince client network timeout olursa biz retry atabiliriz — Binance ise order i already-accepted gormus olabilir.

Bu olgusal at-least-once gerceginde, butun command/notification handler lari **idempotent** yazilmak zorundadir; aksi takdirde cift kline, cift trade, **cift order** uretimiyle PnL bozulur, risk limitleri yanilir.

## Decision

Idempotency dizeyi **database constraint + upsert + clientOrderId / eventId tekilligi** uzerinden kurulur. Her handler asagidaki politikaya uyar:

### 3.1 Constraint Matrisi

| Aggregate | Unique index (EF HasIndex IsUnique) | Upsert stratejisi |
|---|---|---|
| Kline | `(Symbol, Interval, OpenTime)` | EF Core 7+ `ExecuteUpdate` veya `context.Klines.Upsert` pattern. `x=false` iken update, `x=true` ilk upsert'te closed-once invariant. |
| Trade | `(Symbol, TradeId)` | INSERT IF NOT EXISTS; duplicate -> log debug, return `Result.Success`. |
| DepthSnapshot | `(Symbol, LastUpdateId)` | Snapshot in tek row olmasi beklenir; `lastUpdateId < existing.lastUpdateId` -> ignore. |
| Order | `(ClientOrderId)` UNIQUE + `(ExchangeOrderId)` UNIQUE nullable | `clientOrderId` uygulama taraflı uretilir (ULID/GUID-based), her retry ayni id ile gider -> Binance -2010 NEW_ORDER_REJECTED yerine idempotent yeniden gonderimde `duplicate order` -2013 alir ve bu hata **ignore** edilir. |
| OrderFill (entity) | `(OrderId, ExchangeTradeId)` UNIQUE | executionReport replay guvenli. |
| Position | `(Symbol)` — ayni anda tek open position / symbol | State transition `Open -> Increased -> Reduced -> Closed`; expected state mismatch -> `Result.Conflict`. |
| StrategySignal | `(StrategyId, BarOpenTime)` | Ayni bar icin ayni strateji tek sinyal. |

### 3.2 Command Handler Kurallari

1. **Natural key kontrolu**: handler cagrilinca ilk is, domain in natural key i ile var mi kontrolu. Yoksa insert, varsa merge/update. Exception cift-key insert ile atma — `DbUpdateException` ile control flow yasak (CLAUDE.md altin kural 5).
2. **ClientOrderId uretimi**: `OrderPlacementService` her yeni order icin `ULID` tabanli deterministic id. Retry policy (Polly) ayni id ile tekrar dener; Binance response unda `COID` eslesmesi duplicate tespiti.
3. **DB transaction sinir**: bir handler = bir aggregate = bir DB transaction (CLAUDE.md "aggregate boundary"). Cross-aggregate sync yazim yasak; tetikleme MediatR `INotification` ile ayri handler da.
4. **Event handler idempotency**: MediatR `INotificationHandler<OrderFilledEvent>` -> `PositionUpdaterHandler`. Handler, `OrderFill.Id` zaten process edilmis mi diye "processed_events" tablosuna bakar (outbox/inbox pattern in sadelestirilmis inbox tarafi). Ilk kez ise pozisyonu guncelle + inbox a insert; transactional.
5. **Loglama**: duplicate tespit edildiginde `ILogger.LogDebug("duplicate {EventType} {NaturalKey} ignored")`; WARN a cikartma — normal akis.

### 3.3 At-least-once Kabul Sozlesmesi

- Her command handler signature: `Task<Result>` — exception for control flow yok. Duplicate -> `Result.Success` (idempotent no-op).
- Her notification handler : `Task` — ancak icinde `processed_events` guard lı upsert.
- Outbox pattern MVP de **NOT_IN_SCOPE**; MediatR in-memory bus yeterli (bkz. opsiyonel ADR 0008). Sonraki fazda kafka/rabbit gelirse outbox zorunlu.

## Consequences

### Pozitif

- Duplicate event bozulma uretmez; WS reconnect, REST warmup, retry hepsi guvenli.
- PnL hesabi guvenli — cift fill yok.
- Order placement guvenli — network timeout ta cift order acilmaz.

### Negatif / Tradeoff

- Unique index lar insert latency sini hafif arttirir (MSSQL icin ihmal edilebilir, index lookup + B-tree).
- Inbox tablosu buyur; saatte bir background job `DELETE WHERE processed_at < now - 7d`.
- `ClientOrderId` i developer in uretmesi gerekir, Binance'e delege etmek yerine. ULID kutuphanesi bir nuget eklentisi (`NUlid` veya kendi implementasyon).

### Notr

- Bu disiplin testlerde de guncel: her handler testi "ayni command iki kez gonder -> ayni son state + tek side-effect" assertion lari icerir.

## Alternatifler

1. **Exactly-once messaging** (outbox + transactional outbox polling) — MVP icin over-engineering. `NOT_IN_SCOPE` simdilik.
2. **Distributed cache (Redis) ile dedup** — ekstra bagimlilik, MSSQL UNIQUE constraint yeterli.
3. **Binance in kendi `orderId` sini authoritative kabul et** — Order DB insert timing i Binance response dan sonra olursa network timeout ta ORDER-acik-ama-DB-kayit-yok senaryosu olusur. `ClientOrderId` authoritative yapmak bu deligi kapatir.

## Kaynak

- [docs/research/binance-research.md §2.6](../research/binance-research.md)
- [binance-spot-api-docs — errors.md](https://raw.githubusercontent.com/binance/binance-spot-api-docs/master/errors.md) — -2010 / -2013 duplicate order semantigi
- [Microsoft — EF Core unique indexes](https://learn.microsoft.com/en-us/ef/core/modeling/indexes)
- [Enterprise Integration Patterns — Idempotent Receiver](https://www.enterpriseintegrationpatterns.com/patterns/messaging/IdempotentReceiver.html)
- CLAUDE.md Altin Kural 5 (throwing for control flow yasak)
