# 0011. Equity-Aware Sizing, Exit Handling ve Risk Tracking Reformu

Date: 2026-04-17
Status: Accepted

> Operasyonel detay (kod sablonu, dosya/satir listesi, agent zinciri) icin bkz. `loops/loop_3/decision-sizing.md`. Burada normatif karar.

## Context

Loop 2 sonrasi PM tanisi (`loops/loop_3/plan.md` P0 #3+#4+#7+#8) ve binance-expert arastirmasi (`loops/loop_3/research-sizing.md`) birlesik tek bir kirilma noktasini ortaya koydu: BinanceBot'un risk yonetimi **kagit uzerinde var, calisma anlaminda yok**. Detaylar:

1. **Sizing equity-aware degil.** `TrendFollowingEvaluator` ve `MeanReversionEvaluator` `parametersJson.OrderSize`'tan sabit `qty` doner (BTC 0.001, BNB 0.01). Sermaye buyuse de kuculse de ayni miktar. Quarter Kelly + risk-per-trade pratigi ([ADR-0005](./0005-risk-limit-policy.md)) calismiyor.
2. **`StrategySignalToOrderHandler` hardcode `0.001m` BTC** (mevcut satir 56). Sinyal `SuggestedQuantity` taşisa bile fan-out handler okumuyor — fixed `0.001m` her zaman; BNB icin $0.63 notional → minNotional `5 USDT` altina duser → testnet reject; XRP icin stepSize 0.1 ile uyumsuz.
3. **`PaperFillSimulator.ValidateFilters` MARKET icin minNotional kontrolu yok** (`src/Infrastructure/Trading/Paper/PaperFillSimulator.cs:181-211`). LIMIT yolunda `notional < instrument.MinNotional` kontrol ediliyor ama MARKET branch'i icinde yok — paper'da `$2` MARKET gecer, mainnet ayni siparisi reject eder. Mode-fan-out (ADR-0008) **divergent davranis** uretir.
4. **Exit sinyali yok sayiliyor.** `StrategySignalToOrderHandler:31-34` `if (notification.Direction == Exit) return;` log + sessizce drop. Acik pozisyonlar surekli OPEN; trend stratejisi cikis veremiyor; risk hesaplari (`RealizedPnl24h`) hep 0.
5. **RiskProfile guncellemesi sadece `PositionClosedEvent`'e bagli** — pozisyon kapatma akisi yoksa, bu handler hic tetiklenmez. Sonuc: `RealizedPnl24h=0`, `PeakEquity=0`, `CurrentDrawdownPct=0`, `CircuitBreakerStatus=Healthy` permanent. CB hicbir zaman trip olmaz.
6. **Risk parametre default'lari boot'tan gelmiyor.** `RiskProfile.CreateDefault` 3 mode icin (`ADR-0008 §8.6`) `RiskPerTradePct=0.01`, `MaxPositionSizePct=0.10` yaziyor; ama `StrategyConfigSeeder`'a paralel `RiskProfileSeeder` yok — boot davranisi tek kaynaktan tanimli degil.
7. **Slippage modeli yok.** Paper, depth `bookTicker` (single-level) varsa best-ask/bid ile dolduruyor; gercek mainnet'te 5-10 bps slippage tipiktir; testnet depth zayif. `loops/loop_3/research-sizing.md` `%0.05` sabit slippage onermistir.
8. **Cikis emrinde Stop-Loss yok.** [ADR-0005 §5.3](./0005-risk-limit-policy.md) "her trade'le paired STOP_LOSS_LIMIT" diyor; mevcut implementasyon `StopPrice` field'ini Order'a yaziyor ama Binance tarafinda OCO atmiyor. Bu **ADR-0011 scope disinda** — ayri ADR-0012 STOP/OCO icin reserve edilir; Loop 3'te sadece **client-side soft-exit** (Evaluator Direction.Exit + ClosePositionCommand) eklenir, `loops/loop_3/decision-sizing.md`'de net.

## Decision

Tek aggregate degisikligi yok; mevcut `RiskProfile` ve `Position` davranislari korunur, **sizing + exit + risk-tracking** Application/Infrastructure katmanlarinda yeni servisler ve handler genisletmeleriyle cozulur. Domain davranis genisletmeleri minimal.

### 11.1 PositionSizingService (Application)

**Yeni interface:** `BinanceBot.Application.Abstractions.Trading.IPositionSizingService`

```
PositionSizingResult Calculate(PositionSizingInput input);

record PositionSizingInput(
    decimal Equity,
    decimal EntryPrice,
    decimal StopDistance,    // |entry - stop|; 0 ise risk yerine notional cap kullanilir
    decimal RiskPct,         // RiskProfile.RiskPerTradePct
    decimal MaxPositionPct,  // RiskProfile.MaxPositionSizePct
    decimal MinNotional,     // Instrument.MinNotional
    decimal StepSize,        // Instrument.StepSize
    decimal MinQty,          // Instrument.MinQty
    decimal SlippagePct);    // PaperFillOptions.FixedSlippagePct (LiveTestnet/LiveMainnet 0)

record PositionSizingResult(
    decimal Quantity,        // 0 = SKIP
    decimal NotionalEstimate,
    string? SkipReason);     // "min_notional_floor", "qty_below_min_qty", "equity_zero", null=ok
```

**Konum:** `src/Application/Sizing/PositionSizingService.cs` (interface ve impl ayni dosya, **pure** — `IClock`/`IDb` injection yok; deterministik, test-friendly).

**Algoritma (research-sizing.md adim 1-6):**

```
1. effectiveEntry = entryPrice * (1 + slippagePct)        // BUY worst-case; SELL semantic mirror
2. riskAmount     = equity * riskPct
3. qtyByRisk      = stopDistance > 0 ? riskAmount / stopDistance : decimal.MaxValue
4. notionalCap    = equity * maxPositionPct
5. qtyByCap       = notionalCap / effectiveEntry
6. qtyRaw         = min(qtyByRisk, qtyByCap)
7. qtyStepped     = floor(qtyRaw / stepSize) * stepSize
8. if qtyStepped < minQty → SKIP("qty_below_min_qty")
9. notional       = qtyStepped * effectiveEntry
10. if notional < minNotional → SKIP("min_notional_floor")
11. return (qtyStepped, notional, null)
```

**Cagri yeri:** §11.3 — `StrategySignalToOrderHandler`. `IStrategyEvaluator` arayuzu **degismez**; `SuggestedQuantity` field'i deprecated kabul edilir (geriye donuk uyumluluk; yeni evaluator'lar `0` doner ve handler ignore eder, mevcut iki evaluator field'i okur ama handler override eder).

**Gerekce — neden Application servisi, neden Domain'de degil:**
- Sizing **cross-aggregate**: `RiskProfile` (yuzdeler), `Instrument` (filtreler), `VirtualBalance` (equity), `BookTicker` (entry estimate). Tek aggregate'a sigmaz; Domain Service de Domain'de cross-aggregate okuma yapamaz (DB I/O Application).
- Pure algorithm: `static Calculate` testleri en az 12 senaryo (qty stepping, notional floor, slippage, zero stopDistance, zero equity, BNB 0.001 step, XRP 0.1 step) — pure fonksiyon iste burada.
- DDD literaturunde "Position Sizing" `Strategy Service` kategorisi — Eric Evans "Domain-Driven Design" ch. 5: "operations don't belong on aggregates" → Service.

### 11.2 InstrumentMetadata Kaynagi

**Karar:** Mevcut `Instrument` aggregate kullan (`src/Domain/Instruments/Instrument.cs` — zaten `MinNotional`, `StepSize`, `TickSize`, `MinQty`, `MaxQty` field'lari var). **Yeni aggregate yok.** `appsettings.json` veya yeni boot worker'a gerek yok — Loop 3 oncesi seed mevcut.

**Reddedilen Alternatif A:** Boot'ta `IBinanceExchangeInfoService` REST cek + `Instrument.UpdateFilters` cagir, hourly refresh. Reddedildi cunku:
- Testnet'te BTC/BNB/XRP filtreleri **statik** (5 USDT minNotional, ay/yıl degismez).
- Boot worker `KlineBackfillWorker`'a benzer bir background service eklemek bagimliligi artirir.
- Filter degisimi nadirdir; manuel `POST /api/instruments/refresh` endpoint'i (admin) ile tetiklenebilir — Loop 4+ scope'a otelendi.

**Reddedilen Alternatif B:** `appsettings.json` `Instruments[]` array. Reddedildi — ayni veriyi iki yerde tutmak (DB + config) divergence riski; mevcut seed yeterli.

**Karar:** `IInstrumentReader` adli read-only Application interface (`src/Application/Abstractions/Trading/IInstrumentReader.cs`) sadece sizing servisinin handler'a `Instrument` bagimliligini izole eder; impl tek satir `_db.Instruments.AsNoTracking().FirstOrDefaultAsync(...)`. Onerilir ama not-blocker; backend-dev karar verirse handler dogrudan `IApplicationDbContext` okur.

### 11.3 Fan-Out Handler'da Sizing — Evaluator Sadece Direction

**Karar:** `StrategySignalToOrderHandler` (Infrastructure) sizing'i orada hesaplar, **evaluator sadece Direction + Stop hint doner**. PM onerisi kabul edilir (clean separation: evaluator pure signal generator, sizing infrastructure-aware decision).

`IStrategyEvaluator.StrategyEvaluation` record degismez (geriye donuk), ama handler `SuggestedQuantity` field'ini **ignore** eder. Yeni handler psuedo:

```
foreach (var mode in [Paper, LiveTestnet, LiveMainnet]):
    1. equity = await GetEquityForMode(mode, ct)         // §11.4
    2. risk   = await db.RiskProfiles.AsNoTracking()
                   .FirstOrDefaultAsync(r => r.Id == RiskProfile.IdFor(mode))
       if risk is null OR risk.CircuitBreakerStatus == Tripped:
           log + continue (next mode)
    3. instr  = await db.Instruments.AsNoTracking()
                   .FirstOrDefaultAsync(i => i.Symbol.Value == notification.Symbol)
       if instr is null: continue
    4. ticker = await db.BookTickers.AsNoTracking()
                   .FirstOrDefaultAsync(b => b.Symbol.Value == notification.Symbol)
       if ticker is null: continue
       entry = (direction == Long ? ticker.AskPrice : ticker.BidPrice)
    5. stopDistance = notification.SuggestedStopPrice.HasValue
                       ? abs(entry - notification.SuggestedStopPrice.Value)
                       : 0m  // service notional-cap fallback'e duser
    6. sizing = sizingService.Calculate(new PositionSizingInput(
                   equity, entry, stopDistance,
                   risk.RiskPerTradePct, risk.MaxPositionSizePct,
                   instr.MinNotional, instr.StepSize, instr.MinQty,
                   GetSlippageForMode(mode)))
       if sizing.Quantity == 0:
           emit SystemEvent("trade.sizing_skipped", warning, payload={reason, mode, symbol})
           continue
    7. cmd = new PlaceOrderCommand(cid, symbol, side, Market, IOC,
                  sizing.Quantity, null, null, strategyId, mode)
    8. mediator.Send(cmd, ct)
```

**Slippage table:**
- `Paper` → `0.0005m` (5 bps; configurable via `PaperFillOptions.FixedSlippagePct`)
- `LiveTestnet` → `0m` (binance fills gercek)
- `LiveMainnet` → `0m` (zaten blocked)

### 11.4 Equity Kaynagi (Mode-Aware)

**Karar:** Yeni Application servisi `IEquitySnapshotProvider` (`src/Application/Abstractions/Trading/IEquitySnapshotProvider.cs`):

```
Task<decimal> GetEquityAsync(TradingMode mode, CancellationToken ct);
```

Impl strategy:
- **Paper:** `_db.VirtualBalances.AsNoTracking().FirstOrDefaultAsync(b => b.Id == 1)` → `balance.Equity` (mevcut alan, ADR-0008 §8.4). Boş ise `balance.CurrentBalance` fallback. Hicbiri yoksa `0m`.
- **LiveTestnet:** `IBinanceCredentialsProvider.HasTestnetCredentials()` false → `0m` (handler skip yapar). True → `IBinanceAccount.GetEquityAsync(ct)` (yeni metod, USDT cash + acik pozisyon mark price). Loop 3'te credentials yok → her zaman 0 → handler `if (equity <= 0) skip`.
- **LiveMainnet:** her zaman `0m` (zaten ADR-0006 blocked).

### 11.5 Exit Handling: ClosePositionCommand

**Karar:** Yeni CQRS command `ClosePositionCommand` — PM onerisi kabul edilir.

**Konum:** `src/Application/Positions/Commands/ClosePosition/ClosePositionCommand.cs`

```
record ClosePositionCommand(
    string Symbol,
    long? StrategyId,
    TradingMode Mode,
    string Reason,
    string CorrelationCidPrefix) : IRequest<Result<ClosedPositionDto>>;

record ClosedPositionDto(long PositionId, decimal RealizedPnl, string Reason);
```

**Handler akisi:**
1. `position = await db.Positions.FirstOrDefaultAsync(p => p.Symbol.Value == request.Symbol && p.Mode == request.Mode && p.Status == Open)`. Yoksa `Result.NotFound`.
2. `bookTicker` oku → `markPrice = side == Long ? bookTicker.BidPrice : bookTicker.AskPrice` (cikista counter-side).
3. `cid = $"{request.CorrelationCidPrefix}-x-{Mode.ToCidSuffix()}"` ("x" = exit suffix).
4. `closeOrder = new PlaceOrderCommand(cid, symbol, reverseSide, Market, IOC, position.Quantity, null, null, strategyId, mode)`.
5. `mediator.Send(closeOrder, ct)` — paper/testnet branch'lerine duser, `OrderFilledEvent` raise olur.
6. `OrderFilledEvent` handler (yeni: `OrderFilledPositionUpdater`, §11.6) ilgili pozisyonun `position.Close(exitPrice, reason, now)` cagirir → `PositionClosedEvent` → mevcut `PositionClosedRiskHandler` zaten calisiyor.
7. Result: `Result.Success(new ClosedPositionDto(position.Id, computedPnl, reason))`.

**Reddedilen Alternatif:** `PlaceOrderCommand`'a `IsClose: bool` flag eklemek. Reddedildi — command tek-aggregate-yazma anlamini kaybeder, validator da PNL hesabini bilmez; close orchestration ayri bir use-case.

**Cagri yeri:** `StrategySignalToOrderHandler` — `if (notification.Direction == Exit) → mediator.Send(new ClosePositionCommand(...))` her 3 mode icin (mevcut fan-out loop icinde). LiveMainnet skip; LiveTestnet credentials yoksa skip; Paper her zaman.

### 11.6 OrderFilledPositionUpdater (Yeni Domain Event Handler)

**Sorun:** Mevcut akista `OrderFilledEvent` raise oluyor (Order.RegisterFill icinde), ama bunu dinleyip `Position` aggregate'i guncelleyen handler **yok**. `Position.Open(...)` sadece `PaperFillSimulator` veya live fill akisi tarafindan cagrilmali; `Position.AddFill` / `Position.Close` ise reverse fill akisinda. Bu glue yok.

**Karar:** Yeni Infrastructure handler `src/Infrastructure/Positions/OrderFilledPositionUpdater.cs` — `INotificationHandler<OrderFilledEvent>`.

Akis:
1. `var openPosition = await db.Positions.FirstOrDefaultAsync(p => p.Symbol == order.Symbol && p.Mode == order.Mode && p.Status == Open, ct)`.
2. **Acik pozisyon yok + order Buy → entry:** `Position.Open(symbol, Long, qty, fillPrice, strategyId, mode, now)`; `db.Positions.Add(position)`.
3. **Acik pozisyon var + order ayni side → ekleme:** `position.AddFill(qty, fillPrice, now)`.
4. **Acik pozisyon var + order ters side → kapanma:** Eger `order.Quantity == position.Quantity` → `position.Close(fillPrice, reason: $"order_{order.ClientOrderId}", now)`. Eger `order.Quantity > position.Quantity` → `position.Close(...)` + ek `Position.Open(reverse-side)` ile yeni pozisyon ac (long flip).
5. `await db.SaveChangesAsync(ct)` — `PositionOpenedEvent`/`PositionClosedEvent` zaten domain event olarak yayilir, mevcut `PositionClosedRiskHandler` (ADR-0008 §8.6) tetiklenir → `RecordTradeOutcomeCommand` → `RiskProfile.RecordTradeOutcome` → CB check.

**Gerekce:** `PositionClosedEvent` zaten mevcut akiş için bekliyor; sadece `Position` aggregate'inin Open/Close olmasini saglayan glue eksik. Bu handler eklenince §11.7 RiskProfile guncelleme akisi otomatik aktiflesir — mevcut `PositionClosedRiskHandler` calisir, yeni kod gerekmez.

### 11.7 RiskProfile Guncelleme — Mevcut Handler Korunur

**Karar:** Yeni `IRiskTrackingService` **eklenmez**. Mevcut `PositionClosedRiskHandler` (`src/Infrastructure/Positions/PositionClosedRiskHandler.cs`) zaten:
- `PositionClosedEvent` dinler.
- Mode bazinda toplam realized + unrealized hesaplar (equity).
- `RecordTradeOutcomeCommand.Handle` cagirir.
- `RiskProfile.RecordTradeOutcome` → consecutive losses, drawdown, peak equity guncellenir.
- Handler icindeki `if tripped → TripCircuitBreaker` zaten var (`RecordTradeOutcomeCommandHandler:36-53`).

Tek eksik §11.6'daki `OrderFilledPositionUpdater` glue idi. O eklenince zincir tamamlanir. **Bu ADR'in en kucuk surprise'ı:** mevcut risk-tracking kodunun dogru, sadece "fed" edilmesi gerek.

### 11.8 Risk Parametre Default'lari — Boot Seed

**Karar:** PM onerisi kabul edilir — `appsettings.json` `RiskProfile.Defaults` section. Boot'ta `RiskProfileSeeder` (yeni hosted service ya da `IDataSeeder` impl, mevcut `StrategyConfigSeeder` patternine paralel) 3 mode icin upsert yapar.

**`appsettings.json` ornegi:**
```json
"RiskProfile": {
  "Defaults": {
    "RiskPerTradePct": 0.01,
    "MaxPositionSizePct": 0.15,
    "MaxDrawdown24hPct": 0.05,
    "MaxDrawdownAllTimePct": 0.25,
    "MaxConsecutiveLosses": 3
  }
}
```

**Algoritma (`RiskProfileSeeder`) — config-as-source-of-truth:**
```
foreach mode in [Paper, LiveTestnet, LiveMainnet]:
    var existing = db.RiskProfiles.FirstOrDefault(r => r.Id == RiskProfile.IdFor(mode));
    if (existing is null):
        var profile = RiskProfile.CreateDefault(mode, _clock.UtcNow);
        profile.UpdateLimits(opts.RiskPerTradePct, opts.MaxPositionSizePct,
                             opts.MaxDrawdown24hPct, opts.MaxDrawdownAllTimePct,
                             opts.MaxConsecutiveLosses, _clock.UtcNow);
        db.RiskProfiles.Add(profile);
    else if (existing.diverges_from(opts)):
        existing.UpdateLimits(opts.RiskPerTradePct, opts.MaxPositionSizePct,
                              opts.MaxDrawdown24hPct, opts.MaxDrawdownAllTimePct,
                              opts.MaxConsecutiveLosses, _clock.UtcNow);
db.SaveChanges();
```

**Idempotent reconciler — config-as-source-of-truth.** appsettings.json `RiskProfile.Defaults` değişirse boot'ta DB ile reconcile edilir. UI üzerinden `UpdateRiskProfileCommand` ile yapılan değişiklikler **runtime experiment** sayılır; kalıcı kılınmak için appsettings.json'a yazılması gerekir. Bu pattern operations best-practice (12-factor app config) ve loop'lar arası deterministik baseline garanti eder.

**Operasyonel sonuç:** Restart sonrası UI ayarları reset olur. Audit trail için her UpdateRiskProfileCommand SystemEvent yazar (zaten mevcut akış); operatör hangi değerin kaynağa ait olduğunu loga bakarak görebilir.

**Reddedilen alternatif:** `RiskProfile.CreateDefault` icindeki sabitleri (`0.01, 0.10, 0.05, 0.25, 3`) `appsettings.json`'a tasimak ve `Domain.CreateDefault`'i parametre alır yapmak. Reddedildi — Domain layer config bilmez (dependency rule). `CreateDefault` factory'si "domain'e gore mantikli minimum default" doner; seeder Application/Infrastructure'da config okur ve `UpdateLimits` ile override eder.

**MaxPositionSizePct: 0.10 → 0.15 degisikligi.** Research-sizing.md %15 onermistir. `RiskProfile.UpdateLimits` validation `<= 0.20` izin verir, problem yok. Domain factory default'u `0.10`'da kalir (conservative); appsettings.json `0.15` override eder.

### 11.9 Slippage Modeli — Paper Only

**Karar:** `PaperFillOptions` (yeni record, `src/Infrastructure/Trading/Paper/PaperFillOptions.cs`):

```
public sealed record PaperFillOptions
{
    public decimal FixedSlippagePct { get; init; } = 0.0005m;  // 5 bps
}
```

`PaperFillSimulator.FillMarket` icinde her fill level icin:
```
adjustedPrice = side == Buy ? lvl.Price * (1 + opts.FixedSlippagePct) : lvl.Price * (1 - opts.FixedSlippagePct);
```

ve `Order.RegisterFill` `adjustedPrice` ile cagirilir. Commission hesabi `adjustedPrice * quantity * TakerFeeRate` zaten dogru (mevcut `ComputeCommission`).

**`appsettings.json`:**
```json
"PaperFill": {
  "FixedSlippagePct": 0.0005
}
```

DI: `services.Configure<PaperFillOptions>(config.GetSection("PaperFill"))`; `PaperFillSimulator` ctor'a `IOptions<PaperFillOptions>` inject.

### 11.10 BUG-A Fix — PaperFillSimulator MARKET MinNotional

**Karar:** `PaperFillSimulator.ValidateFilters` icinde MARKET branch'i icin minNotional kontrolu **yapilamaz** cunku `order.Price` MARKET'te null. Cozum: kontrolu `FillMarket` icine `BuildLevels` sonrasi tasi:

```
// FillMarket basinda, BuildLevels'tan sonra:
var topPrice = levels[0].Price;
var notionalEstimate = order.Quantity * topPrice;
if (notionalEstimate < instrument.MinNotional)
{
    order.Reject($"filter_MIN_NOTIONAL_{notionalEstimate}<{instrument.MinNotional}", now);
    return new PaperFillOutcome(false, true,
        $"min_notional_{notionalEstimate}<{instrument.MinNotional}", 0m, 0m, 0m);
}
```

`ValidateFilters` icinde MARKET icin de notional kontrol etmek istersek `bookTicker.AskPrice` parametre olarak gecirilmesi gerek — daha temizi `FillMarket` icinde estimate. Reviewer paper-vs-live divergence regression test ister: `PaperFillSimulator_Tests.MarketBelowMinNotional_Rejects`.

### 11.11 BUG-B Fix — StrategySignalToOrderHandler Hardcode 0.001

**Karar:** §11.3'te ortadan kalkar. Hardcode `0.001m` `sizing.Quantity` ile degistirilir; `0` durumunda `continue`.

### 11.12 Stop-Loss Server-Side — Loop 3 Disinda

**Karar:** [ADR-0005 §5.3](./0005-risk-limit-policy.md) "her trade'le paired `STOP_LOSS_LIMIT`" Spot OCO ile yapilacak; bu **ADR-0012'ye reserve edilir** (Loop 4+ scope). Loop 3'te:

- Evaluator `SuggestedStopPrice` doner (mevcut).
- `PositionSizingService` stopDistance'i sizing icin kullanir.
- **Server-side stop emri konulmaz** — sadece soft-exit (Direction.Exit ile evaluator pozisyonu kapatir).
- `ADR-0005` ile gerilim: kabul edilir, gecici. Loop 4 ADR-0012'de OCO entegre edilince §5.3 fully honored olacak.

`PositionClosedEvent.Reason` icine "soft_exit_signal" yazılır → reviewer + tester gormeli.

## Consequences

### Pozitif
- Sizing artik equity-aware: $100 portfoyde 1% risk + 15% notional cap → BTC `qty ≈ 0.0002`, BNB `qty ≈ 0.023`, XRP `qty ≈ 30` — minNotional 5 USDT'yi her sembolde aşar.
- BUG-A ve BUG-B kapatilir; paper-vs-mainnet divergence azalir.
- Exit signali pozisyonlari kapatir → `Position.Close()` raise → `PositionClosedRiskHandler` → `RiskProfile.RecordTradeOutcome` → CB calismaya baslar.
- Slippage modeli ile paper fill'leri gercege yakinlasir; backtest overfitting biraz azalir.
- `RiskProfileSeeder` boot'ta deterministik baslangic sağlar; PM iterasyon reset'lerinde `IterationId` degisir ama RiskProfile preserve edilir (audit kontinuiti).
- Yeni domain davranisi yok — Domain saf kalir; tum karmasiklik Application/Infrastructure katmanlarinda; Clean Architecture dependency rule ihlali yok.

### Negatif / Tradeoff
- `IStrategyEvaluator.StrategyEvaluation.SuggestedQuantity` field'i **deprecated ama kaldirilmadi** — kafa karistirici. Loop 4'te field record'tan silinir; o zaman 2 evaluator + tum testler updated.
- `OrderFilledPositionUpdater` yeni handler — fill akisinda 1 ek DB roundtrip (positions lookup + save). Performans: dakikada birkac fill; goz ardi edilebilir.
- Server-side stop hala yok (Loop 4 ADR-0012). Bu sure boyunca WS koparsa stop calismaz; ADR-0005 §5.3 kismi ihlal — riski operasyonel olarak kabul ediyoruz cunku Paper + LiveTestnet (no creds) modlarinda gercek para riski yok, LiveMainnet bloklu.
- `PaperFillOptions` config eklenir — `appsettings.json` template'i ve DI buyur; test'lerde `Options.Create(new PaperFillOptions())` boilerplate.
- `IEquitySnapshotProvider` LiveTestnet branch'i Loop 3'te 0 doner (credentials yok); §11.4 algoritmasi `if (equity <= 0) skip` yapar — LiveTestnet fan-out'u sizing tarafindan bile baslamadan skip edilir. Bu **LiveTestnet stub'inin (ADR-0008 §8.8) iki katmanli skip'i** yaratir; gozlemlenebilir log farki: `"sizing_skipped equity=0"` vs `"no_credentials_testnet"` — backend-dev + reviewer log korelasyon kurali eklemeli (correlationId tek).

### Notr
- Domain event sayisi artmaz; mevcut `OrderFilledEvent`, `PositionOpenedEvent`, `PositionClosedEvent` yeniden kullanilir.
- DB schema degisikligi yok — migration gerekmez. (Sadece `appsettings.json` + DI + handler kodu.)
- `RiskProfile.RecordTradeOutcome` icindeki `equity` parametresi `PositionClosedRiskHandler` tarafindan zaten `realized + unrealized` olarak hesaplaniyor; bu **VirtualBalance.Equity** ile farkli bir gosterimde — Loop 4'te `EquitySource` enum (`Positions` / `VirtualBalance`) ile uyumlanabilir; simdilik tutarli.

## Alternatifler

### Alt-1: Sizing Evaluator Icinde
PM onerisi reddetti: clean separation kayboluyor, evaluator strategi mantigi + sizing'i karistiriyordu. Reddedildi.

### Alt-2: Sizing EmitStrategySignalCommand Handler'inda
`StrategySignal` aggregate'in icine sizing alani ekle. Reddedildi — `StrategySignal` ham sinyal, sizing fan-out karari mode-bazli; her mode icin ayri equity/risk → 3 farkli qty olabilir; sinyalin tek bir `qty`'si olmasi yanlis bilgi.

### Alt-3: PlaceOrderCommand'a IsClose Flag
§11.5'te reddedildi.

### Alt-4: Yeni IRiskTrackingService
§11.7'de reddedildi — mevcut `PositionClosedRiskHandler` zaten servis gibi davraniyor; ek soyutlama YAGNI.

### Alt-5: Yeni Position Aggregate'i Yok, Order'lardan Derive Et
Position aggregate zaten var ve EF tablo mevcut (`ADR-0008 §8.5`). Order'lardan derive `AsNoTracking` query ile her zaman mumkun ama 100+ order'da O(N) sum — aggregate cache faydali. Reddedildi (mevcut tabloyu kullaniyoruz).

### Alt-6: Server-Side STOP_LOSS_LIMIT Bu Loop'ta
ADR-0005 §5.3 hemen tam karşilamak. Reddedildi — Spot OCO ayri bir tasarim semasi (entry order fill bekle → stop place → cancel-on-cancel pairing); kendi ADR'ı (ADR-0012) hak ediyor; Loop 4'e otelendi.

## Cakisma Kontrolu (ADR-0005 / ADR-0006 / ADR-0008 / ADR-0009 / ADR-0010)

- **ADR-0005 §5.1 RiskPerTradePct max %2:** Bu ADR `0.01` default tutuyor (max icinde) — uyumlu.
- **ADR-0005 §5.2 MaxPositionSizePct default %10:** Bu ADR `0.15`'e cikariyor (UpdateLimits cap `0.20` izin veriyor) — uyumlu, audit log'da gerekce: "100 USDT portfoyde 5 USDT minNotional altina dusmemek icin minimum 15%".
- **ADR-0005 §5.3 server-side stop:** Loop 3 disinda, Loop 4 ADR-0012'ye otelendi. ADR-0011 §11.12'de net.
- **ADR-0006 testnet-first:** Mainnet branch'i sizing oncesi skip; uyumlu.
- **ADR-0008 §8.3 fan-out per mode:** Sizing mode-bazinda hesaplanir; her mode kendi `RiskProfile`/equity'sine bakar; uyumlu.
- **ADR-0008 §8.6 mode-scoped RiskProfile:** Bu ADR aynen kullaniyor (`RiskProfile.IdFor(mode)`); uyumlu.
- **ADR-0009 backfill:** Sizing/exit akislari KlineBackfillWorker'dan bagimsiz; uyumlu.
- **ADR-0010 backfill event suppression:** Backfill `KlineClosedEvent` susturuyor → `StrategyEvaluationHandler` cagrilmiyor → fan-out tetiklenmiyor → sizing/exit cagrilmiyor. Tam uyumlu.

## Kaynak

- [ADR-0003 Idempotent Handler Discipline](./0003-idempotent-handler-discipline.md)
- [ADR-0005 Risk Limit Policy](./0005-risk-limit-policy.md)
- [ADR-0006 Testnet-First Policy](./0006-testnet-first-policy.md)
- [ADR-0008 Trading Modes Fan-Out](./0008-trading-modes.md)
- [ADR-0009 REST Kline Backfill On Boot](./0009-rest-kline-backfill-on-boot.md)
- [ADR-0010 Backfill Event Suppression](./0010-backfill-event-suppression.md)
- [loop_3/research-sizing.md](../../loops/loop_3/research-sizing.md) — binance-expert min notional + fee + slippage
- [loop_3/decision-sizing.md](../../loops/loop_3/decision-sizing.md) — operasyonel detay + backend-dev sablonu
- [loop_3/plan.md §P0 #3+#4+#7+#8](../../loops/loop_3/plan.md)
- [Eric Evans — Domain-Driven Design ch. 5 Services](https://www.dddcommunity.org/learning-ddd/what_is_ddd/)
- [Microsoft Learn — DDD-CQRS patterns: Application services](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/)
- [jasontaylordev/CleanArchitecture — handler-per-action](https://github.com/jasontaylordev/CleanArchitecture)
- [ardalis/CleanArchitecture — domain event handler pattern](https://github.com/ardalis/CleanArchitecture)
- [Binance Spot Filters](https://binance-docs.github.io/apidocs/spot/en/#filters)
