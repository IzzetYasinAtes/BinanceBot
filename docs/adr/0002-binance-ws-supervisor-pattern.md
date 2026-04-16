# 0002. Binance WebSocket Supervisor Pattern

Date: 2026-04-16
Status: Accepted

## Context

BinanceBot BTC/USDT, ETH/USDT, BNB/USDT Spot sembollerini gercek zamanli izleyecek. Binance WebSocket in yapisal kisitlari ([docs/research/binance-research.md §2](../research/binance-research.md)):

- Tek connection **max 24 saat**; server otomatik kapatir.
- Server **her 20 saniyede bir ping** yollar; client 1 dakikada pong atmazsa disconnect.
- **5 mesaj/sn** incoming limit; 1024 stream/connection; 300 connection/5 dk/IP.
- `@depth` diff stream i snapshot + buffered replay disiplini ister (U/u senkronizasyonu).
- Reconnect sonrasi subscription **replay** gerekir; aksi halde stream i kaybederiz.

Saf `while (ws.State == Open)` dongusu yeterli degil; reconnect, backoff, subscription replay, backpressure ve channel-based consumer ihtiyaci var. Bu sebeplerle dedike bir **Supervisor** pattern ADR olarak sabitlenir.

## Decision

**BinanceWsSupervisor** `BackgroundService` olarak Infrastructure layer inda yasar. Sorumluluklari:

1. **ClientWebSocket** yonetimi — tek "combined stream" connection: `wss://stream.binance.com:9443/stream?streams=btcusdt@kline_1m/ethusdt@kline_1m/bnbusdt@kline_1m/btcusdt@bookTicker/.../btcusdt@depth@100ms/...`. Testnet de `wss://stream.testnet.binance.vision/stream?...`. (Bkz. [0006](./0006-testnet-first-policy.md).)
2. **Producer**: ham WS mesaji -> `Channel<WsEvent>` (bounded, `FullMode = DropOldest`, capacity 10k). Parse olmadan ham JSON + stream name + recv timestamp Channel a yazilir.
3. **Consumer** (ayni BackgroundService'in ayri task i veya ayri hosted service): Channel oku, stream tipine gore parse et (Kline/BookTicker/Depth), ilgili MediatR `INotification` yayinla (`KlineIngestedNotification` vb.). Handler lar Application layer inda.
4. **Reconnect state machine**:
   - `Disconnected -> Connecting -> Connected -> Subscribed -> Active`
   - `Active` dan `Disconnected` a dususte: **exponential backoff with jitter** — 1s, 2s, 4s, 8s, 16s, 30s cap; Polly v8 `ResiliencePipelineBuilder` kullan.
   - Her disconnect te subscription listesi bellekten **replay**: ayni `subscribe` mesaji ya da yeniden combined URL.
5. **Heartbeat**: `ClientWebSocket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30)`. Kendi pong hesabini yapmaya gerek yok — .NET pong u otomatik cevaplar. Ayri bir watchdog task i 60 saniyeden uzun sure mesaj yoksa `reconnect` flag i ceker.
6. **Preemptive reconnect**: 23. saatte `CancellationTokenSource` ile ongoing connection i kapat, yenisini ac. 24h cut oncesi failover. Iki connection k.sa bir sure paralel yurutulur (overlap window: ~5 sn) — eski kapanmadan yeni aktif.
7. **Depth U/u resync** (binance-research.md §2.4): `@depth@100ms` ilk event geldiginde buffer a yaz, `GET /api/v3/depth?limit=5000` snapshot cek, resync algoritmasini uygula. Gap tespit edilirse **resync yeniden baslar**, supervisor kendini yeniden subscribe etmez (ayri bir `DepthBookSynchronizer` infrastructure service i yapar).
8. **Clock offset watcher**: saatte bir `GET /api/v3/time`; local clock offset > 2s alarm (structured log WARN + optional `RiskLimitBreachedEvent` tetikle).

Domain boundary: Supervisor ham veriyi parse edip **Application** layer ina `IngestKlineCommand` / `SyncDepthSnapshotCommand` gibi MediatR command i gonderir. Domain aggregate lari (Kline/Depth) Application handler inda repository uzerinden hydrate edilir ve `Ingest()` davranis metodu cagrilir.

## Consequences

### Pozitif

- Reconnect kullanicidan gizlenir; downstream handler lar "always on" gorur.
- Channel-based backpressure producer i consumer dan ayirir; burst durumunda eski mesaj dusurulur (piyasa verisi degistigi icin eskimis mesaj zaten sifat).
- Preemptive reconnect 24h cut gecisini siyah ortalik saniyesiz atlatir.
- Infrastructure Domain e bagimli; ters degil. Clean Architecture kurali korunur.

### Negatif / Tradeoff

- Combined stream tek connection; bir tane bozulursa butun veri akisi kesilir. Alternatif: her symbol icin ayri connection — yonetim agirlasir, connection-per-5dk limitine yaklasir. Secilen: tek combined connection + saglikli reconnect.
- `DropOldest` backpressure burst durumunda veri kaybina yol acar. Kline de bu kabul edilemez (eksik bar) — bu sebeple Kline parse -> DB upsert hattinda REST warmup ile bosluk kapatilir (`IngestKlineCommand` handler i son kline id sini kontrol eder, gap varsa `GET /api/v3/klines` tetikler). Depth de kaybolan diff -> full resync.
- BackgroundService crash ederse ASP.NET host hala ayakta kalir ama WS veri akmaz. `IHostApplicationLifetime` ile crash durumunda uygulamayi stop et (boot failure gibi) veya Polly `OnCircuitOpened` ile alarm yayinla.

### Notr

- `ClientWebSocket` .NET BCL; ekstra kutuphane yok. Binance-connector-net in kendi WS wrapper i var ama Polly / Channel disiplinini kendimiz yazmak daha nette.

## Alternatifler

1. **Binance.Net (JKorf)** — Hazir wrapper; opinionated. Kendi retry, kendi parse. Dezavantaj: bizim Channel + domain event disiplinimize yamalamak gerek; dependency inversion bozulur. Reddedildi.
2. **SignalR** — SignalR Binance ye connect eden bir tool degil; bu alternatif anlamsiz.
3. **Her sembol ayri connection** — Connection-per-5dk limiti (300/IP/5dk) rahatlikla asilabilir ama operasyonel karmasa artar. Reddedildi.
4. **REST polling** — Rate limit felaket, binance-research.md §1.2 karsi cikiyor. Reddedildi.

## Kaynak

- [docs/research/binance-research.md §2.3 Ping/Pong + 24 saat kurali](../research/binance-research.md)
- [docs/research/binance-research.md §2.4 Depth Snapshot + Diff](../research/binance-research.md)
- [docs/research/binance-research.md §2.6 Reconnect + Replay + Idempotent Handler](../research/binance-research.md)
- [Microsoft — System.Threading.Channels](https://learn.microsoft.com/en-us/dotnet/standard/threading/channels)
- [Microsoft — HTTP resilience with Polly v8](https://learn.microsoft.com/en-us/dotnet/core/resilience/http-resilience)
- [binance-spot-api-docs — web-socket-streams.md](https://raw.githubusercontent.com/binance/binance-spot-api-docs/master/web-socket-streams.md)
