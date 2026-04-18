# Loop 1 — Decision: REST Kline Backfill Tasarimi

Date: 2026-04-17
Status: Accepted
Agent: architect
Sequel-of: docs/adr/0002-binance-ws-supervisor-pattern.md, docs/adr/0003-idempotent-handler-discipline.md
Related-to: docs/adr/0006-testnet-first-policy.md

> Bu dosya `loop_1/` calismasinin karar tutanagidir. Resmi ADR olarak `docs/adr/0009-rest-kline-backfill-on-boot.md` adi altinda mirror edilecektir (ayni icerik, ayni numara).

## Context

`KlineIngestionWorker` su an yalnizca WS akisindan beslenir. Her DB drop / fresh boot sonrasi evaluator'lar (TrendFollowing SlowEma=20, ATR=14; MeanReversion BB=20, RSI=14) ~20-30 dk warmup yasiyor; 4h test loop'larinda olculen "calisan strateji suresi" bozuluyor.

binance-expert arastirma ozeti (loop_1/research.md) sunlari netlestirdi:
- `GET /api/v3/klines` testnet'te calisir, weight=2/istek, 1m x 1000 bar tek istekte gelir (~16h 40dk derinlik).
- 3 sembol = 6 weight; mainnet 1200/dk limitinin %0.5'i.
- `BinanceMarketDataClient.GetKlinesAsync` hazir.
- `KlineIngestionWorker.PersistAsync` zaten `(Symbol, Interval, OpenTime)` PK uzerinden upsert (ADR-0003 §3.1).
- Sira **WS subscribe ONCE -> REST backfill SONRA**; aksi halde backfill penceresinde acilan barlar gap olusturabilir.

Ayrica **ADR-0006 mainnet kalici bloklu**: backfill mekanizmasi `BinanceOptions.RestBaseUrl`'e dokunmamali, `IBinanceMarketData` mevcut named client (`binance-rest`) uzerinden gitmeli; hicbir yerde mainnet endpoint hardcode edilmemeli.

## Decision

7 alt karar, sirayla:

### D1 — Konum: Ayri `KlineBackfillWorker : IHostedService`

Yeni bir `BackgroundService` **degil**, `IHostedService` impl edilir; ayri dosya: `src/Infrastructure/Binance/Workers/KlineBackfillWorker.cs`.

**Gerekce (SRP + KISS):**
- `KlineIngestionWorker.ExecuteAsync` su an saf bir consumer loop'u (`await foreach` Channel reader). Backfill onun icine gomulurse hem responsibility kirlenir (push-stream consumer + one-shot pull) hem de testte iki davranisi ayirmak zorlasir.
- `BackgroundService` yerine direkt `IHostedService` secilmesinin sebebi: backfill **once-and-done**; `ExecuteAsync` semantigi (uzun yasayan kuyruk dongusu) bu davranisla yanlis sinyal verir. `StrategySeeder` da ayni desene sahip (`IHostedService.StartAsync` -> bir kez yap -> bitir) — onu pattern olarak kopyalanacak.

**Reddedilen alternatif:** "KlineIngestionWorker icine `if (!_backfilled) { ... }` blogu" — anemic mixin, SRP ihlali, test zorlugu.

### D2 — Tetikleme: `IHostedService.StartAsync` + `Task.Run` (fire-and-forget background)

`StartAsync` cagrildiginda backfill **hemen baslar ama Task await edilmez**; ic gorev `Task.Run`'a dusurulup kontrol host'a iade edilir. `StartAsync` icinde `Task.CompletedTask` doner.

**Neden:**
- ASP.NET Core, `IHostedService.StartAsync` cagrilarini **sirayla** yapar; eger backfill orada `await` edilirse host startup ~700ms-2s blocklanir. Bu sure boyunca WS supervisor da `StartAsync`'ini calistirmayi bekler — yani **WS subscribe gecikir, sira tersine doner**.
- `IHostApplicationLifetime.ApplicationStarted` daha temiz gibi gorunur ama: callback `Task` donmez, async arkadan kovalanir, lifecycle sinyali test'te zor mock edilir, `IHostedService.StopAsync` cagrildiginda hala running olabilir (CT akisi belirsiz).
- `Task.Run` icinde alinan calisma `IHostApplicationLifetime.ApplicationStopping` token'i ile baglanir; shutdown'da temiz iptal.

