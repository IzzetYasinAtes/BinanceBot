# 0017. TimeStop Mapping + Duplicate Signal Protection + Sizing Semantik Düzeltmesi

Date: 2026-04-19
Status: Accepted
Relates to: [ADR-0014 Pattern-Based Scalping Reform](./0014-pattern-based-scalping-reform.md), [ADR-0015 VWAP-EMA Hybrid Strategy](./0015-vwap-ema-hybrid-strategy.md), [ADR-0016 VWAP-EMA Hybrid V2 Tuned](./0016-vwap-ema-hybrid-v2-tuned.md), [ADR-0011 Equity-Aware Sizing](./0011-equity-aware-sizing-and-risk-tracking.md), [ADR-0012 Trade Frequency Realism Reform](./0012-trade-frequency-realism-reform.md)

> Bu ADR üç eksende karar verir: (A) `ContextJson → Position.MaxHoldDuration` map yolunu **`maxHoldBars` OR `maxHoldMinutes`** olacak şekilde genişletir — `TimeStopMonitor` tetiklenemeyen pozisyonları bitirir. (B) Application katmanına `(StrategyId, Symbol)` bazlı **duplicate signal protection** getirir — aynı sembolde çift poz. açılmasını engeller. (C) `StrategySignalToOrderHandler` **sizing semantiğini** değiştirir: `MaxPositionPct` artık hard-cap değil, `targetNotional = max(equity × 0.20, 20)` kuralı `qtyByCap`'i override eder. Parametre tuning ADR-0016'dan korunur; burada sadece aggregate sınırları ve handler davranışı değişir.

## Context

### 17.1 Loop 21 Halt Kanıtı — 3 Backend Bug

`loops/loop_21/summary.md` (t~115 halt) + `loops/loop_20/user-feedback-ui.md` (t60 feedback):

| Gözlem | Sayısal Kanıt | Beklenen | Root Cause |
|---|---|---|---|
| BNB pozisyonu 31.5 dk açık | `MaxHoldMinutes=10` seed'te | TimeStop 10 dk'da close | `Position.MaxHoldDurationSeconds = NULL` — `ContextJson → Position` map'i kırık |
| BNB sembolü 3 dk arayla çift emit → $78 notional | $20 hedef | Aynı (strategyId, symbol) açık pos varsa skip | Application/Handler'da duplicate protection yok |
| `cash = −$18.75`, equity ≈ $100 → 2 poz × ~$40 = $80 kullanıldı | Beklenen $20 + $20 = $40 | $20/poz | `qtyByCap = equity × MaxPositionPct = 100 × 0.40 = 40` → notional cap %40'ta, min-notional alt sınırı değil hedef floor değil |

### 17.2 Bug-1 Root Cause — Anahtar Uyumsuzluğu

`src/Infrastructure/Strategies/Evaluators/VwapEmaStrategyEvaluator.cs:143-157`:

```csharp
var ctx = EvaluatorParameterHelper.SerializeContext(new
{
    type = "vwap-ema-hybrid-v2",
    ...,
    maxHoldMinutes = p.MaxHoldMinutes,   // <-- evaluator "maxHoldMinutes" yazıyor
});
```

`src/Infrastructure/Orders/OrderFilledPositionHandler.cs:90-99`:

```csharp
using var doc = JsonDocument.Parse(lastSignal.ContextJson);
if (doc.RootElement.TryGetProperty("maxHoldBars", out var mhb)      // <-- "maxHoldBars" arıyor
    && mhb.ValueKind == JsonValueKind.Number
    && mhb.TryGetInt32(out var bars)
    && bars > 0)
{
    maxHoldDuration = TimeSpan.FromMinutes(bars);
}
```

`TryGetProperty("maxHoldBars", ...) == false` → `maxHoldDuration = null` → `Position.Open(..., maxHoldDuration: null)` → DB'de `MaxHoldDurationSeconds = NULL` → `StopLossMonitorService` time-stop branch'i (ADR-0014 §14.5) hiç tetiklenmez.

**ADR-0014 pattern stratejisi** `ContextJson.maxHoldBars` yazıyordu; **ADR-0016 VWAP-EMA V2** `maxHoldMinutes` yazıyor. Handler ikisini de tanıyacak şekilde genişletilmedi. Bu map regression'ı pattern → VWAP-EMA geçişinde yakalanmamış.

### 17.3 Bug-2 Root Cause — Duplicate Protection Yok

`src/Infrastructure/Strategies/StrategySignalToOrderHandler.cs:128-137`:

```csharp
var openCount = await db.Positions.AsNoTracking()
    .CountAsync(p => p.Status == PositionStatus.Open && p.Mode == mode, ct);
if (openCount >= risk.MaxOpenPositions)     // <-- sadece GLOBAL count
{
    _logger.LogInformation("Max open positions reached ...");
    continue;
}
```

`MaxOpenPositions = 2` mod-başına **toplam** sınır; sembol-bazlı değil. Senaryo:
- t=0: BNB signal → order fill → open pos #1 (BNB)
- t=2m: BNB yine signal (VWAP zone + volume konfirme) → open pos #2 (BNB). Toplam 2, global sınır aşılmadı → kabul.
- Sonuç: BNB'de çift poz, ortalama entry kaydırılıyor (`Position.AddFill` same-side → avg price update), notional 2× beklenen.

