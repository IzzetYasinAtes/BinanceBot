# 0010. Backfill Event Suppression

Date: 2026-04-17
Status: Accepted

> Bu ADR'in operasyonel detayli loop notu `loop_3/decision.md` icindedir. Burada normatif karar ozeti tutulur.

## Context

[ADR-0009](./0009-rest-kline-backfill-on-boot.md) ile boot'ta `KlineBackfillWorker` her sembol icin REST'ten 1000 mum cekiyor. Persistence yolu ortak `IKlinePersister`; `Kline.Ingest` / `Kline.Upsert` her bar icin `KlineIngestedEvent` ve (kapali bar icin) `KlineClosedEvent` raise ediyor (`src/Domain/MarketData/Kline.cs:62, 65, 98, 101`). `ApplicationDbContext.SaveChangesAsync` override'i (`src/Infrastructure/Persistence/ApplicationDbContext.cs:48-87`) tum event'leri `IPublisher.Publish` ile yayinliyor. `StrategyEvaluationHandler : INotificationHandler<KlineClosedEvent>` her event'te 250 bar history yukleyip aktif strateji evaluator'larini calistiriyor ve `EmitStrategySignalCommand` gonderiyor.

Sonuc: Loop 2 boot'unda `21:00:02`'de tek anda **11 retro-sinyal** emit edildi; 1000 historik bar x N strateji = sentetik sinyal patlamasi. Trade fan-out (Paper/LiveTestnet/LiveMainnet) bu sinyalleri gercek pozisyon emrine cevirmeye calisti, risk profili ve istatistikler bozuldu.

Talep: backfill **persistence**'i korunsun ama strateji evaluator'i historik bar'lar icin **tetiklenmesin**. WS path'i ayni anda gercek bir bar kapatirsa, o bar icin sinyal yayinlanmaya devam etsin (yani suppression global degil, per-call olmali).

## Decision

**`IKlinePersister.PersistAsync` overload'u `bool emitDomainEvents = true` parametresi alir.** `KlineBackfillWorker` `false` gecer; `KlinePersister` implementasyonu `false` halinde upsert/ingest sonrasi `SaveChangesAsync`'ten **once** ilgili `Kline` aggregate'inin `DomainEvents` listesini `ClearDomainEvents()` ile temizler. Domain entity ve event tanimlari degismez.

### 10.1 Suppression Granulariti

Per-call. Global flag (singleton state) yok. WS path'inde `KlineIngestionWorker` default parametreyle (`true`) cagirir; backfill `false` gecer. Bu sayede backfill devam ederken WS gercek bir 1m bar kapatirsa, o bar icin event'ler dogal sekilde yayinlanir.

### 10.2 Domain Davranisi

`Kline.Ingest` ve `Kline.Upsert` davranisi degismez — her ikisi de event raise eder. Suppression Infrastructure katmaninda yapilir; "silent" semantigi domain ubiquitous language'ina sokulmaz. DDD purity korunur, anemic model riski yok.

### 10.3 Persistence Sirasi (Kritik)

`ClearDomainEvents()` cagrisi **`SaveChangesAsync`'ten once** yapilmali. `ApplicationDbContext.SaveChangesAsync` once `ChangeTracker` uzerinden `DomainEvents` listesini topluyor sonra clear ediyor; bizim oncel clear cagrımız listenin bos toplanmasini saglar → publish dongusunde hicbir sey yayinlanmaz. Reviewer bu sirayi denetler.

### 10.4 Idempotency

[ADR-0003](./0003-idempotent-handler-discipline.md) §3.1 ve [ADR-0009](./0009-rest-kline-backfill-on-boot.md) §9.6 kontratlari korunur. Backfill bir bar'i `IsClosed=false` ile yazsa, WS sonra `IsClosed=true` ile upsert etse → WS path'i default `true` ile cagirir → o anki gercek kapanma event'i yayinlanir. Beklenen davranis.