**DI registration sirasi:** `services.AddHostedService<BinanceWsSupervisor>()` **once**, ardindan `services.AddHostedService<KlineBackfillWorker>()`. ASP.NET Core hosted service'leri kayit sirasiyla `StartAsync` eder; bu sira garantili.

### D3 — Sira garantisi: Lifecycle sirasi + WS-ready signal

Iki katman:

**Katman 1 (zorunlu):** DI registration sirasi — `BinanceWsSupervisor` once, `KlineBackfillWorker` sonra. ASP.NET Core spec garantisi.

**Katman 2 (defensive):** `BinanceWsSupervisor` icinde mevcut `WsSupervisorState` enum'u var (`Connecting/Connected/Subscribing/Streaming`). `KlineBackfillWorker` ic `Task.Run` blogunun basinda **state poll edilir**: `Streaming` veya `Subscribing` olana kadar 200ms aralikla bekler, max 10s timeout. Timeout asilirsa "WS not ready, proceeding anyway" WARN log + backfill yine de yapilir (gap potansiyeli loglanir).

**`Task.Delay(2_000)` magic-number reddedildi** — flaky, env-bagimli, KISS gibi gorunup gercekte teknik borc.

Implementasyon notu: `BinanceWsSupervisor.State` zaten `public` property; `KlineBackfillWorker` constructor'da supervisor'i singleton DI ile alir (`services.AddSingleton<BinanceWsSupervisor>()` zaten kayitli, satir 124).

### D4 — Hata davranisi: Retry-then-skip per symbol, **fail-fast yok**

Her sembol icin:
- HTTP resilience handler (Polly v8) **zaten** `binance-rest-resilience` pipeline'inda 3 deneme + exponential backoff. `KlineBackfillWorker` ekstra retry **eklemez**.
- Pipeline tukenirse (3 deneme sonra hata): WARN log + diger sembollere devam.
- Tum semboller fail ederse: ERROR log + boot devam eder, host crash etmez.

**Gerekce:**
- Backfill **performans optimizasyonu**, kritik path degil. Boot crash ettirmek WS akisini da olduren overshoot.
- Evaluator'lar zaten WS-only mod ile `[gap] -> warmup` davranisina sahip; degradation graceful.
- Fail-fast secilirse ADR-0006 mainnet bloku ile birlesince istenmeyen "boot reddi" yuzeyi genisler.

**Reddedilen alternatif:** "Tek sembol fail = host crash" — fragility artar, deger dusuktur.

### D5 — Konfigurasyon: `BinanceOptions` icine 3 yeni alan

`BinanceOptions.cs`'e eklenecek (PM onerisi onaylandi):

```csharp
public bool BackfillEnabled { get; init; } = true;

[Range(1, 1000)]
public int BackfillLimit { get; init; } = 1000;

[Required]
[MinLength(1)]
public string[] BackfillIntervals { get; init; } = ["1m"];
```

**Kararlar:**
- Default `Enabled=true`: degradation default'u (warmup) kotu UX, opt-out daha mantikli.
- `Limit` 1-1000 range; Binance API limit'i. Default 1000 (max derinlik).
- `BackfillIntervals` ayri bir array (mevcut `KlineIntervals` ile karistirilmadi). Sebep: WS abonelik intervals'i ile backfill intervals'i farkli politika icerir; ileride WS sadece 1m, backfill 1m+5m+15m gibi senaryolar mumkun. Default `["1m"]` — research bulgusuyla uyumlu.

**Reddedilen:** "Mevcut `KlineIntervals`'i yeniden kullan" — bagimlilik baglar, ileride bagimsiz tuning'i bozar.

### D6 — Hangi semboller: `BinanceOptions.Symbols` (config), StrategySeeder degil

Backfill, `BinanceOptions.Symbols` listesini kullanir.

**Gerekce:**
- WS supervisor da ayni listeyi kullaniyor (`BinanceWsSupervisor.BuildStreamUrl`, satir 122-132). **WS ne abone oluyorsa REST de ona warmup yapsin** — gap mantigi simetrik.
- StrategySeeder'dan symbol cekmek catlak yaratir: bir strateji aktif degilse veya farkli sembollerde calisiyorsa backfill ile WS ortusmez. SRP ihlali.
- Ileride strateji-spesifik backfill gerekirse ayri bir `StrategyBackfillCommand` ile MediatR uzerinden cozulur, boot worker'ina karistirilmaz.

