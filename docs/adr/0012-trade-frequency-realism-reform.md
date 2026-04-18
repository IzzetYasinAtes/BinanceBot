# 0012. Trade Frequency Reform + 24h Ticker + Stop-Loss Monitor

Date: 2026-04-17
Status: Accepted

> Operasyonel detay (commit-by-commit kod sablonu, dosya/satir listesi, agent zinciri) icin bkz. [`loops/loop_5/decision-frequency.md`](../../loops/loop_5/decision-frequency.md). Bu dokuman normatif karardir.

## Context

Loop 4 (`loops/loop_4/summary.md`) sonu: Paper portfoy `$100 → $95.30` (-%4.7), 40 dakikada sadece 4 trade, `consecutiveLosses=3=max` ama CB hala `Healthy`. Kullanici ulti­matomu net: **"kar etmeliyiz, hizli islem yap, canlida nasilsa oyle calis"** — yani (a) sinyal frekansi yeter­siz, (b) paper ile mainnet davranisi bir noktada divergent (24h ticker, stop trigger, fee/latency), (c) CB tetikleme zinciri bozuk. binance-expert audit'i (loop_5/research-audit.md ozeti — sohbet baglami) 8 kirilim noktasini onceliklendirdi:

**P0 — kritik (canlida farkli davranis)**

1. **Market summary 24h hesaplamasi yanlis.** `GetMarketSummaryQueryHandler` (`src/Application/MarketData/Queries/GetMarketSummary/GetMarketSummaryQuery.cs:53-60`) DB'deki 1m kline'larin 24h onceki bar'ina bakiyor; ama `Binance.BackfillLimit=1000` (`src/Api/appsettings.json:45`) → 1000 dakika ≈ 16h40m → 24h gerisinde kayit YOK → `dayAgoClose=null` → `changePct=0`. Mainnet'te `/api/v3/ticker/24hr` tek REST cagriyla weight=2/sembol gercek 24h yuzdesi doner; bizim implementasyon yanlis bilgi uretiyor. UI'da BTC/BNB/XRP `+0.00%` gosteriyor — kullaniciya yalan.
2. **XRP-Grid bandi olu.** `appsettings.json:81` `LowerPrice=0.50, UpperPrice=0.80`; testnet XRP/USDT `~1.4717`. `GridEvaluator:35-37` band disinda `null` doner — Grid stratejisi **hicbir bar'da sinyal uretmez**. Loop 4'teki 4 trade'in tamami trend/meanrev'den.
3. **Stop-loss izlenmiyor.** `TrendFollowingEvaluator:46` `SuggestedStopPrice` emit ediyor; `StrategySignalEmittedEvent` payload'a giriyor; ama hicbir handler "mark price stop'u gectiyse pozisyonu kapat" mantigini calistirmiyor. ADR-0011 §11.12 server-side OCO'yu ADR-0012'ye otelemisti — su an client-side soft-exit bile yok. Trend pozisyon `-%5` indikten sonra evaluator yeni cross gormezse pozisyon `OPEN` kalir, kayip buyur.

**P1 — frekans (sinyal sayisi)**

4. **TrendFollowing parametreleri agir.** Fast=5/Slow=20 EMA cross 1m timeframe'de gunde 1-3 sinyal verir (ATR 14 + minimum 22 bar). Hedef: gunde 10-25 sinyal. Hizlandirmak `Fast=3 Slow=8` ile mumkun ama whipsaw riski yukselir → false-signal filtresi gerekli (RSI 30-70 bandi disinda cross'u kabul etme).
5. **MeanReversion parametreleri ag­ir.** RSI 30/70 + BB 2σ kombo gunde 1-2 ekstrem nokta verir. Gevsetme yolu: ya RSI 35/65 (sinirlari ice cek) **ya da** BB 1.5σ (bandlari daralt). Iki­sini birden gevsetmek false-signal patlamasi getirir.
6. **Grid count yetersiz.** `GridCount=10` 0.50→0.80 araliginda step 0.03; canli fiyat (1.30→1.65) bandinda bile 10 grid → step 0.035, BTC volatilitesinde dakikada bir bucket atlama nadir. `GridCount=20` step yariya iner, yaklasik 2x bucket-cross hizi.

**P2 — paper realism (mainnet davranisina yakinsama)**