### 10.5 Konfigurasyon

Yeni config alani **eklenmez**. Davranis cagri-yerinde explicit; toggle yok. Backfill her zaman event suppress eder; WS hicbir zaman etmez. Ekstra yuzey acmak ileride tehlikeli toggle'a donusur.

### 10.6 Test

- Unit: `KlinePersisterTests` — 4 senaryo (default emit, suppress yeni bar, suppress upsert, multi-bar emit).
- WS path icin yeni test gerekmez (default davranis degismedi).
- `StrategyEvaluationHandler` icin test degisikligi yok.

### 10.7 Reviewer Kontrol Listesi

1. Tum WS-yolu cagricilar (`KlineIngestionWorker` ve varsa diger WS aboneleri) `emitDomainEvents` parametresini ya hic gecmez (default `true`) ya da explicit `true` gecer.
2. Sadece `KlineBackfillWorker` `false` gecer.
3. `ClearDomainEvents` cagrisi `SaveChangesAsync`'ten once, ingest/upsert sonrasi.
4. `IKlinePersister` Application.Abstractions altinda; degisiklik dependency rule'u (Domain ← Application ← Infrastructure) bozmaz.

## Consequences

### Pozitif

- Retro-sinyal patlamasi sifirlanir.
- WS path'i sifir regresyon yuzeyi (default parametre).
- DDD purity korunur — domain event tanimlari ve aggregate metodlari degismez.
- WS overlap senaryosu guvende; backfill suresi boyunca gerçek WS bar'lari icin sinyaller normal calisir.

### Negatif / Tradeoff

- `IKlinePersister` arayuzu Infrastructure-spesifik bir flag tasiyor (`emitDomainEvents`). Application-Abstractions katmaninda goze batar; ama Application zaten IPublisher / IDomainEvent kavramini biliyor, semantik kabul edilebilir.
- Clear etmek **butun** event'leri (Ingested + Closed) susturuyor. Su an kimse `KlineIngestedEvent`'e abone degil; ileride yeni handler eklenirse o da backfill icin tetiklenmeyecek. Bu **istenen** davranis (backfill = historik veri, hicbir reaktif handler calismamali); ama future contributor'lar farkina varmali.

### Notr

- Performans negligible (`ClearDomainEvents` O(1)).
- Trading mode'dan ([ADR-0006](./0006-testnet-first-policy.md)) bagimsiz; hem testnet hem mainnet'te ayni davranir.
- Migration / DB schema degisikligi yok.

## Alternatifler

1. `Kline.IngestSilent(...)` factory — domain dilini "silent" gibi teknik kavramla kirletir, anemic-model'in tersine bloated-model uretir. Reddedildi.
2. `IBackfillState.IsBackfillInProgress` global singleton — backfill anindaki gercek WS bar'i da skip eder; race condition. Reddedildi.
3. `BackfillCompletedEvent` + kalici `IsSeeded` flag — global flag riskleri + handler'a cross-cutting state bagimliligi. Reddedildi.
4. Notification handler'a `barOpenTime > UtcNow - 2dk` zaman filtresi — magic number, interval'a duyarsiz, fragile. Reddedildi.
5. Backfill icinde `SaveChangesAsync` cagrisini bypass edip raw SQL bulk insert — entity invariant kontrollerini atlar (Validate); EF Core idiomatik degil. Reddedildi.

## Kaynak

- [0003-idempotent-handler-discipline.md](./0003-idempotent-handler-discipline.md)
- [0009-rest-kline-backfill-on-boot.md](./0009-rest-kline-backfill-on-boot.md)
- [loop_3/decision.md](../../loop_3/decision.md) — operasyonel detay + backend-dev sablonu
- [Microsoft Learn — Domain events: design and implementation](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/domain-events-design-implementation)
- [jasontaylordev/CleanArchitecture — DispatchDomainEventsInterceptor pattern](https://github.com/jasontaylordev/CleanArchitecture)