### D7 — Test: 1 unit + 1 integration (smoke)

**Unit test (`Tests/Infrastructure.Tests/Binance/Workers/KlineBackfillWorkerTests.cs`):**
- `IBinanceMarketData` mock (Moq/FakeItEasy).
- `IServiceScopeFactory` ile `IApplicationDbContext` (InMemory provider).
- `BinanceWsSupervisor` icin `State` property'sine erisim icin **direkt instance** veya kucuk bir `IWsReadinessProbe` interface'i extract edilir (D3 katman 2 testi icin). Architect onerisi: kucuk interface extract — `IWsReadinessProbe.IsReady` — supervisor implement eder, worker interface uzerinden okur. Test mock kolaylasir, dependency rule korunur.
- Senaryolar: (a) `Enabled=false` -> hicbir REST cagrisi yapilmaz; (b) 3 sembol, hepsi success -> 3000 row upsert; (c) 1 sembol exception -> diger 2'si yine isler, log WARN; (d) WS-ready timeout -> WARN + yine de calisir.

**Integration smoke (`Tests/Api.IntegrationTests/Backfill/BackfillSmokeTests.cs`):**
- `WebApplicationFactory<Program>` + InMemory DB.
- `IBinanceMarketData` test double ile replace; 3 sembol icin sahte 1000 bar doner.
- Boot sonrasi `Klines` tablosunda her sembol icin >= 1000 row beklentisi.
- WS supervisor mock (Channel'a hicbir sey yazmaz) — sadece backfill path'i test edilir.

**Reddedilen:** "Canli testnet'e gercek HTTP" — flaky, network bagimli, CI'da yasak. Smoke icin double yeterli; gercek HTTP `binance-expert`'in arastirma loop'unda dogrulandi.

## Consequences

### Pozitif

- Boot sonrasi ilk kline upsert ~700ms-2s icinde tamamlanir; evaluator warmup penceresi 20-30 dk -> ~0 dk.
- 4h test loop'larinda "calisan strateji suresi" olcumu gerceklestirilir hale gelir.
- WS-only failover yolu acik kalir (D4); backfill bozulursa system degil sadece warmup degrade eder.
- ADR-0002 (WS supervisor), ADR-0003 (idempotent upsert) ve ADR-0006 (testnet-first) ile sifir catismayla kompoze.

### Negatif / Tradeoff

- Yeni bir hosted service + 3 yeni config alani: yuzey artar, dokuman + test yuku. Kabul: kazanc (warmup eliminasyon) maliyeti asar.
- `BinanceWsSupervisor.State`'i public okumak icra (D3 katman 2) supervisor'i `IWsReadinessProbe` interface'iyle yeniden gosterir; kucuk bir indirection. Kabul: testabilite kazanci > maliyet.
- Backfill 1m-only basliyor; ileride 5m/15m strateji eklenirse `BackfillIntervals` config'ine bar eklemek + daha derin tarihsel pencere kararliligi yeniden degerlendirilmeli.

### Notr

- HTTP weight maliyeti negligible (6/1200 mainnet, 6/6000 testnet).
- Backfill sirasinda WS'den ayni bar paralel gelirse: ADR-0003 §3.1 upsert garanti — duplicate yok, race-condition zararsiz.

## Alternatifler

1. **`KlineIngestionWorker.ExecuteAsync` icine gomme** — SRP ihlali, test zorlugu. Reddedildi (D1).
2. **`BackgroundService.ExecuteAsync` icinde `if (!_backfilled)`** — once-and-done semantigi `BackgroundService` ile yanlis sinyal. `IHostedService` daha dogru kontrat. Reddedildi (D1).
3. **`IHostApplicationLifetime.ApplicationStarted` callback** — async kontrolsuz, shutdown CT belirsiz, test mock zor. Reddedildi (D2).
4. **`Task.Delay(2000)` ile sira garantisi** — magic number, env-bagimli, flaky. Reddedildi (D3).
5. **Fail-fast (boot crash)** — degradation > crash; warmup performans kararidir, kritik path degil. Reddedildi (D4).
6. **`KlineIntervals` listesini reuse** — bagimlilik baglar; bagimsiz tuning kapanir. Reddedildi (D5).
7. **StrategySeeder'dan sembol cek** — WS-backfill simetrisi bozulur; strateji degisirse gap olusur. Reddedildi (D6).
8. **Canli testnet smoke** — CI flake, network. Doubles yeterli. Reddedildi (D7).

## Implementasyon Notlari (backend-dev icin)

1. **Yeni dosyalar:**
   - `src/Infrastructure/Binance/Workers/KlineBackfillWorker.cs` (`IHostedService`).
   - `src/Application/Abstractions/Binance/IWsReadinessProbe.cs` (yeni interface, single bool).
   - Test: `tests/Infrastructure.Tests/Binance/Workers/KlineBackfillWorkerTests.cs`.

2. **Degisecek dosyalar:**
   - `src/Infrastructure/Binance/BinanceOptions.cs` — 3 yeni property (D5).
   - `src/Infrastructure/Binance/Streams/BinanceWsSupervisor.cs` — `IWsReadinessProbe` implement et; `IsReady => _state is WsSupervisorState.Subscribing or WsSupervisorState.Streaming`.
   - `src/Infrastructure/DependencyInjection.cs` — DI siralamasi:
     - Mevcut `services.AddHostedService(sp => sp.GetRequiredService<BinanceWsSupervisor>())` (satir 125) AYNEN BIRAKILIR.
     - Hemen ardindan `services.AddSingleton<IWsReadinessProbe>(sp => sp.GetRequiredService<BinanceWsSupervisor>())` eklenir.
     - `services.AddHostedService<KlineBackfillWorker>()` `services.AddHostedService<KlineIngestionWorker>()` (satir 129) cagrisindan **once** koy ki StartAsync sirasi: WsSupervisor -> KlineBackfill -> KlineIngestion.
   - `src/Api/appsettings.json` — `Binance` section'a `BackfillEnabled/BackfillLimit/BackfillIntervals` defaultlari.

3. **PersistAsync re-use:** `KlineIngestionWorker.PersistAsync` private; backfill icin **kopya** uretmek yerine, ortak bir `KlinePersister` (Infrastructure scoped service) extract edilir. Imza: `Task UpsertAsync(WsKlinePayload payload, CancellationToken ct)`. WS path'i `WsKlinePayload` olusturup cagirir; backfill path'i `RestKlineDto` -> `WsKlinePayload` adapter ile ayni metodu cagirir. DRY garantisi + ADR-0003 §3.1 upsert kontrati tek yerden surdurulur.

4. **`IsClosed` semantigi:** REST'ten gelen son bar (open candle) `IsClosed=false` ile yazilmali; WS sonradan kapanis ile guncelleyecek. Adapter map'inde:
   - `RestKlineDto.CloseTime > DateTimeOffset.UtcNow` -> `IsClosed=false`.
   - aksi -> `IsClosed=true`.

5. **ADR-0006 uyum:** Yeni kod `BinanceOptions.RestBaseUrl`'i okumaz, hardcoded URL yazmaz. `IBinanceMarketData` zaten `binance-rest` named client uzerinden gider; `RestBaseUrl` boot guard'i tek otorite.

## Kaynak

- [docs/adr/0002-binance-ws-supervisor-pattern.md](../docs/adr/0002-binance-ws-supervisor-pattern.md) — WS supervisor disiplini
- [docs/adr/0003-idempotent-handler-discipline.md](../docs/adr/0003-idempotent-handler-discipline.md) §3.1 — Kline upsert kontrati
- [docs/adr/0006-testnet-first-policy.md](../docs/adr/0006-testnet-first-policy.md) — Mainnet bloku, RestBaseUrl tek otorite
- [loop_1/research.md](./research.md) — binance-expert REST backfill arastirmasi
- [Microsoft Learn — IHostedService lifecycle](https://learn.microsoft.com/en-us/dotnet/core/extensions/generic-host#ihostedservice-interface)
- [Microsoft Learn — BackgroundService vs IHostedService](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services)
- [Binance Spot REST — /api/v3/klines](https://developers.binance.com/docs/binance-spot-api-docs/rest-api/market-data-endpoints#klinecandlestick-data)