7. **Paper stop trigger yok.** `PaperFillSimulator` MARKET fill ediyor ama acik pozisyonlari mark price'a karsi izlemiyor. Mainnet'te `STOP_LOSS_LIMIT` (Spot OCO) sunucu tarafinda triggered; paper'da iste­dig­imiz ayni davranisin client-side simulation'i: stop fiyati sakla, `BookTickerUpdated`/her bar markPrice geldiginde stop'u gecmisse otomatik market sell. Aksi halde paper kar/zarari overstated; stratejiyi mainnet'e gecince surprise.
8. **Paper latency simulasyonu yok.** Mainnet MARKET fill `~80-120ms` (REST round-trip + matching). Paper anlik fill (`Task.CompletedTask`); fast-cycle stratejilerde overfit. `SimulatedLatencyMs=100` minor ama gercege yakinlasma.

**Yan bulgu — CB bug (ayri dosya, ADR-0012 kapsaminda fix)**

9. Loop 4 t30: `consecutiveLosses=3` (`MaxConsecutiveLosses=3`) ama `CircuitBreakerStatus=Healthy`. `RecordTradeOutcomeCommandHandler:38` zaten `>=` kullaniyor — kod dogru. Olas­i sebepler:
   - (a) `PositionClosedEvent` MediatR pipeline'inda yutuluyor (handler exception loglan­miyor), `RecordTradeOutcomeCommand` hic gelmiyor → consecutiveLosses DB'de hic guncellenmedi, UI 3'u baska kaynaktan gosteriyor;
   - (b) `realizedPnl=0m` (zero-pnl trade) → `RiskProfile.RecordTradeOutcome:149-156` `< 0` ve `> 0` branch'lerine girmez, sayac artmaz; UI snapshot'i baska zamanin;
   - (c) `OrderFilledPositionHandler` (`src/Infrastructure/Orders/OrderFilledPositionHandler.cs`) `Position.Close` cagirsa da `db.SaveChangesAsync` olmadan domain event dispatch olmuyor.
   ADR-0012 audit aksiyonu ekler: `RecordTradeOutcomeCommandHandler:32` once `_logger.LogInformation("CB-AUDIT mode={Mode} pnl={Pnl} consecutiveBefore={Before} ...")` ekle, Loop 5 sonu loga bak; root cause net olunca minor fix (kod 1 satir veya log kanali). Bu **yan-iz** karar — Loop 5 P0/P1/P2 tamam­landiktan sonra baslamasi gereken ek audit.

## Decision

ADR-0011 modelinden ders alindi: tek mega-ADR (Application + Infrastructure + Config + Domain reactivasyon) gondereriliyor. **Yeni aggregate yok.** Mevcut `Position`/`RiskProfile`/`Order` aggregate davranislari korunur; `IBinanceMarketData` arabirimi genisler (yeni metod), `PaperFillSimulator` icine stop-trigger eklenir, yeni `BackgroundService` ve handler genislemeleri Application/Infrastructure'da yasar. Domain dependency rule ihlali yok (Domain saf kalir).

### 12.1 Ticker24h REST Endpoint — Yeni `IBinanceMarketData.GetTicker24hAsync`

**Karar:** `IBinanceMarketData` (`src/Application/Abstractions/Binance/IBinanceMarketData.cs`) yeni metod:

```
Task<IReadOnlyList<Ticker24hDto>> GetTicker24hAsync(
    IReadOnlyCollection<string> symbols,
    CancellationToken cancellationToken);
```

`Ticker24hDto` (`src/Application/Abstractions/Binance/Ticker24hDto.cs`):

```
public sealed record Ticker24hDto(
    string Symbol,
    decimal LastPrice,
    decimal PriceChangePct,   // -2.34 = -2.34%
    decimal HighPrice,
    decimal LowPrice,
    decimal QuoteVolume,
    DateTimeOffset CloseTime);
```

**Impl:** `BinanceMarketDataClient` `/api/v3/ticker/24hr?symbols=[...]` cagirir (Binance Spot REST docs — weight 2 per symbol, 80 max). 3 sembol icin weight=6 — `X-MBX-USED-WEIGHT-1M` 6000 cap altinda fersah fersah. Implementation pattern `GetExchangeInfoAsync` (`BinanceMarketDataClient.cs:45-102`) ile ozdes — `JsonDocument` parse, decimal helpers reuse. Single REST call array doner; tek roundtrip, latency ~200ms.

**`GetMarketSummaryQueryHandler` reform:**

Mevcut akiş (`GetMarketSummaryQuery.cs:30-91`) tamamen rewrite. Yeni akış:

```
Handle(query, ct):
    var ticker24h = await _binance.GetTicker24hAsync(query.Symbols, ct);     // tek REST
    var bookTickers = await _db.BookTickers.AsNoTracking()
                              .Where(b => query.Symbols.Contains(b.Symbol.Value))
                              .ToDictionaryAsync(b => b.Symbol.Value, ct);
    var results = ticker24h.Select(t => new MarketSummaryDto(
        Symbol: t.Symbol,
        LastPrice: t.LastPrice,
        MarkPrice: bookTickers.TryGetValue(t.Symbol, out var bt)
                       ? (bt.BidPrice + bt.AskPrice) / 2m
                       : t.LastPrice,
        ChangePct: t.PriceChangePct,            // GERCEK 24h yuzdesi
        Volume24h: t.QuoteVolume,
        AsOf: t.CloseTime)).ToList();
    return Result.Success<IReadOnlyList<MarketSummaryDto>>(results);
```

DB Klines tablosu artik 24h hesabi icin okunmaz; backfill verisinin 16h40m sinirla­masi market-summary'yi etkilemez. `IApplicationDbContext` constructor injection azalir (sadece BookTicker icin); `IBinanceMarketData` eklenir.

**Cache stratejisi:** Hic yok. `/ticker/24hr` ucuz (weight 2, 200ms); UI 5s polling × 1 = 1.2 RPM, 6000 cap'ta ihmal. `MemoryCache` eklemek prematur; gerekirse Loop 6'da ADR.

**Reddedilen Alternatif A (BackfillLimit'i artir):** `BackfillLimit=1500` (25h) ile mevcut akis duzelir gibi gorunur ama (1) backfill suresi uzar (3 sembol × 1500 = 4500 row), (2) UI hala stale data gosterir (1m bar son kapali bar'a kadar gecikme), (3) gercek 24h ticker rolling-window'dur — bizim kline-based hesabimiz dakikalik bar gridine snap'lenir, ufak farklar olusur. Reddedildi — Binance'in resmi endpoint'i tek kaynak.

**Reddedilen Alternatif B (Hibrit: REST 24h + DB son fiyat):** REST yalniz `priceChangePercent` icin, fiyat DB'den. Reddedildi — iki kaynak divergence riski (REST t=N, DB t=N-30s); tek kaynak (REST) tutarli.

### 12.2 XRP-Grid Bandi — Statik Reseed

**Karar:** `appsettings.json` `Strategies.Seed[2].ParametersJson` testnet gercek fiyatina gore guncellenir:

```json
"ParametersJson":
  "{\"LowerPrice\":1.30,\"UpperPrice\":1.65,\"GridCount\":20,\"OrderSize\":5}"
```

Bant `[1.30, 1.65]` testnet XRP/USDT `~1.4717` etrafinda ±%12 tampon. `GridCount=20` step `0.0175` USD. Order size `5 XRP × 1.4717 = ~7.4 USDT` notional → minNotional 5 USDT'yi asar.

**Operasyonel uyari:** `StrategySeeder.cs:84-85` mevcut Active stratejilerin `ParametersJson` guncellemesini **skip eder** (`existing.Status != Active` kosulu). Bu Loop 5'te problem: yeni bant uygulanmasi icin ya (a) admin UI'dan once "XRP-Grid" deactivate, sonra restart, sonra activate — ya da (b) seeder davranisi degisir: `Active` strateji icin de `ParametersJson` reconcile edilir, `UpdateStrategyParametersCommand` `Active`'ta calisir mi diye Strategy domain davranisi gozden gecirilir. Karar: **`StrategySeeder` davranisi degismez** (Active strateji canli, runtime parametre swap riski; mainnet'te traders'in adli ozelligini bozar). Loop 5 cozumu: backend-dev migration script'i veya `decision-frequency.md` operasyon adimi — admin endpoint'inden manual `DeactivateStrategy → boot → reseed`.

**Reddedilen Alternatif (dynamic auto-band):** Boot'ta `GET /api/v3/ticker/price` ile XRP fiyatini cek, ±%15 etrafinda otomatik band. Reddedildi — config-as-source-of-truth ihlali, restart'lar arasi non-deterministik baseline; ileride stratejiye dahil bir feature olarak ayri ADR'a kalir.

### 12.3 StopLossMonitor — Yeni BackgroundService

**Karar:** PM onerisi kabul edilir. `BackgroundService` (30s tick) tercih sebebi: bar gelmeyen anlarda da koruma (cross-exchange data lag, WS kopukluk), `StrategyEvaluationHandler` hi hizla degil — bar bagimli; oysa stop trigger reaktif olmali.

**Konum:** `src/Infrastructure/Trading/StopLossMonitorService.cs`

