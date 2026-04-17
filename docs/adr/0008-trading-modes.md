# 0008. Uc Yollu Trading Mode Fan-Out (Paper / LiveTestnet / LiveMainnet)

Date: 2026-04-17
Status: Accepted

> Not: [plan.md §15.2](../plan.md) Faz-2 siralamasinda "0008 = Transactional Outbox" olarak on-rezerve idi; outbox kaydi `0012`'ye otelendi. Bu ADR `0008` numarasini trading-mode fan-out tasarimina alir (kullanici onayli one-shot karar).

## Context

BinanceBot mevcut durumda `PlaceOrderCommand` tek bir execution backend bilir: `DryRun=true` ise `/api/v3/order/test`, degilse gercek `/api/v3/order`. PM iterasyon loop'unun (docs/plan.md §14 S5/S6) calisabilmesi icin ayni strateji sinyalinin uc ayri execution backend'inde es-zamanli degerlendirilip metrik uretmesi gerekiyor:

1. **Paper** — sanal bakiye (baslangic 100 USDT), komisyon + slippage + spread birebir simulasyon; PM loop'u 8 saat basi resetleyebilmeli.
2. **LiveTestnet** — gercek `POST /api/v3/order` cagrisi, `testnet.binance.vision`. Bugun icin API key yok → "disconnected" stub.
3. **LiveMainnet** — [ADR-0006](./0006-testnet-first-policy.md) ile uyumlu, asla gercek order atmayan permanent "blocked" stub. UI'da kirmizi BLOCKED rozeti.

User talebi acik: "3 farkli islem" — yani tek sinyalde DB'ye 3 ayri Order kaydi dussun, history uzerinden uc backend'in davranisi yan yana gorulsun. Mevcut tasarimdaki tekillikler bu gereksinimi kirar:

- `OrderConfiguration` UNIQUE(ClientOrderId) — ayni cid uc kere insert olamaz.
- `PositionConfiguration` UNIQUE(Symbol) WHERE Status=Open — ayni sembol icin ayni anda sadece 1 acik pozisyon.
- `RiskProfile` singleton (`Id=1`) — paper'in drawdown'u live'i bloklar.
- `StrategySignalToOrderHandler` tek `PlaceOrderCommand` uretir.

Bu ADR fan-out sinirlarini, idempotency anahtar semasini, guard kodunu ve migration sirasini **kod yazmadan** net tanimlar; `backend-dev` direkt uygulayacak.

## Decision

### 8.1 TradingMode Enum — Konum ve Sema

**Karar:** `TradingMode` enum `Domain/Common/TradingMode.cs` altinda. Yani **Option A — global concept**.

Gerekce: enum `Order`, `Position`, `VirtualBalance`, `RiskProfile` ve `SystemEvent` gibi birden cok aggregate tarafindan sorumluluk paylasilmadan kullanilir; Ubiquitous Language seviyesinde "platform-wide execution channel" kavrami; `Orders` namespace'ine kapatmak `Position` aggregate'inin `Orders`'a yanlis dependency'sine neden olur. Domain layer hicbir sey import etmedigi icin `Common` dogru yerdir — zaten `DomainException`, `AggregateRoot<T>`, `IClock` contract'lari orada.

Enum tanimi (3 sabit, integer backing):

```
Paper         = 1
LiveTestnet   = 2
LiveMainnet   = 3
```

Sirasi bilinclidir: seed row Id'leri enum integer'iyla **dogrudan eslesir** (VirtualBalance ve ModeScopedRiskProfile tarafinda). EF Core persistence `HasConversion<int>()`.

Global enum oldugu icin `Domain/Common/TradingMode.cs` dosya ismiyle singular; enum tipleri `Enum.TryParse` ile string round-trip (audit log + HTTP contract).

### 8.2 Order.Mode Alani ve ClientOrderId Semasi

**Karar:** `Order.Mode` alani **required** (non-nullable `TradingMode`). Default enum degeri (`Paper`) migration backfill'de kullanilir — sonradan nullable kalmaz.