`OrderFilledPositionHandler:65-68` same-side fill'i `AddFill` olarak kabul ediyor (domain invariant: tek açık poz per symbol+mode), bu yüzden **aggregate DB'de iki row değil tek row $78 olarak birikir**. Ama semantik olarak bir sinyale bir trade kuralı bozulur; sizing öngörüsü de.

### 17.4 Bug-3 Root Cause — Sizing Semantik Karmaşası

`src/Application/Sizing/PositionSizingService.cs:39-44`:

```csharp
var notionalCap = i.Equity * i.MaxPositionPct;                   // 100 × 0.40 = 40
var qtyByCap = effectiveEntry > 0m ? notionalCap / effectiveEntry : 0m;
var qtyRaw = Math.Min(qtyByRisk, qtyByCap);
```

`StrategySignalToOrderHandler.cs:159-161`:

```csharp
var userMinNotional = SnowballSizing.CalcMinNotional(equity);           // 100 × 0.20 = 20
var effectiveMinNotional = Math.Max(userMinNotional, instrument.MinNotional);
```

`PositionSizingService`'e geçen `MinNotional = 20` **alt sınır** (satır 60: `if (notional < i.MinNotional) → skip`). Cap ise `equity × 0.40 = $40`. stopPct 0.3% → stopDistance küçük → `qtyByRisk` büyük → kesici olmaz. Sonuç: her trade $40 notional (cap). Kullanıcı beklentisi $20.

`appsettings.json:54-62`:

```json
"RiskProfile": { "Defaults": {
    "RiskPerTradePct": 0.02,
    "MaxPositionSizePct": 0.40,
    ...
}}
```

Loop 14 reform'u `MaxPositionSizePct` 0.15 → 0.40'a çıkarmıştı (günde 22 trade × $15 notional matematik saçma argümanı — `loops/loop_14/research-paper-live-and-sizing.md`). ADR-0011 sizing kuralı **üç kısıt**: (riskPct, maxPositionPct, minNotional). Şu an `minNotional` user floor **değil** target; `maxPositionPct` ise her tradeyi %40'a itiyor. Kullanıcı Loop 21'de `max(equity × 20%, $20)` single rule istedi — iki parametreli karmaşa yerine tek hedef notional.

### 17.5 Kullanıcı Kararları (Loop 21 t60 + boot-brief)

> "nasıl − olabiliyor − olmaması gerek" (cash −$18.75 screenshot)
> "Parametreler korun (Loop 21 kanıtlıyor), risk kontrolü + monitor + UI düzelt"
> "max(equity × 0.20, $20) tek kural — hem min hem target aynı"

Parametre layer (ADR-0016 tuned) **dokunulmaz**. Risk + sizing + monitor wiring layer düzeltilir.

### 17.6 Mevcut Altyapı Envanteri

Kontrol edildi — eksik parça bırakılmış:

| Kaynak | Durum |
|---|---|
| `ISystemEventPublisher` (Application) | **Var** (ADR-0016 §16.9.3) |
| `SystemEventType.SignalSkipped = 41` | **Var** (ADR-0016 §16.9.2) |
| `StrategySignalSkippedEvent` + handler | **Var** (`src/Infrastructure/SystemEvents/Handlers/StrategyHandlers.cs:83`) |
| `Position.Open(..., maxHoldDuration)` overload | **Var** (ADR-0014 §14.5, `Position.cs:54-107`) |
| `Positions.MaxHoldDurationSeconds` kolonu | **Var** (migration mevcut, değer NULL geliyor — map bug'ı) |
| `IApplicationDbContext.Positions` read path | **Var** |
| Aggregate-per-repo disiplini | **Var** (repository-per-entity yasak, `src/CLAUDE.md`) |

**Sonuç:** Yeni aggregate / yeni abstraction yok. Sadece (a) evaluator veya handler'da key genişletme, (b) handler'da duplicate-check sorgusu + publish, (c) sizing formül değişikliği.

## Decision

### 17.7 Bug-1 — TimeStop ContextJson Map'i `maxHoldMinutes` OR `maxHoldBars` Okur

**Karar:** `OrderFilledPositionHandler`'ın ContextJson okuma blok'u **her iki anahtarı** tanır. ADR-0014 pattern stratejisi (gelecekte) ve ADR-0016 VWAP-EMA V2 tek handler ile uyumlu.

Öncelik: `maxHoldMinutes` varsa önce o okunur (VWAP-EMA path), yoksa `maxHoldBars` (pattern path). İki alan da sayısal dakika olarak yorumlanır (BinanceBot 1m bar, 1 bar = 1 dakika — ADR-0014 §14.5 convention).

**Değişiklik yeri (tek dosya):** `src/Infrastructure/Orders/OrderFilledPositionHandler.cs:90-104`.

**Domain invariant korunur:** `Position.Open` `maxHoldDuration` parametresi zaten vardı (ADR-0014 §14.5); invariant `maxHoldDuration <= TimeSpan.Zero → DomainException`. Handler `TimeSpan.FromMinutes(...)` çağrısı öncesi `minutes > 0` gate'i tutar.

**Alternatif reddedildi (Domain invariant zorunlu):** `maxHoldSeconds` parametresini `Position.Open` imzasına **zorunlu** yapmak aggregate sınırı açısından doğru görünür ama `OrderFilledPositionHandler`'in eski `StrategySignal.ContextJson` içermeyen veri (örn. manuel admin trade, backfill) ile çağrıldığı yollarda break getirir. Opsiyonel parametre korunur, handler tarafı null-safe kalır.

### 17.8 Bug-2 — Duplicate Signal Protection, Application Seviyesi

**Karar:** `StrategySignalToOrderHandler`'ın `foreach (var mode in AllModes)` döngüsüne girmeden **pre-check** yapılır: aynı `(StrategyId, Symbol)` için açık bir `Position` varsa signal order pipeline'a girmez, `StrategySignalSkippedEvent` publish edilir. Global `MaxOpenPositions` kontrolü korunur (farklı guard — global vs per-symbol).

**Yerleşim gerekçesi:** Cross-aggregate invariant (`Position` + `Strategy`) tek bir aggregate'a koyulamaz (aggregate-per-transaction kuralı — `loops/loop_3/decision.md`, DDD Evans). Application orchestrator doğru katman. Domain method olarak `Strategy.EmitSignal` içine koymak tasarım olarak güzel ama `Strategy` aggregate `Position` state'ini okumadığı için erişim yok; MediatR handler seviyesinde repository sorgusu ile çözülür — **`src/CLAUDE.md` "anemic model yasak"** kuralı bozulmaz: `Position`'un iş kuralları (açılış/kapanış) aggregate'ta; "aynı stratejide açık poz var mı?" sorgusu ise state **okuma** (read concern), aggregate invariant değil.

**Sorgu:** 
```csharp
var hasOpen = await db.Positions.AsNoTracking().AnyAsync(p =>
    p.StrategyId == notification.StrategyId &&
    p.Symbol == symbolVo &&
    p.Status == PositionStatus.Open, ct);
```

Fan-out döngüsünden **önce** — tüm mode'lar için aynı strateji+sembol pozisyon sahipliği kontrol edilir. Mode granüler ayırım gerekiyorsa gelecekte eklenir (şu an paper + testnet + mainnet aynı mantıksal strateji; ADR-0008 mode-başına ayrı pos aggregate dolduruyor ama `StrategyId` hepsinde aynı, sembol de aynı — duplicate protection global).

Mode-per-mode ayrımı **Nötr** alternatif: mode-başına farklı açık poz olabiliyor ADR-0008'de. O yüzden pre-check döngü İÇİNDE yapılır, her mode için bağımsız:

```csharp
foreach (var mode in AllModes)
{
    var hasOpen = await db.Positions.AsNoTracking().AnyAsync(p =>
        p.StrategyId == notification.StrategyId &&
        p.Symbol == symbolVo &&
        p.Status == PositionStatus.Open &&
        p.Mode == mode, ct);
    if (hasOpen)
    {
        // publish skip event + continue
    }
    // ... existing sizing + order code
}
```

**Bu yol TERCİH EDİLİR** (mode izolasyonu ADR-0008 ilkesiyle tutarlı).

**Skip olayı:** `StrategySignalSkippedEvent` (mevcut) publish edilir. Payload: `StrategyId, Symbol, Reason="duplicate_open_position", Mode`. Infrastructure `StrategySignalSkippedSystemEventHandler` (mevcut) `SystemEvents` tablosuna `SignalSkipped (41)` row ekler. UI `logs.html` Sistem Olayları chip'inde görünür.

**Sinyal kaydı:** `EmitStrategySignalCommand` handler'ı **olduğu gibi kalır** — signal DB'ye yazılır, `StrategySignalEmittedEvent` raise olur. Skip fan-out'a (`StrategySignalToOrderHandler`) girildikten sonra uygulanır; signal kaydı yine görünür (audit trail), ama order oluşmaz. Böylece:
- `StrategySignals` tablosu: signal emit'ini tam olarak yazar (analiz/backtest için kayıt tam)
- `SystemEvents` tablosu: `SignalEmitted` + `SignalSkipped` iki satır (UI görünürlük)
- `Orders` tablosu: order yok (duplicate önlendi)
- `Positions` tablosu: yeni row veya AddFill yok

### 17.9 Bug-3 — Sizing Semantik Reform: Tek Hedef Notional

**Karar:** `StrategySignalToOrderHandler` sizing çağrısı **önce hedef notional** hesaplanır, sonra `PositionSizingService` çağrılır. `MaxPositionPct` riskProfile field'ı **üst güvenlik sınırı** olarak kalır (sanity guard — hedef notional bunu aşarsa clamp), ama birincil belirleyici değil.

**Formül (tek kural, kullanıcı kararı):**

```
targetNotional = max(equity × 0.20, 20.0)     // hem floor hem hedef
hardCap        = equity × MaxPositionPct       // güvenlik üst sınır (default 0.40)
notional       = Math.Min(targetNotional, hardCap)  // hedef < cap → hedef, hedef > cap → cap
qty            = Math.Floor((notional / effectiveEntry) / stepSize) * stepSize
```

Equity durumları:
| Equity | targetNotional | hardCap (0.40) | notional | Yorum |
|---|---|---|---|---|
| $50  | max(10, 20) = 20 | 20 | 20 | Min floor devrede |
| $100 | max(20, 20) = 20 | 40 | 20 | **Kullanıcı hedefi — Loop 22** |
| $200 | max(40, 20) = 40 | 80 | 40 | %20 oransal büyüme |
| $500 | max(100, 20) = 100 | 200 | 100 | Kartopu |
| $10000 | max(2000, 20) = 2000 | 4000 | 2000 | Cap devrede değil |

`PositionSizingService.PositionSizingInput` kontratı değişmez — caller `MinNotional` ve `MaxPositionPct` değerlerini yeni semantiklere göre doldurur:

- `MinNotional = targetNotional` (tam hedef floor — exchange `instrument.MinNotional` de bu kadardan küçük kalır genellikle; değilse `Math.Max(targetNotional, instrument.MinNotional)`).
- `MaxPositionPct = notional / equity` (hedef notional'ın equity'ye oranı — cap branch hedef notional'ı üretir).

**Alternatif (daha temiz):** `PositionSizingInput`'a `TargetNotional` alanı eklenir, cap/floor branch'leri içeride yönetilir. Service değişir. Bu yol daha doğru ama Application kontratı değiştirir → reviewer + testler genişler. **Loop 22 için handler-side override** seçildi (iki satır değişikliği, service imzası aynı). Cleanup **Loop 23 backlog** — `IPositionSizingService` kontratına `TargetNotional` alanı eklenip `MinNotional` ve `MaxPositionPct` semantikleri netleştirilir.

**Exchange `instrument.MinNotional` entegrasyonu:** `targetNotional < instrument.MinNotional` ise trade reject (log + skip + `SignalSkipped` reason=`exchange_min_notional`). Testnet BNB/BTC/XRP şu an MIN_NOTIONAL ≈ $5 — $20 floor her durumda üstte kalır, aktif kısıt olmaz. Ama code path korunur (future sembol eklemesi için).

**`MaxPositionSizePct` config'te 0.40 olarak kalır** (ADR-0011+0014 kararlarını bozmamak için — güvenlik valfi olarak görevli). Kullanıcı hedefi `targetNotional` ile sağlanır, cap yalnızca anomaly durumunda ($20 × 3 kartopu sonrası equity $100'e düşerse yine cap 40, hedef 20 → hedef seçilir) güvencedir.

### 17.10 Domain Aggregate Sınırı — Değişiklik Yok

| Aggregate / Type | Katman | ADR-0017 Etki |
|---|---|---|
| `Position` aggregate | Domain | **Değişiklik yok** — `Open(..., maxHoldDuration: ...)` overload mevcut (ADR-0014). Invariant `maxHoldDuration > TimeSpan.Zero` korunur |
| `Strategy` aggregate | Domain | Değişiklik yok |
| `Order` aggregate | Domain | Değişiklik yok |
| `RiskProfile` aggregate | Domain | Değişiklik yok — `MaxPositionSizePct` field korunur, semantik handler'da reinterpreted |
| `StrategySignal` entity | Domain | Değişiklik yok |
| `StrategySignalSkippedEvent` domain event | Domain | Değişiklik yok (mevcut) |
| `EmitStrategySignalCommand` + handler | Application | Değişiklik yok — signal kaydı duplicate fazından önce atılır |
| `StrategySignalToOrderHandler` | Infrastructure | **Değişir** — duplicate pre-check + sizing override |
| `OrderFilledPositionHandler` | Infrastructure | **Değişir** — `ContextJson` anahtar genişletme (`maxHoldMinutes` OR `maxHoldBars`) |
| `PositionSizingService` | Application | **Değişiklik yok** (kontrat aynı, caller yeni değer verir) |
| `IPositionSizingService` | Application | Değişiklik yok |
| `SnowballSizing` | Infrastructure | **Değişiklik yok** — `CalcMinNotional(equity) = max(equity × 0.20, 20)` doğru zaten |

Yeni abstraction **yok**, yeni aggregate **yok**, yeni event **yok**. ADR-0016'nın `ISystemEventPublisher` + `StrategySignalSkippedEvent` altyapısı yeterli. Dependency rule ihlali yok (Domain → Application → Infrastructure; Api composition root).

### 17.11 Implementation Contracts (Backend-Dev)

#### 17.11.1 `OrderFilledPositionHandler` — ContextJson Anahtar Genişletme

Dosya: `src/Infrastructure/Orders/OrderFilledPositionHandler.cs:76-106`

```csharp
TimeSpan? maxHoldDuration = null;
if (order.StrategyId is long sid)
{
    var freshnessCutoff = now.AddMinutes(-5);
    var lastSignal = await db.StrategySignals.AsNoTracking()
        .Where(s => s.Symbol == order.Symbol
            && s.StrategyId == sid
            && s.EmittedAt >= freshnessCutoff)
        .OrderByDescending(s => s.EmittedAt)
        .FirstOrDefaultAsync(cancellationToken);

    if (lastSignal is not null && !string.IsNullOrWhiteSpace(lastSignal.ContextJson))
    {
        try
        {
            using var doc = JsonDocument.Parse(lastSignal.ContextJson);
            int? holdMinutes = null;

            // ADR-0017 §17.7: prefer maxHoldMinutes (VWAP-EMA V2), fall back
            // to maxHoldBars (pattern strategies). BinanceBot bar = 1 minute.
            if (doc.RootElement.TryGetProperty("maxHoldMinutes", out var mhMin)
                && mhMin.ValueKind == JsonValueKind.Number
                && mhMin.TryGetInt32(out var minutesFromJson)
                && minutesFromJson > 0)
            {
                holdMinutes = minutesFromJson;
            }
            else if (doc.RootElement.TryGetProperty("maxHoldBars", out var mhb)
                && mhb.ValueKind == JsonValueKind.Number
                && mhb.TryGetInt32(out var barsFromJson)
                && barsFromJson > 0)
            {
                holdMinutes = barsFromJson;
            }

            if (holdMinutes is int m && m > 0)
            {
                maxHoldDuration = TimeSpan.FromMinutes(m);
            }
        }
        catch (JsonException)
        {
            // Non-structured ContextJson — silent, time-stop stays inert.
        }
    }
}
```

#### 17.11.2 `StrategySignalToOrderHandler` — Duplicate Pre-Check + Sizing Override

Dosya: `src/Infrastructure/Strategies/StrategySignalToOrderHandler.cs`

Yeni constructor dependency: `IPublisher` (MediatR) — `StrategySignalSkippedEvent` dispatch için. Zaten `IMediator`'dan erişim var (`scope.ServiceProvider.GetRequiredService<IMediator>()`), `Publish` çağrısı aynı mediator ile yapılır (MediatR `IMediator` hem `Send` hem `Publish` destekler).

```csharp
// Branch 2 (entry) — BEFORE the foreach mode loop, after ticker check:
// (already have symbolVo, instrument, ticker, entry)

foreach (var mode in AllModes)
{
    var cid = $"{cidPrefix}-{mode.ToCidSuffix()}";

    // ADR-0017 §17.8: per-(strategy, symbol, mode) duplicate protection.
    // Prevents cascading $40 positions on the same symbol when VWAP zone
    // + volume confirm emit back-to-back bars (Loop 21 BNB 3-min double).
    var hasOpenSameStrategySymbol = await db.Positions.AsNoTracking().AnyAsync(p =>
        p.StrategyId == notification.StrategyId &&
        p.Symbol == symbolVo &&
        p.Status == PositionStatus.Open &&
        p.Mode == mode, ct);

    if (hasOpenSameStrategySymbol)
    {
        _logger.LogInformation(
            "Duplicate signal skipped mode={Mode} strategyId={Sid} symbol={Symbol} cid={Cid}",
            mode, notification.StrategyId, notification.Symbol, cid);

        await mediator.Publish(new StrategySignalSkippedEvent(
            notification.StrategyId,
            notification.Symbol,
            "duplicate_open_position",
            mode.ToString()), ct);

        continue;
    }

    // ... existing RiskProfile + CB + openCount (global) checks ...

    // ADR-0017 §17.9: target-notional semantics (replaces cap-only branch).
    var targetNotional = SnowballSizing.CalcMinNotional(equity);  // max(equity*0.20, 20)
    var hardCap = equity * risk.MaxPositionSizePct;
    var chosenNotional = Math.Min(targetNotional, hardCap);
    var effectiveMinNotional = Math.Max(chosenNotional, instrument.MinNotional);

    // Express chosenNotional as a MaxPositionPct equivalent so PositionSizingService
    // cap branch lands on the target. MinNotional acts as the floor for "too small"
    // exchange rejects. qtyByRisk still clamps large risk drawdowns on wide stops.
    var effectiveMaxPositionPct = equity > 0m
        ? chosenNotional / equity
        : risk.MaxPositionSizePct;

    var sizingResult = sizing.Calculate(new PositionSizingInput(
        Equity: equity,
        EntryPrice: entry,
        StopDistance: stopDistance,
        RiskPct: risk.RiskPerTradePct,
        MaxPositionPct: effectiveMaxPositionPct,  // <-- was risk.MaxPositionSizePct
        MinNotional: effectiveMinNotional,        // <-- now target-equal floor
        StepSize: instrument.StepSize,
        MinQty: instrument.MinQty,
        SlippagePct: slip));

    // ... existing qty <= 0 skip, PlaceOrderCommand, mediator.Send ...
}
```

Şemada **PositionSizingService'e dokunulmaz**. Caller semantiği değiştirir. `effectiveMaxPositionPct = chosenNotional / equity` → `qtyByCap = equity × effectiveMaxPositionPct / entry = chosenNotional / entry` → `notional = chosenNotional`. Math netleşir.

#### 17.11.3 `StrategySignalSkippedEvent` Payload — Doğrulama

`src/Domain/Strategies/Events/StrategyEvents.cs` içinde mevcut record okunup kontrat doğrulanır. Payload `(StrategyId, Symbol, Reason, Mode?)` şemasında değilse ADR-0017 `reason="duplicate_open_position"` ve mode bilgisi eklenir — gerekli alanlar zaten `INotificationHandler<StrategySignalSkippedEvent>` impl'inde kullanılıyor (ADR-0016 §16.9).

### 17.12 Test Matrisi (Backend-Dev İçin)

Domain tests (`tests/Tests/Domain/Positions/PositionTests.cs`):
1. `Position.Open(maxHoldDuration: TimeSpan.Zero)` → `DomainException`
2. `Position.Open(maxHoldDuration: TimeSpan.FromMinutes(10))` → `MaxHoldDuration == TimeSpan.FromMinutes(10)`

Application tests (`tests/Tests/Application/Sizing/PositionSizingServiceTests.cs` — yeni cases):
3. equity=100, minNotional=20, maxPosPct=0.20 → qty × entry == 20
4. equity=50, minNotional=20, maxPosPct=0.40 → qty × entry == 20 (floor)
5. equity=200, minNotional=40, maxPosPct=0.20 → qty × entry == 40
6. equity=500, minNotional=100, maxPosPct=0.20 → qty × entry == 100

Infrastructure tests (`tests/Tests/Infrastructure/Strategies/StrategySignalToOrderHandlerTests.cs`):
7. Given open position same (strategyId, symbol, mode) → order NOT placed + `StrategySignalSkippedEvent` published with reason `duplicate_open_position`
8. Given open position DIFFERENT symbol same strategy → order placed normally
9. Given open position same symbol DIFFERENT mode → order placed (mode isolation)

Infrastructure tests (`tests/Tests/Infrastructure/Orders/OrderFilledPositionHandlerTests.cs` — yeni test dosyası veya mevcut'u genişlet):
10. ContextJson contains `maxHoldMinutes=10` → `Position.MaxHoldDuration == TimeSpan.FromMinutes(10)`
11. ContextJson contains `maxHoldBars=15` (legacy pattern path) → `Position.MaxHoldDuration == TimeSpan.FromMinutes(15)`
12. ContextJson contains both `maxHoldMinutes=10` and `maxHoldBars=15` → `maxHoldMinutes` preferred → `TimeSpan.FromMinutes(10)`
13. ContextJson contains neither → `Position.MaxHoldDuration == null` (backward compat)

Integration smoke (tester agent, Playwright yok — backend-only):
14. Paper mode `PlaceOrderCommand` + `OrderFilledEvent` dispatch → DB `Positions.MaxHoldDurationSeconds = 600` (10 dk × 60)

Toplam: 14 test case (yeni/genişleyen). `src/CLAUDE.md` test kuralı: Domain saf, Application handler + Moq, Infrastructure InMemory DbContext.

### 17.13 Migration + DB Impact

**Migration yok.** `Position.MaxHoldDurationSeconds` kolonu zaten mevcut (ADR-0014); sadece NULL olarak yazılıyordu. Fix sonrası bu kolon dolar.

Loop 22 boot migration reset (`DELETE FROM Positions; ...` — boot-brief §DB reset) handler düzeltmesinden bağımsız; handler düzeltmesi Loop 22 ilk yeni trade'de etkisini gösterir.

### 17.14 Operasyonel Gözlem Planı

Loop 22 wakeup (t=30 min) sonrası doğrulama:
- `SELECT Id, Symbol, StrategyId, MaxHoldDurationSeconds FROM Positions` → MaxHoldDurationSeconds = 600 (10 dk) olmalı
- `SELECT COUNT(*) FROM Positions WHERE StrategyId=X AND Symbol='BNBUSDT' AND Status='Open'` → ≤ 1 (duplicate yok)
- `SELECT Symbol, Quantity * AverageEntryPrice AS notional FROM Positions WHERE Status='Open'` → ~$20 (hedef notional)
- `SELECT COUNT(*), EventType FROM SystemEvents WHERE EventType=41 GROUP BY EventType` → SignalSkipped row'lar varsa duplicate protection tetiklendi demektir
- `SELECT * FROM Positions WHERE Status='Open' AND (UtcNow - OpenedAt) > INTERVAL 10 min` → boş olmalı (TimeStop kapatmış)

## Consequences

### Pozitif

- **TimeStop gerçek tetiklenir.** 10dk sonrası açık kalan pozisyon kalmaz — Loop 21'in 31.5 dk BNB pos gibi drift'ler biter.
- **Duplicate guard.** `$78 BNB pos` senaryosu tekrar etmez. Her `(strategy, symbol, mode)` açık pos tekilliği invariant olarak sağlanır.
- **Sizing matematik temizlenir.** Kullanıcı hedefi `max(equity × 0.20, $20)` tam olarak gerçekleşir. Cash = equity − sum(notional) tutarlı olur (Loop 21 −$18.75 = equity 100 − 2×$40 + 2 yerine 1 yakın).
- **Aggregate sınırı bozulmaz.** Yeni repository yok, yeni aggregate yok. Handler-level orchestration CQRS ve DDD kurallarıyla uyumlu.
- **Audit trail tam.** Duplicate skip'ler `SystemEvents`'e 41 kodu ile düşer, UI'da görünür, ileri analiz (kaç skip/saat?) mümkün.
- **Backward compat.** `maxHoldBars` anahtarı hala tanınır — ADR-0014 pattern stratejisi gelecekte canlanırsa çalışır.
- **Migration yok.** DB şeması dokunulmaz — fix riskini minimize eder.

### Negatif / Tradeoff

- **Sizing handler tarafı yeni hesap.** `effectiveMaxPositionPct = chosenNotional / equity` trick `PositionSizingInput` semantiğini bükerek kullanır; `IPositionSizingService` arayüzüne `TargetNotional` alanı eklemek daha temiz olurdu. Backlog Loop 23 — şimdi hızlı fix.
- **Duplicate pre-check async DB round-trip.** Fan-out döngüsü her mode için +1 query. Üç mode × 1 query = ~3-5ms paper/testnet — kabul edilir, CQRS read path + `AsNoTracking`.
- **Test yüzeyi 14 case genişler.** Mevcut 3 handler test + 6 sizing test + 3 position test → yeni 14'le toplam ~26. Review yükü artar.
- **`MaxPositionSizePct` semantik ikilik.** Artık hem "üst güvenlik cap" hem "default sizing driver" (eski kod); handler yeni formülde override ediyor. Okuyan developer için confusing. Loop 23 cleanup ile aynı yolda çözülür: kontrat yeniden yazılırken `MaxPositionSizePct` sadece cap, `TargetNotionalPct` ayrı alan.
- **Duplicate guard mode-per-mode.** Paper, testnet, mainnet ayrı değerlendirilir. Üç mod aynı symbol aynı strategyId'de farklı pos açabilir. ADR-0008 mode izolasyonu ile tutarlı — kullanıcı seviyesinde üç ayrı portfolio.

### Nötr

- ADR-0011/0012/0014/0015/0016 kararları uyumlu. `MaxPositionSizePct = 0.40` configuration değiştirilmez.
- Domain aggregate sayısı değişmez.
- MediatR command/query yüzeyi değişmez.
- `IMarketIndicatorService`, evaluator, indicator service dokunulmaz.
- Parametreler (SlopeTolerance −0.003, VwapTolerancePct 0.005, VolumeMultiplier 0.5, StopPct 0.003/0.004, TpGrossPct 0.005, MaxHoldMinutes 10) korunur.

## Alternatives (Reddedilen)

### Alt-A — Duplicate Protection Domain Invariant (Position.Open)

`Position.Open` içinde "aynı symbol + strategyId için başka açık Position olmamalı" invariantı. Domain bu invariantı check edemez (cross-aggregate query gerektirir, `Position` aggregate diğer `Position` state'ini okumaz — DDD tek aggregate per transaction kuralı). Repository-level query olmaksızın mümkün değil; eğer eklersek `IPositionRepository` domain layer'a kaçırılır → Clean Architecture dependency rule ihlali. **Red.**

### Alt-B — Duplicate Protection StrategySignal Emit Seviyesi

`EmitStrategySignalCommand` handler'ı signal persist etmeden önce `Position` state check etsin. Bu pattern, signal log'unun bozulmasına yol açar (signal emit edilmedi ama aslında strateji emit koşullarını karşılıyor — analiz verisi kaybolur). Ayrıca `EmitStrategySignalCommand` handler'ı command-side; cross-aggregate query command handler'a katmakla ondalık karmaşa getirir. **Red — fan-out handler doğru yer.**

### Alt-C — MaxOpenPositionsPerSymbol Yeni RiskProfile Alanı

`RiskProfile.MaxOpenPositionsPerSymbol = 1` default. Generic ama iki farklı kavramı tek alana sıkıştırır (global vs per-symbol). ADR-0011 risk profili simple tutuldu; ek alan config drift riski getirir. Duplicate check inherently `== 1` olacak senaryo, flag'e gerek yok. **Red — backlog'a alınır gelecek tasarımı net olunca.**

### Alt-D — Sizing'de `MaxPositionSizePct`'yi 0.40 → 0.20'ye Çekmek

Config'te `MaxPositionSizePct = 0.20` yap. Hedef %20 cap → $100'de $20 notional. Basit, tek satır. Ama:
  1. Kullanıcı floor $20'yi istiyor (equity $50 → $10 değil $20). Config sadece cap; floor yok.
  2. ADR-0014 reform %15 → %40 argümanı (günde 22 trade × $15 = $330 ciro anlamsız) korunur — cap'i tekrar daraltmak reform'u geri alır.
  3. Cap-only semantik `targetNotional = max(equity × 0.20, 20)` kuralını ifade edemez.

**Red.**

### Alt-E — `Position.OpenFromSignal` Yeni Domain Method

Boot-brief öneri: `Position.OpenFromSignal(strategyId, symbol, side, entry, qty, stop, tp, maxHoldSeconds, ...)` yeni factory method. Mevcut `Position.Open(..., maxHoldDuration: TimeSpan?)` bu imzayı zaten kapsıyor (ADR-0014 eklemesi). Yeni method eklemek API yüzeyini büyütür, YAGNI. `OrderFilledPositionHandler` zaten `Position.Open(..., maxHoldDuration:)` çağırıyor; eksik olan handler'ın **ContextJson okumasının** doğru anahtara bakması. Method eklemeye gerek yok. **Red — domain imzası yeterli.**

### Alt-F — `maxHoldMinutes` Yerine `maxHoldBars` Evaluator'da

`VwapEmaStrategyEvaluator` `maxHoldBars` yazsın (handler'ı değiştirmeden) — eski sözleşme restore. Bu da çalışır ama `maxHoldBars` kavramı pattern stratejisinin bar-time-frame-agnostic yapısına bağlı (bar 5m veya 1m olabilir). VWAP-EMA V2 explicit dakika düşünüyor (`MaxHoldMinutes = 10` seed field). Semantik olarak `maxHoldMinutes` daha net. Handler her iki anahtarı desteklerse future stratejiler uygun olanı seçer. **Red — evaluator semantik doğru, handler genişler.**

### Alt-G — ContextJson'a Yazılan Alanları Schema'ya Bağla

`ContextJson`'u typed record'a map eden bir `IStrategySignalContextParser` servisi. Schema validation + compile-time safety. Güzel ama over-engineering — `ContextJson` opak by design (ADR-0016 §16.11), her evaluator farklı payload yazabilmeli. Typed path oluşturmak tüm evaluator'ları bağlar. **Red — opak JSON tercih edilir, handler minimum alan okur.**

### Alt-H — Sizing Input Kontratına `TargetNotional` Ekle

`PositionSizingInput.TargetNotional` field'ı eklenir, `PositionSizingService.Calculate` içeri alır, cap/min/target üç branch netleşir. Bu **doğru tasarım** ama Application kontrat değişimi reviewer + 9 test dosyası impact. Loop 22 zaten 3 backend + 6 UI bug taşıyor; scope kontrol için `Loop 23 backlog`. **Kısmi red — Loop 23 tamamlar.**

## Source

- [ADR-0011 Equity-Aware Sizing and Risk Tracking](./0011-equity-aware-sizing-and-risk-tracking.md)
- [ADR-0012 Trade Frequency Realism Reform](./0012-trade-frequency-realism-reform.md)
- [ADR-0014 Pattern-Based Scalping Reform](./0014-pattern-based-scalping-reform.md) §14.5 time-stop, ContextJson maxHoldBars convention
- [ADR-0015 VWAP-EMA Hybrid Strategy](./0015-vwap-ema-hybrid-strategy.md) §15.1 three-seed design
- [ADR-0016 VWAP-EMA Hybrid V2 Tuned](./0016-vwap-ema-hybrid-v2-tuned.md) §16.9 SystemEvents publisher + SignalSkipped (41)
- [`loops/loop_21/summary.md`](../../loops/loop_21/summary.md) — halt t~115, 31.5dk BNB pos, cash −$18.75
- [`loops/loop_20/user-feedback-ui.md`](../../loops/loop_20/user-feedback-ui.md) §Kritik bug 1 (cash negatif), §Kritik bug 3 (ETH 404), §Kritik bug 2 (TP/SL)
- [`loops/loop_22/boot-brief.md`](../../loops/loop_22/boot-brief.md) §Backend 3 bug tanımı
- [`src/Infrastructure/Strategies/StrategySignalToOrderHandler.cs`](../../src/Infrastructure/Strategies/StrategySignalToOrderHandler.cs) — fan-out + sizing call site
- [`src/Infrastructure/Orders/OrderFilledPositionHandler.cs`](../../src/Infrastructure/Orders/OrderFilledPositionHandler.cs) — ContextJson parse + Position.Open
- [`src/Infrastructure/Strategies/Evaluators/VwapEmaStrategyEvaluator.cs`](../../src/Infrastructure/Strategies/Evaluators/VwapEmaStrategyEvaluator.cs) — maxHoldMinutes emit
- [`src/Infrastructure/Strategies/SnowballSizing.cs`](../../src/Infrastructure/Strategies/SnowballSizing.cs) — max(equity × 0.20, 20) floor formülü
- [`src/Application/Sizing/PositionSizingService.cs`](../../src/Application/Sizing/PositionSizingService.cs) — qtyByRisk + qtyByCap + floor zinciri
- [`src/Domain/Positions/Position.cs`](../../src/Domain/Positions/Position.cs) — Open(..., maxHoldDuration) invariant
- [Microsoft Learn — DDD + CQRS](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/)
- [jasontaylordev/CleanArchitecture](https://github.com/jasontaylordev/CleanArchitecture)
- [ardalis/CleanArchitecture](https://github.com/ardalis/CleanArchitecture)
- [joelparkerhenderson/architecture-decision-record (MADR)](https://github.com/joelparkerhenderson/architecture-decision-record)