**Davranis:**
```
class StopLossMonitorService : BackgroundService
{
    ExecuteAsync(CancellationToken ct):
        while (!ct.IsCancellationRequested):
            try:
                await using scope = _scopeFactory.CreateAsyncScope();
                var db = scope.SP.GetRequiredService<IApplicationDbContext>();
                var mediator = scope.SP.GetRequiredService<IMediator>();

                var openPositions = await db.Positions.AsNoTracking()
                    .Where(p => p.Status == PositionStatus.Open)
                    .ToListAsync(ct);

                foreach (pos in openPositions):
                    // Stop seviyesi: pozisyon acilirken kaydedilen `StopPrice` (yeni Position.StopPrice
                    // alani — §12.4); yoksa skip.
                    if (pos.StopPrice is not decimal stop) continue;

                    var bt = await db.BookTickers.AsNoTracking()
                        .FirstOrDefaultAsync(b => b.Symbol == pos.Symbol, ct);
                    if (bt is null) continue;
                    var markPrice = pos.Side == PositionSide.Long ? bt.BidPrice : bt.AskPrice;

                    var triggered = pos.Side == PositionSide.Long ? markPrice <= stop
                                                                  : markPrice >= stop;
                    if (!triggered) continue;

                    // Idempotent: ayni pozisyon icin bir tur once tetiklenmis olabilir.
                    // CloseSignalPositionCommand `Status=Open` kontrolu yapiyor → ikinci cagri NotFound doner.
                    var cidPrefix = $"stop-{pos.Id}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
                    await mediator.Send(new CloseSignalPositionCommand(
                        pos.Symbol.Value, pos.StrategyId, pos.Mode,
                        $"stop_loss_triggered@{markPrice:F4}_stop={stop:F4}",
                        cidPrefix), ct);
            catch (Exception ex):
                _logger.LogError(ex, "StopLossMonitor tick failed");
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
}
```

**`Program.cs` DI:** `services.AddHostedService<StopLossMonitorService>();`