**Karar:** `UNIQUE(ClientOrderId)` index **`UNIQUE(ClientOrderId, Mode)` composite'e donusur**. Filtered degil, full composite; cunku Binance tarafindaki cid'ler per-mode ayri hesap + ayri endpoint olduklari icin collision teknik olarak mumkun degil ama DB'nin bunu ifade etmesi **intent**-level kilit saglar.

**Karar:** ClientOrderId fan-out semasi suffix tabanli, 36 karakter sinirini asmayacak sekilde:

```
sig-{StrategyId}-{BarOpenUnix}-{ModeSuffix}
```

ModeSuffix:
- `p`  — Paper
- `lt` — LiveTestnet
- `lm` — LiveMainnet

Ornek: `sig-12-1744819200-p`, `sig-12-1744819200-lt`, `sig-12-1744819200-lm`. Tek karakterli `p` ve iki karakterli `lt`/`lm` secimi Binance client-order-id 36 karakter tavanini korur (max: `sig-<19 digit>-<10 digit>-lt` = 37 — gercekte `StrategyId` long olsa da aktif tasarimda 1-5 hane + `BarOpenUnix` 10 hane → 23-28 karakter). Tek-uc karakter sabit suffix ile tavanin altinda kaliriz.

**Neden suffix?** Idempotency disiplini ([ADR-0003](./0003-idempotent-handler-discipline.md)) cid'i natural key olarak kullanir; suffix eklemek ayni sinyali 3 mode'da tekrar oynanabilir kilarken her bir (cid, mode) tuple'inin tekillugini korur. Binance tarafinda her mode'un kendi exchange'i oldugu icin `lt` suffix'i mainnet'e gidecegi zaman fiziksel olarak imkansiz — cid collision riski sifir.

