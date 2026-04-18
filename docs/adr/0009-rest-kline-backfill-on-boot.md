# 0009. REST Kline Backfill On Boot

Date: 2026-04-17
Status: Accepted

> Bu ADR'in operasyonel detayli uretim notu `loops/loop_1/decision.md` icindedir. Burada normatif karar ozeti tutulur.

## Context

`KlineIngestionWorker` yalnizca WS akisindan beslenir. Her DB drop / fresh boot sonrasi evaluator'lar (TrendFollowing SlowEma=20, ATR=14; MeanReversion BB=20, RSI=14) ~20-30 dk warmup yasiyor. 4h test loop'larinda olculen "calisan strateji suresi" bozuluyor.

binance-expert arastirmasi (`loops/loop_1/research.md`):
- `GET /api/v3/klines` testnet'te calisir, weight=2/istek; 1m x 1000 bar tek istekte (~16h 40dk derinlik).
- 3 sembol = 6 weight; mainnet 1200/dk limitinin %0.5'i.
- `BinanceMarketDataClient.GetKlinesAsync` hazir; `KlineIngestionWorker.PersistAsync` zaten upsert (bkz. [0003 §3.1](./0003-idempotent-handler-discipline.md)).
- Sira **WS subscribe ONCE -> REST backfill SONRA**; aksi halde gap olusur.

Mainnet kalici bloklu ([0006](./0006-testnet-first-policy.md)); yeni kod `BinanceOptions.RestBaseUrl`'i okumaz, hardcoded URL yazmaz.

## Decision

### 9.1 Konum
Yeni `KlineBackfillWorker : IHostedService` — ayri dosya, SRP. `BackgroundService` degil, cunku one-shot semantik. `IHostedService.StartAsync` icinde **`Task.Run` ile fire-and-forget**, `StartAsync` Task.CompletedTask doner; host startup blocklanmaz.

### 9.2 Sira garantisi
Iki katman:
1. DI siralamasi — `BinanceWsSupervisor` once `AddHostedService`, ardindan `KlineBackfillWorker`. ASP.NET Core `StartAsync`'leri kayit sirasiyla cagirir.
2. Defensive: yeni `IWsReadinessProbe` interface'i `BinanceWsSupervisor`'a takilir. `IsReady => state in (Subscribing, Streaming)`. Worker max 10s probe poll (200ms aralik); timeout halinde WARN + yine de calisir.

`Task.Delay(N)` magic-number reddedildi.

### 9.3 Hata davranisi
Per-sembol retry-then-skip. HTTP retry zaten `binance-rest-resilience` Polly pipeline'inda (3 deneme, exponential). Worker ekstra retry **eklemez**. Tek sembol fail -> WARN, digerleri devam. Tum sembol fail -> ERROR, host crash etmez.

Fail-fast reddedildi: backfill performans optimizasyonu, kritik path degil. WS-only mod degradation graceful.

### 9.4 Konfigurasyon
`BinanceOptions` icine 3 alan:
- `BackfillEnabled` (bool, default `true`).
- `BackfillLimit` (int, [1..1000], default 1000).
- `BackfillIntervals` (string[], default `["1m"]`) — `KlineIntervals`'tan ayri tutulur ki backfill ve WS abonelik politikasi bagimsiz tunelenebilsin.

### 9.5 Sembol kaynagi
`BinanceOptions.Symbols` listesi (WS supervisor ile ayni). StrategySeeder reddedildi — WS-backfill simetrisi bozulurdu.

### 9.6 Idempotency / Domain disiplini
[0003 §3.1](./0003-idempotent-handler-discipline.md) Kline upsert kontrati AYNEN gecerli. Backfill ve WS path'i ortak `KlinePersister` (Infrastructure scoped service) uzerinden gider — DRY. REST'ten gelen acik bar `IsClosed=false` ile yazilir; WS sonradan `IsClosed=true` ile gunceller.

### 9.7 Mainnet uyumu
Yeni kod `RestBaseUrl`'i **okumaz**, URL hardcode etmez. `IBinanceMarketData` mevcut `binance-rest` named client uzerinden gider. [0006](./0006-testnet-first-policy.md) bootguard tek otorite.

### 9.8 Test
- Unit: `KlineBackfillWorkerTests` — `IBinanceMarketData` ve `IWsReadinessProbe` mock; 4 senaryo (disabled, all-success, partial-fail, ws-timeout).
- Integration smoke: `WebApplicationFactory<Program>` + InMemory DB + double `IBinanceMarketData`. Boot sonrasi `Klines` rows >= 1000 / sembol.
- Canli testnet HTTP testi yok (CI flake); arastirma loop'unda manuel dogrulandi.

## Consequences

### Pozitif
- Boot sonrasi evaluator warmup ~20-30 dk -> ~0 dk (1m x 1000 bar yeterli).
- WS-only failover yolu acik; backfill bozulursa system bozulmaz, sadece warmup degrade eder.
- Mevcut ADR'lar ile sifir catismayla kompoze.

### Negatif / Tradeoff
- Yeni hosted service + 3 config alani: yuzey artar.
- `IWsReadinessProbe` indirection — testabilite kazanci icin kabul.
- 1m-only basliyor; cok-interval strateji eklenirse `BackfillIntervals` ve derinlik politikasi yeniden degerlendirilmeli.

### Notr
- HTTP weight maliyeti negligible.
- WS ile concurrent yazim race-condition zararsiz (upsert garanti).

## Alternatifler

1. `KlineIngestionWorker.ExecuteAsync`'a gomme — SRP ihlali, test zoru. Reddedildi.
2. `BackgroundService` icine `if (!_backfilled)` — once-and-done semantigi yanlis sinyal. Reddedildi.
3. `IHostApplicationLifetime.ApplicationStarted` callback — async kontrolsuz, shutdown CT belirsiz. Reddedildi.
4. `Task.Delay(2000)` ile sira garantisi — magic number, env-bagimli. Reddedildi.
5. Fail-fast (boot crash) — degradation > crash. Reddedildi.
6. `KlineIntervals` reuse — bagimsiz tuning kapanir. Reddedildi.
7. StrategySeeder'dan sembol — WS-backfill simetrisi bozulur. Reddedildi.
8. Canli testnet smoke — CI flake. Reddedildi.

## Kaynak

- [0002-binance-ws-supervisor-pattern.md](./0002-binance-ws-supervisor-pattern.md)
- [0003-idempotent-handler-discipline.md](./0003-idempotent-handler-discipline.md)
- [0006-testnet-first-policy.md](./0006-testnet-first-policy.md)
- [loop_1/research.md](../../loops/loop_1/research.md)
- [loop_1/decision.md](../../loops/loop_1/decision.md)
- [Microsoft Learn — Generic Host / IHostedService](https://learn.microsoft.com/en-us/dotnet/core/extensions/generic-host#ihostedservice-interface)
- [Binance Spot REST — /api/v3/klines](https://developers.binance.com/docs/binance-spot-api-docs/rest-api/market-data-endpoints#klinecandlestick-data)