**Reddedilen Alternatif (`StrategyEvaluationHandler` icinde her bar):** PM onerisi reddedildi (ana metinde acikta). Bar 1m gridinde tetiklenir; mark price 30s arasi 1-2% hareket edebilir, bar bekle­yen tasarim 60s'lik bir gecikme penceresi yaratir. Stop ise *zaman-kritik*. BackgroundService 30s tick + olay-bazli (`BookTickerUpdated`) kombinasyonu en saglikli; ama Loop 5'te sadece BackgroundService impl, event-bazli reaktivite Loop 6+ optimizasyonu (`research-stop-trigger.md`'ye not).

**Reddedilen Alternatif (server-side STOP_LOSS_LIMIT/OCO):** ADR-0011 §11.12'de Loop 4'e (yani su an Loop 5'e) otelenmisti. Hala karmasik (Spot OCO entry-fill bekle-stop-place-cancel-pairing); **iki adimda yapilir**: (a) Loop 5 client-side soft-stop (bu §12.3) — paper + live testnet calisir, mainnet'te WS koparsa risk; (b) Loop 6+ ayri ADR-0013 server-side OCO — mainnet pre-flight icin gerekli. ADR-0011 §11.12 superseded by §12.3 (geçici) + ileri ADR-0013 (kalici).

### 12.4 Position.StopPrice Domain Genislemesi

**Karar:** `Position` aggregate'ine `StopPrice` immutable alani eklenir.

**Domain degisikligi:**
```
// Position.cs
public decimal? StopPrice { get; private set; }   // yeni

public static Position Open(
    Symbol symbol, PositionSide side, decimal quantity,
    decimal entryPrice, decimal? stopPrice,        // yeni parametre, nullable
    long? strategyId, TradingMode mode, DateTimeOffset now)
{
    // ... mevcut validasyon ...
    var position = new Position { ... StopPrice = stopPrice, ... };
}
```

**`Position.Open` cagri yerleri:**
- `OrderFilledPositionHandler` (`src/Infrastructure/Orders/OrderFilledPositionHandler.cs`): order'in `StopPrice` alanini `Position.Open(..., stopPrice: order.StopPrice, ...)` olarak gec.
- `Order.StopPrice` zaten var (Order aggregate); `PlaceOrderCommand`'a `StopPrice` parametresi eklenirse `StrategySignalToOrderHandler:153-163` `notification.SuggestedStopPrice` degerini Order'a aktarir → `Order.StopPrice` set olur → fill aninda `Position.StopPrice` olur.

**Migration:** `Positions.StopPrice` decimal(18,8) NULL kolonu — `dotnet ef migrations add AddPositionStopPrice`. Mevcut row'lar `NULL` (geriye donuk uyumlu); yeni acilan pozisyonlar evaluator stop verirse dolu, vermezse NULL → StopLossMonitor skip.

**Gerekce — neden Domain alani, neden DTO/cache/dictionary degil:**
- Stop seviyesi pozisyonun is-anlami (entry+stop birlikte risk semasi); Eric Evans terminolojisinde aggregate state'i.
- Persistence gerekli (restart sonrasi stop kaybolmamali).
- Cross-aggregate query gerekmiyor — pozisyona ait tek deger.

**Reddedilen Alternatif (`StrategySignal` aggregate'ine bagli tut):** Stop sinyaldedir, pozisyon sinyali ariyor. Reddedildi — sinyal-pozisyon iliskisi `StrategyId` uzerinden ama bir strateji icin coklu sinyal/pozisyon olabilir; hangisi hangi stop degeri belirsiz; pozisyon kapaninca da sinyal record kalir, gereksiz lookup karmasi.

### 12.5 TrendFollowing Parametreleri + RSI Filtresi

**Karar:** PM onerisi (yari atilim) kabul edilir. `appsettings.json` `Strategies.Seed[0].ParametersJson`:

```json
"ParametersJson":
  "{\"FastEma\":3,\"SlowEma\":8,\"AtrPeriod\":14,\"AtrStopMultiplier\":2.0,\"OrderSize\":0.001,\"RsiPeriod\":14,\"RsiMin\":30,\"RsiMax\":70}"
```

**Evaluator degisikligi (`TrendFollowingEvaluator.cs:11-18`):**
```
private sealed class Parameters
{
    public int FastEma { get; set; } = 3;
    public int SlowEma { get; set; } = 8;
    public int AtrPeriod { get; set; } = 14;
    public decimal AtrStopMultiplier { get; set; } = 2.0m;
    public decimal OrderSize { get; set; } = 0.001m;
    public int RsiPeriod { get; set; } = 14;
    public decimal RsiMin { get; set; } = 30m;     // RSI bu altindaysa cross sinyalini reddet (asiri satım)
    public decimal RsiMax { get; set; } = 70m;     // RSI bu ustundeyse cross sinyalini reddet (asiri alim)
}
```

**Algoritma genislemesi (cross sonrasi, sinyal donmeden once):**
```
var crossedUp = ...; var crossedDown = ...;
if (!crossedUp && !crossedDown) return null;

// RSI filtresi — extremes'de cross unreliable (whipsaw)
var rsi = Rsi(closedBars, p.RsiPeriod);   // MeanReversionEvaluator'dan kopyala (DRY: helper class)
if (rsi < p.RsiMin || rsi > p.RsiMax) return null;
// ...mevcut ATR + stop hesabi...
```

**Gerekce:** EMA(3,8) cross gunde 8-15 sinyal verir; RSI<30 (asiri satim) sirasi­nda crossUp = "dead cat bounce" buyuk olasilikla; RSI>70 sirasi­nda crossDown = "false dip", whipsaw. Filtre %30-50 sinyalleri eler; hedef nett 5-10 kaliteli/gun. Backtester yok (Loop 6+) — Loop 5 gozlem-driven; t30 raporunda log "trend.signal_filtered.rsi_extreme" sayilir.

**Reddedilen Alternatif (sadece parametre, RSI yok):** Whipsaw riski (Loop 4 trend pozisyonlari -%4.7 PnL, 4/4 stop-out yakin). Reddedildi — frekans + filtre ayni anda.

**Reddedilen Alternatif (ADX/MACD filtresi):** Daha guclu trend filtresi ama yeni indikator + parametre. Reddedildi — Loop 5 minimum kapsam; RSI evaluator'da zaten Mean­Reversion'da var, helper class refactor + reuse.

**DRY refactor:** `Rsi` ve `Ema` static helper'lari `src/Infrastructure/Strategies/Evaluators/Indicators.cs` (yeni dosya) icine tasinir; iki evaluator (Trend + MeanRev) buradan cagirir. KISS — ekstra abstraction yok, sadece method group.

### 12.6 MeanReversion Parametreleri

**Karar (Loop 5 — kullanici ultimatomu sonrasi guncellenmistir):** **Hem RSI eşikleri gevsetilir HEM BB stdDev gevsetilir.** Kullanici "hizli islem yap, kar etmeliyiz" demesi sonrasi PM bilinçli sapma kabul edildi.

**`appsettings.json` `Strategies.Seed[1].ParametersJson`:**
```json
"ParametersJson":
  "{\"RsiPeriod\":14,\"RsiOversold\":35,\"RsiOverbought\":65,\"BbPeriod\":20,\"BbStdDev\":1.5,\"OrderSize\":0.01}"
```

**Gerekce:** RSI 30/70 → 35/65 (ekstrem dar tanim daha geniş), BB 2.0σ→1.5σ bandlari `~%87`'e iner. AND koşulu (`MeanReversionEvaluator:36`) korur ama her iki gevşetme `false-signal patlamasi` riski taşir — Loop 5 t30/t90/t150 health check'lerinde gozlenecek. Eger sinyal çok artar ve win rate düşerse Loop 6'da geri çekilebilir.

**Notı (orijinal karar):** ADR §12.6 ilk versiyonu "sadece BB gevsetilir" diyordu. Loop 5 boot'ta kullanici talimati doğrultusunda her iki parametre birden değiştirildi.

Beklenen frekans: gunde 4-8 (Loop 4 ~1-2'den 3-4x).

**Reddedilen Alternatif (RSI 35/65 + BB 2.0):** Ekstrem-nokta tanimi gevser, RSI'in is amaci kaybolur. Reddedildi.

### 12.7 Grid Count Artisi

**Karar:** `GridCount=20` (§12.2'de zaten yazildi). Step yariya iner, bucket-cross hizi 2x.

**Tradeoff:** Daha kucuk step → kucuk gurultude bile yanlis sinyal. `GridEvaluator:50-53` `priorBucket == latestBucket` kontrolu mevcut — 1 bar icinde ayni bucket'ta kalisa skip; bu gurultu filtresi step kuculmesiyle daha agresif calismaya basliyor. Net: hizli ama biraz yanli sinyalle (Loop 6+ icin grid stop-loss/profit-take onerisi).

### 12.8 PaperFillSimulator — Stop Trigger Simulasyonu (P2-A)

**Karar:** `PaperFillSimulator` tek-shot fill engine'dir; "acik pozisyonu izle ve stop'da kapat" davranisi mantik olarak `StopLossMonitorService` icindedir (§12.3) — paper modu icin de ayni service calisir. Yani **paper-spesifik stop-trigger eklenmez**; mevcut `StopLossMonitorService` Paper/LiveTestnet/LiveMainnet modlarinin uçunu de tarar. Fan-out (ADR-0008) prensibine uygun.

Tek paper-spesifik dokunus: `CloseSignalPositionCommand` Paper modunda `PlaceOrderCommand` → `PaperFillSimulator.Simulate` zincirine duser; mevcut akis. Yeni kod yok; `StopLossMonitorService` doǧru calistiginda paper'da da otomatik stop-out olur. **Iddia: §12.3 + §12.4 P2-A'yi karsilar; ayri "PaperStopSimulator" gereksiz YAGNI.**

### 12.9 PaperFillSimulator — Latency Simulasyonu (P2-B)

**Karar:** `PaperFillOptions` (zaten var, ADR-0011 §11.9) `SimulatedLatencyMs` alani eklenir.

```
public sealed record PaperFillOptions
{
    public decimal FixedSlippagePct { get; init; } = 0.0005m;
    public int SimulatedLatencyMs { get; init; } = 100;   // yeni — mainnet MARKET fill ortalamasi
}
```

`PaperFillSimulator.Simulate` basinda:
```
public async Task<PaperFillOutcome> Simulate(...)   // signature `Task<>` olur
{
    if (_options.SimulatedLatencyMs > 0)
    {
        await Task.Delay(_options.SimulatedLatencyMs, ct);   // ct PaperFillSimulator.Simulate'e gecilmeli
    }
    // ... mevcut sync logic
}
```

**Imza degisikligi:** `IPaperFillSimulator.Simulate` artik `Task<PaperFillOutcome>` doner ve `CancellationToken` alir. Cagri yerleri: `PaperFillOrderExecutor` (Order placement pipeline). Mevcut `Simulate` sync — async'e cevrilir, `await` ile cagrilir. Test: `PaperFillSimulatorTests` Async assertion'a guncellenir.

**`appsettings.json`:**
```json
"PaperFill": {
  "FixedSlippagePct": 0.0005,
  "SimulatedLatencyMs": 100
}
```

**Tradeoff:** Backtest sirasinda 100ms gecikme topludur (1000 trade × 100ms = 100s ek sure). Backtester yok henuz (Loop 6+); production worker icin ihmal. Test'lerde `SimulatedLatencyMs=0` ile override.

### 12.10 CB Bug Audit Hattı

**Karar:** Root cause net degil (Loop 4 health snapshot stale olabilir). ADR-0012 once **gozlem altyapisi** ekler, fix sonra:

1. `RecordTradeOutcomeCommandHandler.cs:32` ICN ek log:
   ```
   _logger.LogInformation(
       "CB-AUDIT mode={Mode} pnl={Pnl} consecBefore={Before} statusBefore={StatusBefore} drawdownBefore={DDBefore}",
       request.Mode, request.RealizedPnl, profile.ConsecutiveLosses,
       profile.CircuitBreakerStatus, profile.CurrentDrawdownPct);
   profile.RecordTradeOutcome(...);
   _logger.LogInformation(
       "CB-AUDIT mode={Mode} consecAfter={After} statusAfter={StatusAfter} drawdownAfter={DDAfter}",
       request.Mode, profile.ConsecutiveLosses, profile.CircuitBreakerStatus, profile.CurrentDrawdownPct);
   ```
2. `PositionClosedRiskHandler.cs:30` once log: `"PositionClosed handler entered pos={PosId} mode={Mode}"`.
3. `OrderFilledPositionHandler` (mevcut): fill akisinda `Position.Close` cagrilirsa `_logger.LogInformation("Position closed via fill {PosId} pnl={Pnl}")`.

Loop 5 t30 sonrasi log analizinden root cause cikar; tek-satir fix `decision-frequency.md`'ye P3 olarak eklenir (acil degil — gozlem-bagimli).

**Reddedilen Alternatif (kor fix `>=` → `>`):** Yanlis taraf. `>=` dogru. Reddedildi.

### 12.11 Cakisma Kontrolu (ADR-0005 / 0006 / 0008 / 0009 / 0010 / 0011)

- **ADR-0005 §5.3 server-side stop:** ADR-0011 §11.12 ADR-0012'ye otelemisti. ADR-0012 §12.3 client-side soft-stop ekler (kismi). Tam server-side OCO Loop 6+ `ADR-0013 OCO Pre-Flight`'ta kalir. ADR-0005 §5.3 hala kismi ihlal — kabul edilen risk: paper + livetestnet (no creds), mainnet bloklu (ADR-0006).
- **ADR-0006 testnet-first:** `Binance.AllowMainnet=false`; mainnet branch tum yeni kodda skip (StopLossMonitor mode-agnostik calisir, mainnet creds yok → CloseSignalPositionCommand zaten skip).
- **ADR-0008 §8.3 fan-out per mode:** §12.3 StopLossMonitor `Status=Open` filtresiyle tum modu tarar; her pozisyon kendi modunda kapatma alir (CloseSignalPositionCommand mode parametresi). Uyumlu.
- **ADR-0009 backfill:** §12.1 reform DB Klines'a bakmaz → backfill BackfillLimit ile market-summary yansimaz; zaten backfill stratejiler icin yeterli (250 bar evaluator history). Uyumlu.
- **ADR-0010 backfill event suppression:** Backfill suresince `KlineClosedEvent` susar; StopLossMonitor bagimsiz, `BookTickerUpdated` (live WS) ile mark price taze. Uyumlu.
- **ADR-0011 §11.5 slippage / §11.9 PaperFillOptions:** §12.9 `SimulatedLatencyMs` ayni record'a eklenir (genisleme); §11 davranisi degismez. Uyumlu.
- **ADR-0011 §11.6 OrderFilledPositionUpdater:** §12.4 `Position.StopPrice` parametresi `OrderFilledPositionHandler`'da `Order.StopPrice`'tan okunup `Position.Open(..., stopPrice: order.StopPrice, ...)` olarak gecirilir. Mevcut handler genisler, semantik bozulmaz.
- **ADR-0011 §11.7 RiskProfile.RecordTradeOutcome:** §12.10 audit log'lari ekler; davranis degismez.

## Consequences

### Pozitif
- UI 24h yuzdesi gercek mainnet/testnet rakaminda — kullaniciya yalan bitti.
- XRP-Grid aktif — 3 strateji + 3 sembol fan-out tam dolu.
- Stop-loss hem paper hem testnet'te calisir; trend/meanrev pozisyonlari tanimli risk altinda.
- Frekans hedefi: trend gunde 5-10 (RSI filtresi netti), meanrev 4-8, grid 8-15 → toplam 17-33 sinyal/gun. Loop 4'un 4 trade/40dk (gunde ~144) seviyesi degil ama gercekci.
- Paper fill latency simulasyonu mainnet'e yakinlasma; over-optimistic backtest yok.
- CB audit altyapisi root cause icin veri toplar.
- Yeni domain davranisi minimal (`Position.StopPrice` tek alan); Clean Architecture dependency rule ihlali yok.

### Negatif / Tradeoff
- `IPaperFillSimulator.Simulate` sync→async imza degisikligi — `PaperFillOrderExecutor` (cagri yeri) ve tum testler async update; reviewer `await` zincirini denetler. Boilerplate.
- StopLossMonitor 30s tick volatilite anlik degisimde gec kalir (2-3% aniden hareket → 30s gecikmeyle stop). ADR-0013 (Loop 6+) OCO server-side fix; Loop 5'te kabul edilir.
- TrendFollowing RSI filtresi yanlis-negative riski (gercek trend ekstremde baslar → filtre eler). Backtest yok, gozlem-driven; `loops/loop_6/research-trend-filter.md` ayri.
- BB 1.5σ false-positive riski — meanrev biraz daha sik, kuçuk hareketleri "mean reversion" sayar. Net etki Loop 5 sonu raporunda.
- `XRP-Grid` reseed icin operatorel adim (deactivate → restart → activate) — `decision-frequency.md` P0-2 commit prosedurunde acik talimat.
- `Position.StopPrice` migration — DB schema degisir (`AddPositionStopPrice`). EF migration gerek; mevcut prod yok, riskce dusuk.
- `IBinanceMarketData.GetTicker24hAsync` testnet endpoint'i destekliyor (`https://testnet.binance.vision/api/v3/ticker/24hr` testnet'te de aktif); ama testnet rate limit daha sıki olabilir — gozlem.

### Notr
- Domain event sayisi artmaz (mevcut `PositionOpenedEvent` `StopPrice` icermez — eklemek isteyen ADR-0014).
- Yeni MediatR command/query yok (mevcut `CloseSignalPositionCommand` reuse).
- DI registration icin tek ekleme: `services.AddHostedService<StopLossMonitorService>()`.

## Alternatifler (uzun)

### Alt-1: Tek mega-ADR yerine 4 ayri ADR
ADR-0012a Market Summary, ADR-0012b Frequency, ADR-0012c StopLoss, ADR-0012d Paper Realism. Reddedildi — 4 ADR muhasebesi birlikte yapildiginda bagimli (StopLoss → Position.StopPrice → Frequency RSI'in stop ile etkilesimi); split = lookup yorgunlugu. ADR-0011 modeli tek mega-ADR ile basariliydi (§11.1-§11.12); tekrarlanir.

### Alt-2: Stop-loss `MarkToMarket` event'inde
Pozisyon `MarkToMarket` cagrisi her bookTicker update'inde olur (eger mevcut kod baglandiysa); orada stop kontrolu yap. Reddedildi — `MarkToMarket` Domain metodu, side-effect (komut send) yapamaz; ayrica acik pozisyon icin `MarkToMarket` cagrisinin sistematik nerede yapildigini denetlemek gerek. BackgroundService daha temiz.

### Alt-3: 24h ticker REST yerine WS `!miniTicker@arr`
Binance WS stream'i `!miniTicker@arr` her sembol icin 1s'de mini-ticker (24h fiyat + yuzde + hacim) push eder. Reddedildi su an — WS supervisor karmasi (yeni stream, yeni dispatcher); `/ticker/24hr` REST 5s polling weight 6 → ucuz. Loop 6+ optimizasyon (UI live-flicker kalitesi icin).

### Alt-4: TrendFollowing'de MACD filtresi
Daha guclu trend filtresi (12-26-9 standard). Reddedildi — yeni indikator, RSI helper var (DRY), MACD parametre yiku artar. RSI filtresi mvp.

### Alt-5: Grid 30+ count
Step `0.012` cok kucuk; gurultu bucket-cross uretir (false sinyal). 20 mvp; ileri loop'ta backtest sonrasi tune.

## Kaynak

- [ADR-0005 Risk Limit Policy](./0005-risk-limit-policy.md) §5.3
- [ADR-0006 Testnet-First Policy](./0006-testnet-first-policy.md)
- [ADR-0008 Trading Modes Fan-Out](./0008-trading-modes.md) §8.2 §8.3 §8.6
- [ADR-0009 REST Kline Backfill On Boot](./0009-rest-kline-backfill-on-boot.md)
- [ADR-0010 Backfill Event Suppression](./0010-backfill-event-suppression.md)
- [ADR-0011 Equity-Aware Sizing & Risk Tracking](./0011-equity-aware-sizing-and-risk-tracking.md) §11.5 §11.6 §11.7 §11.9 §11.12
- [Binance Spot REST — 24hr Ticker](https://binance-docs.github.io/apidocs/spot/en/#24hr-ticker-price-change-statistics)
- [Binance Spot REST — symbol-array endpoints](https://binance-docs.github.io/apidocs/spot/en/#general-info)
- [jasontaylordev/CleanArchitecture — BackgroundService pattern](https://github.com/jasontaylordev/CleanArchitecture)
- [ardalis/CleanArchitecture — domain event reactor pattern](https://github.com/ardalis/CleanArchitecture)
- [Eric Evans — Domain-Driven Design ch. 5 Aggregate state](https://www.dddcommunity.org/learning-ddd/what_is_ddd/)
- [loop_5/decision-frequency.md](../../loops/loop_5/decision-frequency.md) — operasyonel commit-by-commit
- [loop_4/summary.md](../../loops/loop_4/summary.md) — Paper $100→$95.30 raporu