**ExchangeOrderId** hala nullable (Paper ve LiveMainnet'te hicbir zaman atanmaz). Mevcut index `IX_Orders_ExchangeOrderId` filtered `WHERE [ExchangeOrderId] IS NOT NULL` kalir, Mode boyutu bu index'e eklenmez (LiveTestnet tek dolu mode oldugundan filter yeterli).

### 8.3 Fan-Out Noktasi: StrategySignalToOrderHandler icinde Uc PlaceOrderCommand

**Karar:** Option A — `StrategySignalToOrderHandler` bir `StrategySignalEmittedEvent` icin **uc ayri `PlaceOrderCommand`** gonderir, her biri farkli `Mode` ile; `IOrderRouter` soyutlamasi **yazilmaz**.

**Reddedilen Option B (`IOrderRouter`):** tek `PlaceOrderCommand` + Infrastructure'da mode switch, ilk bakista temiz gorunse de su nedenlerle reddedildi:

- **Idempotency grani:** Her mode kendi cid'ine sahip; tek cid ile 3 mode'a fan-out yapmak Binance tarafinda ayni cid'i 3 kere kullanmak demek (sadece testnet gerceklerde), ADR-0003 ihlali.
- **Risk gate ayrimi:** §8.6'da anlatilacak, her mode ayri `RiskProfile` satirina bakar; tek komut icinde uc ayri read + uc ayri guard sonucu tek `Result<T>` donusu imkansiz — kisme-karisma semantigi bozulur.
- **Circuit breaker idempotency:** Paper'in CB'si trip olmus olabilir, LiveTestnet'inki saglam. Tek komut icinde "kismen basarili" state yaratmak mediator pipeline'ini kirer.
- **Handler per action:** CQRS + DDD dogasinda `PlaceOrderCommand` **bir** aggregate yazma aksiyonu — fan-out orchestrator olarak bulandirmak anemic CQRS'e kayar.

Handler'in yeni sekli (psuedo):

1. `StrategySignalEmittedEvent` icerigini oku.
2. Loop: `foreach (var mode in new[] { TradingMode.Paper, TradingMode.LiveTestnet, TradingMode.LiveMainnet })`.
3. Her iterasyonda:
   - cid = `sig-{StrategyId}-{BarOpenUnix}-{suffix(mode)}`
   - `PlaceOrderCommand` olustur, `Mode` alani + `DryRun` mapping:
     - Paper → `DryRun=true` + Mode=Paper (virtual fill)
     - LiveTestnet → `DryRun=false` + Mode=LiveTestnet (credentials varsa live call, yoksa reject)
     - LiveMainnet → `DryRun=false` + Mode=LiveMainnet (handler mainnet-blocked branch'a sapar, hicbir zaman Binance'e cagri gitmez)
   - `IMediator.Send(cmd, ct)` — her bir sonuc ayri `Result<T>`, `_logger.LogWarning` ile izlenir; biri diğerini iptal etmez.
4. Event handler kendi `IServiceScopeFactory.CreateAsyncScope()` icinde zaten; 3 `Send` **pipeline-temizligi** acisindan ayri scope gerektirmez (MediatR handler'i kendi `DbContext`'ini scope'tan alir); ama performans icin tek scope kullanimi yeterli.

Bu yaklasim `PlaceOrderCommand`'i tek-aggregate-yazma semantiginde tutar, idempotency cid uzerinden korunur, CB mode-scoped kalir.

### 8.4 VirtualBalance Aggregate

**Karar:** Yeni aggregate `VirtualBalance` (`Domain/VirtualBalances/VirtualBalance.cs`). 3 seed satir, `Id == (int)TradingMode` — yani `1/2/3`. `RiskProfile` gibi singleton-per-mode. `ValueGeneratedNever()`.

**Fields (domain-level):**

- `Id : int`                       — enum integer (Paper=1, LiveTestnet=2, LiveMainnet=3)
- `Mode : TradingMode`             — `Id`'den duplicated read-only, EF `HasConversion<int>()` ama persistence-level tekrar; check constraint: `Id = (int)Mode`.
- `StartingBalance : decimal`      — mode basina acilis; Paper=100 USDT; LiveTestnet=0 (Binance tarafi doldurur); LiveMainnet=0 permanent.
- `CurrentBalance : decimal`       — sadece cash (realized).
- `Equity : decimal`               — `CurrentBalance + sum(open position unrealizedPnl)` — write-time snapshot, real-time query'de yeniden hesaplanmaz (eventual consistency; §8.9 migration'da sadece kolon eklenir).
- `IterationId : Guid`             — PM loop'unun "oturum" kimligi; her reset yenilenir.
- `StartedAt : DateTimeOffset`     — iterasyon baslangic zamani.
- `LastResetAt : DateTimeOffset?`  — son reset (null = hic resetlenmedi).
- `ResetCount : int`               — toplam reset sayaci; audit amacli.
- `UpdatedAt : DateTimeOffset`     — aggregate touched.

**Behaviors (domain methods):**

- `static CreateSeed(TradingMode, decimal startingBalance, DateTimeOffset now)` — factory, migration seed tarafindan kullanilir.
- `ApplyFill(decimal realizedDelta, DateTimeOffset now)` — Paper mode'da virtual fill handler'i cagiriri; commission + slippage realized'a islenir. **Non-Paper mode'larda cagrilirsa DomainException** — "VirtualBalance.ApplyFill only valid for Paper mode".
- `RecordEquity(decimal equity, DateTimeOffset now)` — mark-to-market ucurumundan sonra cagrilir. Tum mode'larda izinli (LiveTestnet icin Binance hesap equity'sinden sync).
- `ResetForIteration(decimal startingBalance, DateTimeOffset now)` — sadece **Paper mode icin izinli**; LiveTestnet ve LiveMainnet'te cagrilirsa `DomainException("Reset not allowed for mode {Mode}")`. Yeni `IterationId` uretir, `StartedAt = now`, `LastResetAt = now`, `ResetCount++`, `CurrentBalance = StartingBalance = startingBalance`.
- `MarkDisconnected(string reason, DateTimeOffset now)` — LiveTestnet'te API key yoksa uygulanir (sadece bir "latest known" field'da tutulur — §8.8 altinda stub details).

