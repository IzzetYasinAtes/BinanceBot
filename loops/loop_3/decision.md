# Loop 3 — Backlog #10 Architecture Decision

**Date:** 2026-04-17
**Status:** Accepted
**Topic:** Backfill sirasinda strateji evaluator tetiklenmemeli (retro-sinyal patlamasi)
**Related ADR:** [docs/adr/0010-backfill-event-suppression.md](../docs/adr/0010-backfill-event-suppression.md)
**Author:** architect
**Implementation owner:** backend-dev

---

## Context

Loop 2 boot'unda `21:00:02` saniyesinde tek anda **11 strateji sinyali** emit edildi; hepsinin `barOpenTime`'i geriye yayilmis (gecmis bar'lar) ama `emittedAt`'i ayni — gecmis veriden sentetik olarak uretilen retro-sinyaller. Kok sebep:

1. `KlineBackfillWorker` boot'ta her sembol icin REST'ten 1000 mum cekiyor ([ADR-0009](../docs/adr/0009-rest-kline-backfill-on-boot.md)).
2. Her bar `IKlinePersister.PersistAsync` icine giriyor, `Kline.Ingest` veya `Kline.Upsert` cagriliyor (`src/Domain/MarketData/Kline.cs:62, 65, 98, 101`).
3. Bu metodlar `KlineIngestedEvent` + (kapali bar icin) `KlineClosedEvent` raise ediyor.
4. `ApplicationDbContext.SaveChangesAsync` (`src/Infrastructure/Persistence/ApplicationDbContext.cs:48-87`) override'i tum domain event'leri `IPublisher.Publish` ile yayinliyor.
5. `StrategyEvaluationHandler : INotificationHandler<KlineClosedEvent>` (`src/Infrastructure/Strategies/StrategyEvaluationHandler.cs`) her event'te 250 bar history yukluyor + her aktif strateji icin evaluator calistiriyor + `EmitStrategySignalCommand` gonderiyor.

**Net etki:** 1000 bar x 3 sembol = 3000 historik insert -> kabaca 3000 evaluator turn -> "gecmise donuk sinyal" enflasyonu. Trade fan-out (Paper / LiveTestnet / LiveMainnet) bu sinyalleri gercek pozisyon gibi acmaya calisiyor; risk profili ve istatistik bozuluyor.

WS path'i ile backfill path'inin **idempotent ortak persistence** kullanmasi ([ADR-0009 §9.6](../docs/adr/0009-rest-kline-backfill-on-boot.md)) DRY acisindan korunmali; cozum bu kontrati bozmayacak.

## Decision

**Secenek (A) — `IKlinePersister.PersistAsync` overload'una `bool emitDomainEvents = true` parametresi eklenir; `KlineBackfillWorker` `false` gecer; persister, `false` halinde upsert/ingest sonrasi `SaveChangesAsync`'ten **once** ilgili `Kline` aggregate'inin `DomainEvents` listesini `ClearDomainEvents()` cagrisiyla temizler.**

Domain entity'nin event raise davranisi degismez (anemic-model riski yok). Suppression **per-call** granularity'de Infrastructure katmaninda yapilir — global flag (C/D) yok, gercek WS bar backfill ile concurrent gelse bile olagan path'ten gecer ve event yayinlanir.

### Niye Secenek (A) — Diger Alternatifler Reddedildi

| Aday | Reddetme Gerekcesi |
|---|---|
| (B) `Kline.IngestSilent(...)` factory | Domain ubiquitous language'i kirletir; "silent" bir is anlami degil teknik kacis. Domain "I was created" diyor; kim dinleyecek karari sunucu sorumlulugu. Ayrica Upsert icin de paralel `UpsertSilent` gerekir → API yuzeyi sislir. |
| (C) `IBackfillState.IsBackfillInProgress` global flag | Race condition: backfill devam ederken WS gercek bir 1m bar kapanirsa, handler global flag yuzunden bu **gercek** bar'i da skip eder → trading yanlisligi. Boot suresine yayilan tehlike penceresi. |
| (D) `BackfillCompletedEvent` + `IsSeeded` flag | Ayni race + ek karmasiklik (yeni event tipi, MediatR notification, kalici flag servisi). Ayrica DB drop sonrasi yeniden seed davranisi belirsiz. |

Secenek (A) lokal, granular, test edilebilir; ozellikle backfill ile WS overlap'i sirasinda WS path'i dogal `emitDomainEvents = true` ile devam ederken backfill path'i ayri sinyal cretmiyor.

### Implementation Spec (backend-dev icin hazir sablon)

#### 1. `IKlinePersister` arayuzu (Application/Abstractions/Binance/IKlinePersister.cs)

```csharp
public interface IKlinePersister
{
    /// <summary>
    /// Persists a single kline payload (originating from either the WS push stream
    /// or the REST backfill snapshot) into the database using the (Symbol, Interval,
    /// OpenTime) idempotent upsert contract (ADR-0003 section 3.1).
    /// </summary>
    /// <param name="payload">Normalised kline payload (WS or REST).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="emitDomainEvents">
    /// When <c>false</c> the persister clears any domain events raised by this
    /// kline aggregate before <see cref="DbContext.SaveChangesAsync"/>, so that
    /// notification handlers (e.g. <c>StrategyEvaluationHandler</c>) do not fire
    /// for historical bars. Defaults to <c>true</c> so the WS path is unaffected.
    /// (ADR-0010 — backfill event suppression.)
    /// </param>
    Task PersistAsync(WsKlinePayload payload, CancellationToken ct, bool emitDomainEvents = true);
}
```

Default parametre `true` — mevcut WS cagriciler (KlineIngestionWorker vb.) hicbir degisiklik yapmadan eski davranisla calisir. Sadece backfill explicit `false` gecer.

#### 2. `KlinePersister` implementasyonu (Infrastructure/Binance/Workers/KlinePersister.cs)

```csharp
public async Task PersistAsync(WsKlinePayload p, CancellationToken ct, bool emitDomainEvents = true)
{
    var symbolVo = Symbol.From(p.Symbol);
    var existing = await ((DbSet<Kline>)_db.Klines)
        .FirstOrDefaultAsync(
            k => k.Symbol == symbolVo && k.Interval == p.Interval && k.OpenTime == p.OpenTime,
            ct);

    Kline aggregate;
    if (existing is null)
    {
        aggregate = Kline.Ingest(
            symbolVo, p.Interval, p.OpenTime, p.CloseTime,
            p.Open, p.High, p.Low, p.Close,
            p.Volume, p.QuoteVolume, p.TradeCount,
            p.TakerBuyBase, p.TakerBuyQuote, p.IsClosed);
        _db.Klines.Add(aggregate);
    }
    else
    {
        existing.Upsert(
            p.Open, p.High, p.Low, p.Close,
            p.Volume, p.QuoteVolume, p.TradeCount,
            p.TakerBuyBase, p.TakerBuyQuote, p.IsClosed);
        aggregate = existing;
    }

    if (!emitDomainEvents)
    {
        // Backfill-mode: drop the events the aggregate just raised so the
        // ApplicationDbContext.SaveChangesAsync override does not publish them.
        // Persistence still happens; only the notification fan-out is suppressed.
        aggregate.ClearDomainEvents();
    }

    await _db.SaveChangesAsync(ct);
}
```

Onemli detay: `ClearDomainEvents()` cagrisi `SaveChangesAsync`'ten **once** olmak zorunda. `ApplicationDbContext.SaveChangesAsync` override'i 50-77 satirlarinda once `DomainEvents`'i topluyor sonra clear ediyor; ama bizim cagrimiz **toplama oncesi** clear yapip listeyi bos birakiyor → `events.AddRange(k.DomainEvents)` bos sequence ekliyor → publish dongusunde hicbir sey yayinlanmiyor.

#### 3. `KlineBackfillWorker` cagrisi (Infrastructure/Binance/Workers/KlineBackfillWorker.cs:238)

Tek satir degisikligi:

```csharp
// Eskisi:
// await persister.PersistAsync(payload, ct);

// Yenisi:
await persister.PersistAsync(payload, ct, emitDomainEvents: false);
```

Inline yorum: `// ADR-0010: backfill bars must not trigger StrategyEvaluationHandler.`

#### 4. WS path (KlineIngestionWorker — degismez)

WS akisindaki cagri default `true` ile gider; manuel override gerekmez. Reviewer kontrol etsin: WS persistence cagrilarinin **hicbiri** `emitDomainEvents: false` ile cagrilmamali.

#### 5. Test (Tests/Application.Tests veya Infrastructure.Tests)

Yeni `KlinePersisterTests` (en az 4 senaryo):

| Senaryo | Beklenti |
|---|---|
| Default `emitDomainEvents = true`, yeni bar (`Ingest`) | `KlineIngestedEvent` + (closed ise) `KlineClosedEvent` `IPublisher.Publish` mock'una geliyor. |
| `emitDomainEvents = false`, yeni bar | `IPublisher.Publish` **hic** cagrilmiyor; ama `Klines.Count` 1 artmis (persistence calisiyor). |
| `emitDomainEvents = false`, var olan bar (`Upsert`) | Yine publish yok; OpenPrice/ClosePrice DB'de guncellenmis. |
| `emitDomainEvents = true`, ayni transaction'da iki bar | Iki bar icin de event publish ediliyor. |

`StrategyEvaluationHandler` icin yeni test gerekmez — bu degisiklik onun davranisina dokunmaz.

#### 6. Ek (opsiyonel ama tavsiye) — `KlineBackfillWorker.BackfillOneAsync` log

Persist sayisini logladiktan sonra ek satir:

```csharp
_logger.LogInformation(
    "Kline backfill {Symbol} {Interval}: persisted {Count} bar(s) (events suppressed)",
    symbol, intervalCode, persisted);
```

"events suppressed" string'i operator'un live trace'ten retro-sinyal patlamasinin tekrarlanmadigini dogrulamasini kolaylastirir.

## Consequences

### Pozitif

- Retro-sinyal patlamasi tamamen durur — backfill 3000 bar yazsa bile evaluator hicbiri icin tetiklenmez.
- WS path'i tamamen el surulmemis kalir (default parametre `true`); regresyon yuzeyi minimal.
- WS overlap senaryosu guvende: backfill devam ederken gercek WS bar kapanirsa, KlineIngestionWorker default `true` ile cagirir → sinyal yayinlanir → handler dogal calisir.
- Idempotent upsert kontrati ([ADR-0003](../docs/adr/0003-idempotent-handler-discipline.md), [ADR-0009 §9.6](../docs/adr/0009-rest-kline-backfill-on-boot.md)) korunur. Backfill bir bar yazdiysa ve WS sonradan ayni bar'i upsert ederse, **WS upsert'i normal sekilde event yayinlar** — ki bu beklenen davranis: gercek "kapanma" anini WS bildiriyor.
- Domain entity (`Kline`) ve domain event tanimlari degismez. DDD purity korunur; "Silent" gibi yari-domain semantik eklenmez.

### Negatif / Tradeoff

- `IKlinePersister` arayuzu kucuk de olsa Infrastructure-spesifik bir flag (`emitDomainEvents`) ile genisletildi. Saf okumada Application-Abstractions katmanina "publish" detayinin sizdigi gorulur. Kabul: parametre adi semantik (publish degil "emit domain events"); Application zaten IDomainEvent kavramini biliyor (`Application.Abstractions` icinde IPublisher kullaniliyor).
- Clear etmek = **butun** event'leri silmek. Sadece `KlineClosedEvent`'i degil `KlineIngestedEvent`'i de susturuyor. Su an kimse `KlineIngestedEvent`'e abone degil; ileride bir handler eklenirse backfill o handler'i da aktive etmez. Bu **gercekte istenen davranistir** — backfill historik veridir, hicbir reaktif handler tetiklenmemeli.
- Test surface kucuk artar (yeni KlinePersisterTests).

### Notr

- Performans: `ClearDomainEvents` O(1)-ish list clear; olcum altina dusen overhead.
- Mainnet uyumu (ADR-0006): degisiklik trading-mode'dan tamamen bagimsiz; hem testnet hem mainnet'te ayni sekilde calisir.

## Alternatifler — Detayli Reddetme

### (B) `Kline.IngestSilent(...)` factory

Domain'de `IngestSilent` metodu tanitmak demek ubiquitous language'a "silent" diye bir kavram sokmak demek. Domain dilinde "silent ingest" diye bir is konsepti yok; bu pure infrastructure concern (notification fan-out) ve **bunu aggregate'e tasimak Anemic Model yasagi'nin tersi yonunde Bloated Model uretir** — domain teknik calisma moduna gore karar veriyor. Reddedildi.

### (C) `IBackfillState.IsBackfillInProgress` global singleton flag

Ana risk: backfill devam ederken WS gercek bir 1m bar kapanirsa (boot anindan sonra ilk dakika icinde tipik), handler flag'i `true` gorur ve **gercek** bar icin de evaluator skip eder. Bu trading kararlarinin kayba ugramasi demek; backfill amaclanmamis bir trading-blocker olur. Pencere kuçük ama varolan bir risk; reddedildi.

### (D) `BackfillCompletedEvent` + `IsSeeded` kalici flag servisi

(C)'nin tum risklerine ek olarak: kalici flag DB'ye yazilirsa migration ihtiyaci dogar; in-memory ise restart sonrasi yeniden seed davranisi tutarsiz olur. Ayrica handler'in karar mantigi yeni bir cross-cutting state'e bagli olur — DDD acisindan handler'in bilmesi gereken tek sey "su event geldi, su strateji aktif"tir; "sistem seed mi" sorusunu sormak handler sorumlulugunu asar. Reddedildi.

### (E) Notification handler tarafinda zaman filtresi (`barOpenTime > UtcNow - 2dk`)

Plan'da onerilmisti ama:
- Magic number (2dk) — degerin ne olacagi neye gore? 1m, 5m, 1h, 4h interval'larin hepsi icin tek esik yanlis.
- Backfill 1000 mum cekerken sadece son 2dk dis kalir; geri kalan 998 mum yine tetiklenmez ama mantik **handler tarafinda yasiyor** — yanlis sebepten dogru sonuc, fragile.
- Ileride farkli frekanslarda strateji eklenirse esik yeniden ayarlanmali. Reddedildi.

## Migration / Rollout

1. Yeni branch (PM kararı): `fix/backfill-event-suppression`.
2. backend-dev:
   - `IKlinePersister.PersistAsync` overload (default `true`).
   - `KlinePersister` implementasyon — `ClearDomainEvents` ekle.
   - `KlineBackfillWorker.BackfillOneAsync` — `emitDomainEvents: false` gec.
   - `KlinePersisterTests` — 4 senaryo.
3. reviewer:
   - WS yolundaki tum cagricilarda parametre `true` veya default oldugunu dogrula.
   - Hicbir Application/Domain referansinda `KlinePersister` cagrisi olmadigini dogrula (sadece Infrastructure worker'lar).
   - `ClearDomainEvents` cagrisinin **`SaveChangesAsync` oncesinde** oldugunu dogrula.
4. tester:
   - Manuel: DB drop + boot. Loop'un ilk dakikasinda **0 sinyal** beklenmeli; ilk gercek WS bar kapanis sonrasi (1m sonra) ilk sinyal cikmaisi.
   - Playwright: `/strategies/signals/latest` ilk 30sn 0 dondurmeli, sonra dolmali.
5. Loop 3 normal cycle baslamadan once **DB drop** + 4h gozlem.

## Cakisma Kontrolu

| ADR | Cakisma | Sonuc |
|---|---|---|
| [ADR-0003](../docs/adr/0003-idempotent-handler-discipline.md) idempotent upsert | Persistence yolu degismedi, sadece event publish suppression eklendi. | ✅ Uyumlu. |
| [ADR-0006](../docs/adr/0006-testnet-first-policy.md) testnet-first | Trading mode / endpoint ayriminda hic rol oynamaz. | ✅ Bagimsiz. |
| [ADR-0009](../docs/adr/0009-rest-kline-backfill-on-boot.md) backfill on boot | §9.6 idempotency kontrati korundu; `KlinePersister` ortak path olarak yasamaya devam ediyor. ADR-0010 bu ADR'in event-fan-out boslugunu kapatir. | ✅ Genisletici (komplementer). |

## Kaynak

- [docs/adr/0003-idempotent-handler-discipline.md](../docs/adr/0003-idempotent-handler-discipline.md)
- [docs/adr/0009-rest-kline-backfill-on-boot.md](../docs/adr/0009-rest-kline-backfill-on-boot.md)
- [src/Domain/MarketData/Kline.cs](../src/Domain/MarketData/Kline.cs) — event raise noktalari
- [src/Infrastructure/Persistence/ApplicationDbContext.cs](../src/Infrastructure/Persistence/ApplicationDbContext.cs) — SaveChangesAsync override
- [src/Infrastructure/Binance/Workers/KlinePersister.cs](../src/Infrastructure/Binance/Workers/KlinePersister.cs)
- [src/Infrastructure/Binance/Workers/KlineBackfillWorker.cs](../src/Infrastructure/Binance/Workers/KlineBackfillWorker.cs)
- [src/Infrastructure/Strategies/StrategyEvaluationHandler.cs](../src/Infrastructure/Strategies/StrategyEvaluationHandler.cs)
- [loop_3/plan.md](./plan.md) §P0 #10