**Domain events:**
- `VirtualBalanceResetEvent(Mode, IterationId, StartingBalance, now)`
- `VirtualBalanceEquityUpdatedEvent(Mode, Equity, now)`
- `VirtualBalanceFillAppliedEvent(Mode, RealizedDelta, CurrentBalance, now)`

**Application endpoint:**
- `POST /api/virtual-balances/paper/reset` — admin-only ([ADR-0007](./0007-admin-auth-model.md)); gelen body `{ startingBalance: 100 }` optional (default 100). Sadece Paper icin 200; LiveTestnet/Mainnet icin `405 Method Not Allowed` ya da `400 Invalid`.
- `GET /api/virtual-balances` — 3 satir donusu; read-model'de mevcut durum (PM UI dashboard rendrer eder).

### 8.5 Position.Mode Ayrimi

**Karar:** `Position` aggregate'ine `Mode : TradingMode` alani eklenir. Required (migration backfill ile).

**Karar:** `UX_Positions_Symbol_Open` filtered unique index:

```
UNIQUE(Symbol, Mode) WHERE [Status] = 1
```

Yani **ayni sembolde 3 ayri mode'un ayni anda acik pozisyonu olabilir** (gerekli — PM 3 modu paralel izleyecek). Sembol + mode coklu, sembol + mode + acik tekil.

`IX_Positions_Status_Updated` ve `IX_Positions_StrategyId` degismez. Yeni `IX_Positions_Mode_Status` eklenir (mode basina dashboard query'leri icin):

```
IX_Positions_Mode_Status  (Mode, Status, UpdatedAt)
```

Her fill olayi hangi Order'dan geldi ise o Order'in `Mode`'u Position'a propagate edilir. `Position.Open(...)` factory imzasina `TradingMode mode` eklenir (yeni parameter; mevcut cagrilar migration sonrasi guncellenir).

**Cross-mode transfer yasak:** bir fill bir mode'un pozisyonunu, baska mode'un pozisyonunu degistiremez. Application layer guard: `if (order.Mode != position.Mode) throw DomainException("Mode mismatch between order and position")`.

### 8.6 Risk Gate: Mode-Scoped RiskProfile

**Karar:** `RiskProfile` singleton pattern **mode-scoped**'a donusur. Yani **3 satir**: `Id=1 Paper`, `Id=2 LiveTestnet`, `Id=3 LiveMainnet`. `RiskProfile.SingletonId` sabiti kaldirilir; yerine `RiskProfile.IdFor(TradingMode mode) => (int)mode` static helper eklenir.

Alternatif (**reddedildi**): Tek RiskProfile + `Mode` kolonu — cunku mevcut davranislar (`TripCircuitBreaker`, `ResetCircuitBreaker`, `RecordTradeOutcome`) aggregate-icinde state tutar; tek row'da 3 mode state'i carpismaya sebep verir. `Mode` kolonu eklemek composite PK gerektirir (EF gereksiz karmasiklik).

Gerekce net: Paper'in drawdown'u LiveTestnet'i bloklamamali; LiveTestnet'in CB trip'i LiveMainnet'i (zaten blocked) etkilemez — bagimsiz kararlar, bagimsiz aggregate instance'lari.

`PlaceOrderCommand` handler'i su degisikligi yapar:

```
var riskProfile = await _db.RiskProfiles
    .AsNoTracking()
    .FirstOrDefaultAsync(r => r.Id == RiskProfile.IdFor(request.Mode), ct);
```

Seed migration 3 default row yazar (mevcut 1-row seed'in genisletilmis hali). `RiskProfileConfiguration.HasData(...)` bloku 3 object'e cikar; `UpdatedAt` ayni timestamp.

### 8.7 LiveMainnet Offline Stub

**Karar:** `Mode == LiveMainnet` gelen her `PlaceOrderCommand` su yolu izler:

1. Idempotency check (mevcut cid+mode lookup) — zaten kayitli ise ayni DTO return.
2. Enum + validator + filter validation — normal.
3. **Risk gate skip** — cunku Mode=LiveMainnet herhalukarda blocked; gereksiz read.
4. `Order.Place(...)` ile order aggregate olusturulur (Status=New).
5. Derhal `order.Reject("mainnet_blocked_by_adr_0006", _clock.UtcNow)` — status Rejected.
6. `SystemEvent.Record(eventType: "order.mainnet_blocked", severity: Warning, ...)` eklenir; payload cid + sembol + strategyId; source `PlaceOrderCommand.MainnetGuard`.
7. `await _db.SaveChangesAsync(ct)` — row yazilir (user "3 farkli islem" gormek istiyor, history korunur).
8. `Result.Success(new PlacedOrderDto(..., Status="Rejected", Mode="LiveMainnet", DryRun=false))` doner.

**Hicbir zaman** `IBinanceTrading.PlaceLiveOrderAsync` cagrilmaz. Bu branch composition'da da ikinci savunma olarak shield edilir: `BinanceMainnetTradingDecorator` (opsiyonel, §8.9 S7 scope) infra katmaninda `Mode=LiveMainnet` gelirse `throw DomainException` atar — defense in depth. MVP'de decorator yok; sadece handler-level guard yeter.

UI badge: `GET /api/virtual-balances` donusunde `LiveMainnet` satiri `{ status: "blocked", reason: "adr_0006" }` extra field; frontend kirmizi "BLOCKED" rozetini buradan cizer.

### 8.8 LiveTestnet Offline Stub (Credentials Yok)

**Karar:** `Mode == LiveTestnet` geldi + API credentials yok → Order satiri **yine de yaratilir**, derhal `Reject("no_credentials_testnet")` + SystemEvent Warning yazilir. Credentials varsa mevcut `PlaceLiveOrderAsync` akisina girer.

Credential algilama: `IBinanceCredentialsProvider` (Application abstraction, Infrastructure'da user-secrets/env tabanli impl) bir `bool HasTestnetCredentials()` metodu sunar. Handler basinda:

```
if (request.Mode == LiveTestnet && !_creds.HasTestnetCredentials())
{
    order.Reject("no_credentials_testnet", _clock.UtcNow);
    // SystemEvent.Record(...)
    // SaveChangesAsync + return Success DTO (Status=Rejected)
}
```

Credentials eklendiginde bu branch devre disi kalir; mevcut live order akisi calisir. Idempotency aynidir — cid hala UNIQUE(cid, Mode) ile korumali.

Virtual fill simulasyonu (commission, slippage, spread) **sadece Paper mode**'da calisir. LiveTestnet stub durumunda `VirtualBalance` etkilenmez; sadece Order kaydi ve SystemEvent.

### 8.9 Migration Stratejisi

**Karar:** Uc ayri migration, net isimlerle:

1. **`AddTradingModeToOrdersAndPositions`**
   - `Orders.Mode INT NULL` kolonu ekle.
   - `Positions.Mode INT NULL` kolonu ekle.
   - Backfill SQL: `UPDATE Orders SET Mode = 1; UPDATE Positions SET Mode = 1;` (mevcut tum satirlar Paper).
   - `ALTER COLUMN ... NOT NULL`.
   - `DROP INDEX UX_Orders_ClientOrderId` → `CREATE UNIQUE INDEX UX_Orders_ClientOrderId_Mode ON Orders (ClientOrderId, Mode)`.
   - `DROP INDEX UX_Positions_Symbol_Open` → `CREATE UNIQUE INDEX UX_Positions_Symbol_Mode_Open ON Positions (Symbol, Mode) WHERE [Status] = 1`.
   - `CREATE INDEX IX_Positions_Mode_Status ON Positions (Mode, Status, UpdatedAt)`.
   - Check constraint: `CK_Orders_Mode_Range CHECK (Mode IN (1,2,3))`, `CK_Positions_Mode_Range CHECK (Mode IN (1,2,3))`.

2. **`AddVirtualBalancesTable`**
   - `VirtualBalances (Id INT PK, Mode INT, StartingBalance DECIMAL(28,10), CurrentBalance DECIMAL(28,10), Equity DECIMAL(28,10), IterationId UNIQUEIDENTIFIER, StartedAt DATETIMEOFFSET, LastResetAt DATETIMEOFFSET NULL, ResetCount INT, UpdatedAt DATETIMEOFFSET)`
   - Check constraint: `CK_VirtualBalances_ModeIdParity CHECK (Id = Mode)`.
   - 3 seed row (`HasData`):
     - `Id=1 Mode=1 (Paper) StartingBalance=100 CurrentBalance=100 Equity=100`
     - `Id=2 Mode=2 (LiveTestnet) StartingBalance=0 CurrentBalance=0 Equity=0`
     - `Id=3 Mode=3 (LiveMainnet) StartingBalance=0 CurrentBalance=0 Equity=0`

3. **`ExpandRiskProfileSingletonToPerMode`**
   - Yeni 2 row seed: `Id=2 LiveTestnet` ve `Id=3 LiveMainnet` — default limit degerleri mevcut Paper ile ayni.
   - `RiskProfile.SingletonId` const silinir; `IdFor(TradingMode)` helper'i kullanilir. **Kod level** degisiklik, schema degisiklik sadece extra `HasData` eklenmesi; mevcut `Id=1` row'u olduguysa icin korunur, artik Paper'a karsilik gelir.

**Migration sirasi:** once mode-kolonu (1), sonra VirtualBalance (2), en son RiskProfile genisleme (3). Sebep: `PlaceOrderCommand` handler'i `request.Mode` + `RiskProfile.IdFor(mode)` kullanmaya basladigi anda 3 RiskProfile satiri da bulunmak zorunda; ama aggregate tarafindan 3 satir test'lerde Paper'in fallback'i olmali — bu yuzden migration sirasi schema → veri → kod birlesir sekilde bir PR'da alinmaz, **sirali 3 ayri commit**.

## Consequences

### Pozitif

- PM iterasyon loop'u 3 mode'u paralel izler, metrikleri ayni dashboard'da kiyaslar.
- [ADR-0006](./0006-testnet-first-policy.md)'nin 3-kapi testnet-first politikasi **kod seviyesinde guclenir** (LiveMainnet branch'i handler icinde 2. savunma).
- Idempotency cid+Mode uzerinden guclu; ayni sinyali yeniden oynamak deterministik.
- `VirtualBalance` aggregate'i PM loop'unun reset + metrik cekirdegi olur; her 8 saatte yeni iteration Guid'i audit trail icin benzersiz.
- Risk gate mode-scoped: Paper'in "kasten patlamasi" (stres test) live mode'lari bloklamaz.
- UI rozet (BLOCKED/CONNECTED/PAPER) tek endpoint'ten beslenir (`GET /api/virtual-balances`).

### Negatif / Tradeoff

- DB row sayisi 3 kat: Orders, Positions, SystemEvents, OrderFills — 3 mode fan-out'u audit yuku artirir. Kabul edilebilir; 30 gun testnet horizon'da order sayisi 100-1000 mertebesi (binlerce order/gun senaryosu bu proje olcegi disi).
- Idempotency key karmasikligi: cid + mode composite unique; debug/log cikti daha uzun, ama suffix `p/lt/lm` ile okunabilir.
- `StrategySignalToOrderHandler` basit bir fan-out loop'a doner; domain event'in observer'i 3 komut uretir — handler-per-action disiplinine aykiri degil ama "event → N command" pattern'i `ADR-0003 idempotent handler`a baglidir (cid suffix sagliyor).
- Migration uclu: schema + seed + kod; tersine alinmasi (rollback) ayni sirayla geri — operasyonel disiplin gerektirir.
- `RiskProfile.SingletonId` const silinmesi breaking; mevcut handler kodu + test'ler guncellenir.

### Notr

- `OrderFills` tablosu `Order.Id` FK'siyla dolayli olarak Mode scoped; ayrica kolona gerek yok. Index kompozit degisir mi? Hayir — `UX_OrderFills_Order_ExchangeTrade` aynen kalir.
- `SystemEvent` tablosuna `Mode` kolonu eklemeyelim mi? Hayir — payload JSON zaten mode'u iceriyor ve query ihtiyaci dusuk; ileride gerekirse ayri ADR.
- LiveMainnet row'u yine de 3. mode satir halinde duruyor — UI boyutu simetrik, tek branch disable etmek yerine enum butununu koruyoruz.

## Alternatifler

### Alt-1: Tek Mode (Mevcut) + Manuel Switch
Config flag ile mode degistirilir. Reddedildi — PM loop 3 mode'u **ayni anda** izlemek zorunda; sirayli switch metrik kiyaslamasini engeller.

### Alt-2: Iki Mode (Paper + Live)
LiveTestnet ve LiveMainnet'i "Live" altinda birlestir, endpoint config-driven. Reddedildi — [ADR-0006](./0006-testnet-first-policy.md) mainnet'i fiziksel olarak ayri bir kavram olarak saklamak ister; UI'da "BLOCKED" rozeti icin ayri row gereklidir; user'in "3 farkli islem" talebi ile celisir.

### Alt-3: `IOrderRouter` + Tek PlaceOrderCommand
§8.3'te detayli reddedildi. Handler-per-action + idempotency + mode-scoped CB gerekcesiyle.

### Alt-4: Mode Kolonlu Tek RiskProfile
§8.6'da reddedildi — aggregate state isolasyonu icin 3 ayri row temiz.

### Alt-5: VirtualBalance'i RiskProfile'in icine gom
State'leri ayirmak temiz kural; RiskProfile "risk limitleri + CB", VirtualBalance "nakit + equity". Tek aggregate'a gomuldukce SRP ihlal olur — reddedildi.

## Kaynak

- [ADR-0003 Idempotent Handler Discipline](./0003-idempotent-handler-discipline.md)
- [ADR-0005 Risk Limit Policy](./0005-risk-limit-policy.md)
- [ADR-0006 Testnet-First Policy](./0006-testnet-first-policy.md)
- [ADR-0007 Admin Auth Model](./0007-admin-auth-model.md)
- [docs/plan.md §14 S5/S6](../plan.md)
- [src/Domain/Orders/Order.cs](../../src/Domain/Orders/Order.cs)
- [src/Domain/Positions/Position.cs](../../src/Domain/Positions/Position.cs)
- [src/Domain/RiskProfiles/RiskProfile.cs](../../src/Domain/RiskProfiles/RiskProfile.cs)
- [src/Application/Orders/Commands/PlaceOrder/PlaceOrderCommand.cs](../../src/Application/Orders/Commands/PlaceOrder/PlaceOrderCommand.cs)
- [src/Infrastructure/Strategies/StrategySignalToOrderHandler.cs](../../src/Infrastructure/Strategies/StrategySignalToOrderHandler.cs)
- [src/Infrastructure/Persistence/Configurations/OrderConfiguration.cs](../../src/Infrastructure/Persistence/Configurations/OrderConfiguration.cs)
- [src/Infrastructure/Persistence/Configurations/PositionConfiguration.cs](../../src/Infrastructure/Persistence/Configurations/PositionConfiguration.cs)
- [src/Infrastructure/Persistence/Configurations/RiskProfileConfiguration.cs](../../src/Infrastructure/Persistence/Configurations/RiskProfileConfiguration.cs)
- User request (2026-04-17): "3-way trading mode fan-out ekleyeceğiz".

---

## Implementation Order for backend-dev

Bu ADR accept edildiginde dosya degisiklik sirasi. Her numara ayri commit (PR icinde ama mantiksal adim).

1. **`src/Domain/Common/TradingMode.cs`** — yeni enum dosyasi (`Paper=1, LiveTestnet=2, LiveMainnet=3`). Domain-level; hicbir baska degisiklik yok.

2. **`src/Domain/Orders/Order.cs`** — `Mode : TradingMode { get; private set; }` alani + `Place(...)` factory imzasina `TradingMode mode` parametresi; en sona ekle (mevcut cagirganlar kirilir ama sira onlarin guncellemesi sonraki adimda). `OrderPlacedEvent` payload'una `Mode` ekle.

3. **`src/Domain/Positions/Position.cs`** — `Mode : TradingMode { get; private set; }` + `Open(...)` factory parametresi + `PositionOpenedEvent` payload. `AddFill` icinde guard kod: Application tarafindan mode match kontrolu yapilacak (aggregate icinde degil; cross-aggregate rule Application'da).

4. **`src/Domain/VirtualBalances/VirtualBalance.cs` + `Events/`** — yeni aggregate; fields + 4 behavior method + 3 domain event. `Domain/Common/AggregateRoot<int>` miras.

5. **`src/Domain/RiskProfiles/RiskProfile.cs`** — `SingletonId` sabiti **sil**; `IdFor(TradingMode) => (int)mode` helper ekle. `CreateDefault(...)` overload alir — `TradingMode mode` parametresi (seed 3 row icin).

6. **EF Core Migrations + Configurations:**
   - `Infrastructure/Persistence/Configurations/OrderConfiguration.cs` — `Mode` kolon + `HasConversion<int>()` + `UX_Orders_ClientOrderId` DROP, `UX_Orders_ClientOrderId_Mode` (ClientOrderId, Mode) composite unique.
   - `Infrastructure/Persistence/Configurations/PositionConfiguration.cs` — `Mode` kolon + `UX_Positions_Symbol_Open` DROP, `UX_Positions_Symbol_Mode_Open` (Symbol, Mode) filtered unique WHERE Status=1; `IX_Positions_Mode_Status` ekle.
   - `Infrastructure/Persistence/Configurations/VirtualBalanceConfiguration.cs` — yeni; `HasData` 3 seed row; `CK_VirtualBalances_ModeIdParity` check constraint.
   - `Infrastructure/Persistence/Configurations/RiskProfileConfiguration.cs` — `HasData` 3 satira cik (Id=1/2/3, ayni limit degerleri).
   - `dotnet ef migrations add AddTradingModeToOrdersAndPositions --project Infrastructure --startup-project Api`
   - `dotnet ef migrations add AddVirtualBalancesTable --project Infrastructure --startup-project Api`
   - `dotnet ef migrations add ExpandRiskProfileSingletonToPerMode --project Infrastructure --startup-project Api`

7. **`src/Application/Orders/Commands/PlaceOrder/PlaceOrderCommand.cs`** — record'a `TradingMode Mode` ekle; validator mode required; handler:
   - idempotency lookup `o.ClientOrderId == cid && o.Mode == request.Mode`,
   - risk gate `RiskProfile.IdFor(request.Mode)`,
   - LiveMainnet branch (§8.7) — Reject + SystemEvent + SaveChanges erken cikis,
   - LiveTestnet credentials yoksa (§8.8) — Reject + SystemEvent,
   - Paper mode → mevcut `DryRun=true` test-order akisi + VirtualBalance.ApplyFill cagri noktasi (Paper fill simulator ayri servis — Faz-2 alt adim).

8. **`src/Application/Abstractions/IBinanceCredentialsProvider.cs`** — yeni interface (`bool HasTestnetCredentials()`); Infrastructure'da user-secrets/env tabanli impl.

9. **`src/Infrastructure/Strategies/StrategySignalToOrderHandler.cs`** — fan-out loop; 3 mode icin 3 `PlaceOrderCommand` + suffix cid pattern (§8.2).

10. **`src/Application/VirtualBalances/` + `src/Api/Endpoints/VirtualBalancesEndpoints.cs`** — `GET /api/virtual-balances` (tum mode'lar), `POST /api/virtual-balances/paper/reset` (admin-only, ADR-0007 auth). `ResetPaperIterationCommand` + `GetVirtualBalancesQuery` slice'lari.

Ek not: **Test katmani** (`tests/Domain.Tests/VirtualBalanceTests.cs`, `tests/Application.Tests/PlaceOrderCommand_ModeTests.cs`) her yeni behavior icin en az bir happy-path + bir guard-path. Reviewer "ready" verebilmek icin `tester` Playwright ile dashboard'da 3 rozet gorulmeli (PAPER connected, TESTNET disconnected, MAINNET blocked).
