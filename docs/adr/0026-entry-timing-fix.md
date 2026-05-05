# 0026. Entry Timing Fix — R:R Symmetry First, Pullback Limit Second

Date: 2026-05-04
Status: Proposed (Loop 104 halt sonrası — 25 loop -$24.5 net, 0 pozitif loop)
Relates to: ADR-0023 (R:R asymmetry — superseded by §26.5), ADR-0024 (PatternComposer — korunur), ADR-0025 (Futures pivot — korunur)
Memory ref: `feedback_frekans_kartopu.md`, `feedback_no_dead_code.md`, `loop_discipline.md`, `trading_vision.md`

> **Özet:** Loop 80-104 boyunca pattern-composite scalper bar close anında market emit ediyor; bar zaten zirvede kapandığı için entry tepe noktasında oluyor; mark sonrası geri çekiliyor; BE arm tetiklenmiyor (peak +%0.10 eşiği aşılamıyor); sonuç R:R 1:15 (avg win $0.04 vs avg loss $0.60), 25 loop -$24.5 net, 0 pozitif loop. Üç çözüm seçeneği değerlendirildi: (A) Pullback Limit Order, (B) Next-Bar Confirmation, (C) R:R 1:1 simetri tune. **Karar: C → A → B sırası.** Loop 105 sadece **Option C** uygulanır (parametrik tune, kod yok, 1 commit). Loop 106-107 pozitif WR doğrulanırsa Option A (Limit + 5dk pending timeout) implement edilir (4 commit). Option B "candidate signal" karmaşıklığı (PendingSignal aggregate) Loop 110+ değerlendirilir, şimdilik reddedildi. Bu sıralama kullanıcı vizyonuna (`feedback_frekans_kartopu.md`: 30+/h frekans, kartopu kar) ve loop disiplinine (`loop_discipline.md`: kâr olunaya kadar devam) en hızlı yol; mimari değişikliği parametrik tune'un çözmediği kanıtlandıktan **sonra** tetikler (CLAUDE.md §13 deprecated kod yasağı + KISS).

---

## Context

### 26.1 Loop 80-104 Pattern Net Özeti

`loops/loop_104/halt-t90.md` özetinden 25 loop disiplini:

| Aralık | Loop | Win Rate | Avg Loss | Realized |
|---|---|---|---|---|
| Spot long-only | 80-91 | düşük | -$0.40 | -$17.04 |
| Futures pivot başlangıç | 92 | bug | - | -$0.65 |
| Futures bug fix | 93-94 | %50 | -$0.60 | -$1.16 |
| Silent run | 95-99 | bug | - | $0 (0 trade) |
| Stable Futures | 100 | %33 | -$0.65 | -$1.26 |
| Refactor | 101 | %67 | -$0.64 | -$0.57 |
| Tune attempts | 102 | %0 | -$0.34 | -$0.69 |
| Tune attempts | 103 | %0 | -$0.13 | -$0.51 |
| **Halt** | 104 | %0 | -$0.48 | **-$1.91** |

**Kümülatif:** 25 loop, ~140 trade, **-$24.5 net**, **0 pozitif loop**, ortalama WR ~%30, **R:R 1:15** (avg win $0.04 vs avg loss $0.60). Paper $500 → $475.5.

### 26.2 Kök Neden — "Buy at Top" Pattern

Loop 104 halt raporundan birebir ([halt-t90.md L34-44](../../loops/loop_104/halt-t90.md)):

1. Bot bar close anında "uptrend pattern" yakalayıp Long emit (BullishEngulfing / Donchian breakout / RsiOversoldRecovery rising).
2. Bar close zaten zirvede (yukarı kapanış — pattern'in tanım gereği).
3. Pos açıldıktan sonra mark **geri çekilme** (bar zirvesinden doğal mean-revert).
4. Peak entry üstüne **nadir** çıkıyor (~%20-30 of trades).
5. BE arm asla olmuyor — peak +%0.10 eşiği bile aşılamıyor (`MarkToMarketWorker.MoveStopToBreakEven` tetiklenmiyor).
6. Trailing locked profit yok.
7. Sonuç: SL hit -$0.60 büyük loss baskın, küçük TP yakın profit ($0.04) nadir.

**Kökü:** Entry timing — bar close'da emit etmek **zirve yakalama** demek. Bu trend-following bias **doğru niyettir** (uptrend Long mantıklı), ama **yanlış zaman** (bar zirvesinde fill = anında negatif PnL girişi).

### 26.3 Mimari Bağlam — Mevcut Akış

```
KlineClosedEvent (Application)
        │
        ▼
StrategyEvaluationHandler (Infrastructure/Strategies)
        │
        ▼ IStrategyEvaluator.Evaluate(symbol, ct)
PatternCompositeEvaluator (Infrastructure/Strategies/Evaluators)
        │ 1. BarSnapshot build (IMarketIndicatorService)
        │ 2. IPatternRegistry → Long+Short bucket scoring
        │ 3. WeightedScorePatternComposer.Compose(...) → CompositeSignalDecision
        │ 4. MTF gate Direction-aware filter
        │ 5. Cooldown check
        │ 6. Strategy.EmitSignal(suggestedPrice, suggestedStop, suggestedTp, ...)
        ▼
StrategySignalEmittedEvent (Domain Event)
        │
        ▼
StrategySignalToOrderHandler (Infrastructure/Strategies) — fan-out 3 mode
        │ 1. duplicate-protection check
        │ 2. RiskGate / MaxOpenPositions / Equity
        │ 3. SnowballSizing → notional
        │ 4. PlaceOrderCommand (Type=Market.ToString(), Tif=Ioc)
        ▼
PlaceOrderCommandHandler (Application/Orders)
        │ 1. Idempotency
        │ 2. Filter validation (tickSize/stepSize/minNotional)
        │ 3. RiskGate
        │ 4. Order.Place(...)
        │ 5. switch(Mode) → HandlePaper(...) → IPaperFillSimulator.SimulateAsync(...)
        ▼
FuturesPaperFillSimulator (Infrastructure/Trading/Paper)
        │ 1. Depth walk @ bar close ask (Long) / bid (Short)
        │ 2. AvgFillPrice, Commission, Slippage
        │ 3. Order.RegisterFill(...)
        ▼
OrderFilledEvent → OrderFilledPositionHandler → Position.Open(...)
```

**Kritik gözlem:** Step "Strategy.EmitSignal" ile "FuturesPaperFillSimulator" arasında **0 gecikme + 0 sorgu** var. Bar close anındaki ask/bid ile fill anındaki ask/bid **aynı tick**. Yani entry **tam bar zirvesinde**.

### 26.4 Halihazırda Korunacaklar — Net Sınır

ADR-0024 + ADR-0025 mimarisi tamamen korunur:

- `Strategy` aggregate, `StrategySignal` entity, `StrategyType.PatternComposite=3` — değişmez.
- `IPatternDetector` port + `BarSnapshot` shared dto + 17 detector dosyası — değişmez.
- `IPatternSignalComposer` interface + `CompositeSignalDecision` record — değişmez (sadece `EntryPrice` field'ı yorumu güncellenir: emit'ten sonraki **niyet fiyatı**, market fill **veya** limit fiyat).
- `PatternCompositeEvaluator` — değişmez (Option A'da bile composer çıktısı aynı, evaluator değişmez).
- `IExchangeClient` port + `BinanceFuturesClient` — değişmez. (Halihazırda `CancelLiveOrderAsync` var; Limit + cancel destek hazır).
- `Order` aggregate + `OrderType.Limit=2` enum değeri — **zaten var** (Order.cs L60-63 `OrderType.Limit` validation aktif).
- `OrderStatus` enum — **zaten var** (`New=1, PartiallyFilled=2, Filled=3, Cancelled=4, Rejected=5, Expired=6`). Yeni status eklenmez.
- `MarkToMarketWorker` SL/TP/BE/Trailing Direction-aware — değişmez.
- `VirtualBalance` futures cüzdan modeli (WalletBalance + AllocatedMargin + UnrealizedPnl) — değişmez.

**Değişen / yeni (Option A için, Loop 106+):**
- `PlaceOrderCommand` zaten `Type` ve `Price` parametreli — sadece `StrategySignalToOrderHandler` Market yerine Limit gönderir.
- Yeni `PendingLimitOrderTimeoutWorker` (BackgroundService) — 5dk pending timeout cancel.
- `Order.PendingExpiresAt` field eklenir (decimal? veya DateTimeOffset?) — DB migration.
- `FuturesPaperFillSimulator` — Limit order için "fill if mark crosses limit" simülasyonu (yeni branch).

---

## Decision

### 26.5 Karar: C → A → B Sırası

**Loop 105 = Option C only.** Parametrik tune, kod yok, 1 commit. Hipotez doğrulaması: "R:R 1:1 simetri WR'yi düşürmez ama loss büyüklüğünü TP büyüklüğüne eşitler → break-even WR %50 (mevcut %30 WR ile yine -%20 expectancy)."

**Loop 105 başarı kriteri:**
- ≥10 trade
- Avg win × WR ≈ Avg loss × (1−WR) (cebirsel break-even)
- Net realized > -$0.50 (Loop 104 -$1.91'e göre %75 iyileşme)
- En az 3 TP hit (Loop 80-104 boyunca toplam <5 TP hit)

**Eğer Loop 105 başarı kriterini tutturmazsa:** Option A devreye girer (Loop 106).
**Eğer Loop 105 net pozitif:** Option C kalıcı, Option A optional — fakat A entry kalitesini artırır, frekansı düşürse de quality > quantity faydası. Loop 106-107 Option A implement.

**Loop 110+ Option B:** Pozitif streak sonrası "candidate signal multi-bar state" karmaşıklığı değerlendirilir; şimdilik **reddedildi** (§26.10).

### 26.6 Option C — Parametrik R:R 1:1 (Loop 105 öncelik)

**Mevcut (ADR-0023 + Loop 104 tune):**
```
TpAtrMultiplier: 1.5  → R = ATR × 1.5 (TP)
SlAtrMultiplier: 0.6  → R = ATR × 0.6 (SL)
MinTpPct: 0.0040     MaxTpPct: 0.010
MinSlPct: 0.0012     MaxSlPct: 0.003
R:R nominal:  2.5   (1.5 / 0.6)
R:R fiili:   1:15   (avg win $0.04 vs avg loss $0.60 — Loop 80-104)
```

**Loop 105 (Option C):**
```
TpAtrMultiplier: 0.6  → simetrik
SlAtrMultiplier: 0.6  → korunur
MinTpPct: 0.0012     MaxTpPct: 0.003
MinSlPct: 0.0012     MaxSlPct: 0.003
R:R nominal:  1.0   (simetri)
BE_WR fiili:  %50
```

**Hesap:**
- Avg win hedef: $0.18 (Loop 104 SL büyüklüğüne eşit, fakat artık TP)
- Avg loss hedef: -$0.18 (SL aynı)
- WR %50 ⇒ expectancy = 0 (break-even)
- WR %55 ⇒ expectancy = $0.018/trade × 30 trade/h = $0.54/h (Loop 105 4h hedefi: +$2.16)

**Frekans:** TP daha yakın → hit oranı **artar** (%30 → %45-50 tahmini); MaxHold tutarsa loss yine sınırlı. CLAUDE.md §12 "30+/h" kuralı korunur (composer threshold + cooldown sabit).

**Mimari etki:** **Sıfır kod**. Sadece `Strategy.ParametersJson` seed güncellenir + `PatternComposerOptions` defaultları değişir. `PatternComposerOptions.cs` (Application/Strategies/Patterns) sabit defaultlar var; Loop 105 sadece config override.

**Değişen dosyalar:**
- `appsettings.json` veya Strategy seed (Infrastructure/Persistence/SeedData) — ParametersJson içinde TP multiplier + MinTp/MaxTp.
- 0 yeni interface, 0 yeni method, 0 migration.

### 26.7 Option A — Pullback Limit Order (Loop 106-107)

**Senaryo:** Bar close emit anında market yerine **bar close × (1 - 0.001)** fiyatında Long Limit order (Short için × (1 + 0.001)). 5dk içinde fill olmazsa cancel.

**Niyet:** Mark bar zirvesinden -%0.10 çekildikten sonra fill — entry tam zirve değil, "küçük dip". Bar close $100 olsun → Limit @ $99.90; mark $99.90'a düştüğünde fill; pos açıldı; sonraki bar trend devam ederse $100+ peak → BE arm + TP.

**Trade-off:**
- Pozitif: Entry kalitesi artar (peak fill yerine geri-çekilme fill).
- Negatif: ~%30-50 emit'lerde bar geri çekilmiyor → cancel → 0 fill (kaçırılan trade). Frekans düşer.
- CLAUDE.md §12 "0 emit > 1h pivot" kuralı: 5dk timeout ile pending periyodu kısa, cancel sonrası bot yeni bar'da yine emit — 1h boş kalmaz.

**Mimari etki — 4 commit (sırayla):**

#### Commit A.1: Domain — Order PendingExpiresAt + Status genişletmesi

`Order.cs` aggregate'ine yeni field:
```csharp
public DateTimeOffset? PendingExpiresAt { get; private set; }

public static Order PlaceLimit(
    string clientOrderId, Symbol symbol, OrderSide side,
    decimal quantity, decimal limitPrice, decimal? stopPrice,
    long? strategyId, TradingMode mode, DateTimeOffset now,
    decimal? takeProfit,
    DateTimeOffset pendingExpiresAt)  // YENI
{
    var order = Place(clientOrderId, symbol, side, OrderType.Limit, TimeInForce.Gtc,
        quantity, limitPrice, stopPrice, strategyId, mode, now, takeProfit);
    order.PendingExpiresAt = pendingExpiresAt;
    return order;
}

public void ExpirePending(DateTimeOffset now)  // YENI behavior
{
    if (Status is not OrderStatus.New) return;  // partial fill → kalan kısmı expire değil cancel
    Status = OrderStatus.Expired;
    UpdatedAt = now;
    RaiseDomainEvent(new OrderExpiredEvent(ClientOrderId, Symbol.Value));
}
```

`OrderStatus` enum **değişmez** — `Expired=6` yeterli (mevcut). `PendingLimitOrderTimeoutWorker` cancel/expire kararını verir.

#### Commit A.2: Application — StrategySignalToOrderHandler Limit branch

`StrategySignalToOrderHandler.Handle(...)` içinde entry hesabı sonrası:
```csharp
// Loop 106 — Option A: Pullback Limit @ bar close × (1 ± offsetPct)
var limitOffsetPct = 0.001m;  // %0.10
var limitPrice = direction == TradeDirection.Long
    ? notification.SuggestedPrice.Value * (1m - limitOffsetPct)
    : notification.SuggestedPrice.Value * (1m + limitOffsetPct);
limitPrice = AlignToTickSize(limitPrice, instrument.TickSize);

var pendingExpiresAt = _clock.UtcNow.AddMinutes(5);

var cmd = new PlaceOrderCommand(
    cid, notification.Symbol, side.ToString(),
    OrderType.Limit.ToString(),    // ← Market yerine Limit
    TimeInForce.Gtc.ToString(),    // ← Ioc yerine Gtc (5dk pending)
    sizingResult.Quantity,
    limitPrice,                    // ← Price dolu
    notification.SuggestedStopPrice,
    notification.StrategyId,
    mode,
    TakeProfit: notification.SuggestedTakeProfit);
// PendingExpiresAt → Order.PlaceLimit factory'sine PlaceOrderCommand uzantısı ile geçer.
```

`PlaceOrderCommand` record'una opsiyonel `DateTimeOffset? PendingExpiresAt = null` field eklenir (default null = market davranışı korunur, geriye dönük uyumlu).

#### Commit A.3: Infrastructure — FuturesPaperFillSimulator Limit branch

Mevcut simulator depth walk @ bookTicker (instant fill). Limit için yeni branch:

```csharp
public async Task<PaperFillOutcome> SimulateAsync(Order order, ...)
{
    if (order.Type == OrderType.Market) { /* mevcut depth walk */ }
    else if (order.Type == OrderType.Limit)
    {
        // Pending — fill etmez, sadece order'ı pending durumda kaydet.
        // Gerçek fill MarkToMarketWorker veya yeni PendingLimitFillWorker tarafından
        // mark price limit'i kestiğinde yapılır (Long: ask ≤ limit; Short: bid ≥ limit).
        return PaperFillOutcome.Pending(order.ClientOrderId);
    }
}
```

Yeni `PendingLimitFillWorker` (BackgroundService), her tick'te:
```csharp
foreach (var pendingOrder in _db.Orders.Where(o => o.Status == OrderStatus.New && o.Type == OrderType.Limit && o.PendingExpiresAt > _clock.UtcNow))
{
    var ticker = await _db.BookTickers.FirstAsync(b => b.Symbol == pendingOrder.Symbol);
    var crosses = pendingOrder.Side == OrderSide.Buy
        ? ticker.AskPrice <= pendingOrder.Price
        : ticker.BidPrice >= pendingOrder.Price;
    if (crosses)
    {
        // simulate fill at limit price (slippage 0 için Limit guarantee)
        var fill = SimulateLimitFill(pendingOrder, ticker, instrument);
        pendingOrder.RegisterFill(...);
        // VirtualBalance margin akış (mevcut Paper logic)
    }
}
```

#### Commit A.4: Infrastructure — PendingLimitOrderTimeoutWorker

```csharp
public sealed class PendingLimitOrderTimeoutWorker : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var clock = scope.ServiceProvider.GetRequiredService<IClock>();
            var now = clock.UtcNow;
            var expired = await db.Orders
                .Where(o => o.Status == OrderStatus.New
                    && o.Type == OrderType.Limit
                    && o.PendingExpiresAt != null
                    && o.PendingExpiresAt < now)
                .ToListAsync(ct);
            foreach (var order in expired)
            {
                order.ExpirePending(now);
            }
            if (expired.Any()) await db.SaveChangesAsync(ct);
            await Task.Delay(TimeSpan.FromSeconds(15), ct);
        }
    }
}
```

LiveTestnet için `IExchangeClient.CancelLiveOrderAsync(symbol, clientOrderId)` çağrılır — port zaten var (IExchangeClient.cs L37-40). Paper için sadece DB status update.

**Test stratejisi (Option A için, 8 yeni test):**
- `Order.PlaceLimit` factory testi (3) — limit price valid, stop price valid, pending expiresAt geleceğe.
- `Order.ExpirePending` testi (1).
- `StrategySignalToOrderHandler` Limit branch testi (2) — Long limit < bar close, Short limit > bar close.
- `PendingLimitFillWorker` cross detection testi (2) — Long ticker.ask ≤ limit ⇒ fill; Short ticker.bid ≥ limit ⇒ fill.

**Migration `AddOrderPendingExpiresAt` (1 migration):**
- `Orders.PendingExpiresAt DATETIMEOFFSET NULL` (default null = market order).
- Backfill: olduğu gibi (eski Market orderlar null kalır).
- Filter index: `IX_Orders_Status_Type_PendingExpiresAt_Filtered` (Status=New AND Type=Limit) — worker query optimizasyonu.

### 26.8 Option B — Next-Bar Confirmation (Loop 110+ değerlendirilir, şimdilik reddedildi)

**Senaryo:** Bar close composer "candidate" emit eder (state: Pending). Sonraki bar HIGH > previous HIGH (Long için; Short: LOW < prev LOW) ise market fill; aksi halde discard.

**Mimari etki — büyük (8+ commit):**
- Yeni domain concept: `PendingSignal` aggregate veya StrategySignal status. State: Candidate / Confirmed / Discarded.
- `KlineClosedEvent` handler 2 bar bilgisi tutar (pending signal scope). Multi-bar state tracking gerekli.
- `StrategySignalEmittedEvent` semantik değişir — "candidate" mu "confirmed" mu? Downstream handler (StrategySignalToOrderHandler) sadece Confirmed için fan-out yapar.
- Cooldown logic değişir — candidate emit cooldown tetiklemez (gerçek emit henüz yok).
- Test yükü: PendingSignal aggregate testleri, multi-bar state geçişleri (~15 test).

**Reddedilme gerekçesi (Loop 105 için):**
1. **CQRS sınırını yeniden çizer** — yeni aggregate (PendingSignal) veya StrategySignal status field eklenir; ADR-0024 §24.6 "yeni aggregate açma" kararı tersine çevrilir.
2. **Frekans düşüşü kanıtsız büyük** — Loop 80-104 verisinde "next bar HIGH > prev HIGH" oranı tahmini %40-50; emit'in yarısı discard edilir, frekans -%50.
3. **Karmaşıklık-fayda dengesizliği** — Option C zero-code; Option A 4 commit; Option B 8+ commit. Loop disiplini (`loop_discipline.md`: kar olunaya kadar devam) en hızlı yol C → A.
4. **KISS ihlali** — multi-bar state tracking yeni infrastructure katmanı (PendingSignal repository, lifecycle worker, audit). Anti-CLAUDE.md §13 (yeni komplekslik için kanıt yok).

**Loop 110+ tetikleyicisi:** Option C ve A birlikte uygulandı; pozitif loop yakalandı; AMA WR hâlâ %50 altında AMA expectancy pozitif. Bu durumda B "false-breakout filter" olarak quality booster işlevi görür. Şimdilik **erteleme**.

### 26.9 ADR-0023 R:R 1:2.5 Asimetri ile İlişki

ADR-0023 Loop 35'te yazıldı, R:R 1:2.5 (TpAtrMul 1.5 / SlAtrMul 0.6) önerdi. Mantık: "BE_WR %28.6 → ulaşılabilir eşik." Loop 80-104'te WR %30 civarı geldi, AMA **avg win çok küçük** ($0.04 — TP nadir hit) **avg loss çok büyük** (-$0.60 — SL hit). R:R nominal 2.5 fiili 1:15. Bu ADR-0023'ün **temel varsayımının çürümesi**:

> ADR-0023 §23.6 tahmin: 4 TP × $0.325 + 4 SL × -$0.195 = +$0.42 net.
> Gerçek (Loop 80-104): 0-1 TP × $0.04 + 4 SL × -$0.60 = -$2.40 net per 5 trade.

**Karar:** ADR-0023 §23.4 parametre matrisi **superseded** ADR-0026 §26.6 Option C tarafından. ADR-0023'ün "risk-first" stratejik amacı korunur (SL clip %0.30 cap), sadece TP simetriye çekilir. Yeni varsayım:

> Loop 105 tahmin: 5 TP × $0.18 + 5 SL × -$0.18 = $0 net (BE), WR %55+ → +$0.18/trade × 30 trade/h = $5.4/h.

ADR-0023 status: **Superseded by ADR-0026 §26.6** (R:R 1:1 simetri). Tarihsel ders: "nominal R:R fiili R:R'a eşit değil — geometri × hit oranı = expectancy."

### 26.10 Composer Geometri Tier — Tier Multiplier Düzeltmesi

ADR-0024 §24.12 skor-tier multiplier:
- Skor 5-7: TpMul 1.3, SlMul 0.7
- Skor 7-9: TpMul 1.5, SlMul 0.6 (default)
- Skor 9-11.5: TpMul 1.8, SlMul 0.5

**Loop 105 değişimi (Option C):**
- Skor 5-7: TpMul 0.5, SlMul 0.7 (R:R 1:0.71 — düşük güvenli)
- Skor 7-9: TpMul 0.6, SlMul 0.6 (R:R 1:1 — simetri)
- Skor 9-11.5: TpMul 0.7, SlMul 0.5 (R:R 1:1.4 — güvende olunca biraz agresif)

`PatternComposerOptions.TpAtrMultiplierLow/High` + `SlAtrMultiplierLow/High` **mevcut** (ADR-0024 §24.14, dosya `Application/Strategies/Patterns/PatternComposerOptions.cs`). Loop 105 commit sadece bu defaultları + Strategy seed ParametersJson update.

---

## Consequences

### Pozitif

1. **Loop 105 sıfır risk:** Sadece parametre tune, kod yok, migration yok. CB reset + bot restart yeterli. Geri dönüş 1 commit revert.
2. **Hızlı doğrulama:** 4h loop (`loop_discipline.md`: bozuk ise hemen halt+fix+yeni loop) — "R:R simetri WR'yi düşürmüyor mu?" hipotezi 4h'de yanıtlanır.
3. **Mimari değişikliği gerekçeli:** Eğer Loop 105 Option C başarı kriterini tutturmazsa Option A devreye girer; **kanıtlı problem** üzerine mimari büyütülür (over-engineering yok).
4. **Option A hazır:** `IExchangeClient.CancelLiveOrderAsync` zaten var; `OrderType.Limit` zaten var; `OrderStatus.Expired` zaten var. Sadece `Order.PendingExpiresAt` field + 2 worker eklemek yeterli — minimal mimari delta.
5. **CLAUDE.md §13 uyumu:** Deprecated kod yasağı; ADR-0023 §23.4 tablosu superseded işaretlenir, eski parametreler Strategy seed'inden silinir.
6. **Vision uyumu:** `feedback_frekans_kartopu.md` 30+/h korunur; cooldown + composer threshold değişmez. Sadece TP yakınlaştığı için hit oranı artar.

### Negatif / Tradeoff

1. **Option C tek başına yetmeyebilir:** R:R 1:1'de WR <%50 ise expectancy negatif kalır. Loop 105 sonu pozitif değilse Option A şart — mimari değişiklik **bir loop ertelenir**, ama gerekçeli.
2. **Option A frekans düşürür:** Pullback %30-50 oranında oluşmaz → cancel → 0 fill. Frekans %50 düşebilir; CLAUDE.md §12 "30+/h" kuralı sınırda kalır. Mitigation: pendingExpiresAt 5dk; bar interval 1m → her bar yeni emit şansı.
3. **Option A test yükü:** 8+ yeni test, 1 yeni migration, 2 yeni worker. Reviewer + tester yükü artar.
4. **Limit fill realism:** PaperFillSimulator Limit branch "ask ≤ limit ⇒ fill" basit guarantee; mainnet'te queue position + partial fill realism daha karmaşık. Mainnet geçişte tekrar değerlendirilir (testnet'te kabul edilebilir basitlik).
5. **Option B ertelendi — false breakout riski:** Bar close emit "trend devam edecek" varsayar; pattern yanlışsa entry kayıp. Option C+A bunu fix etmez (sadece kayıp büyüklüğünü küçültür). Loop 110+ yeniden bakılır.

### Nötr

1. ADR-0024 PatternComposer altyapısı korunur (BarSnapshot, IPatternDetector, 17 detector dosyası).
2. ADR-0025 Futures Direction-aware mimari korunur (TradeDirection enum, Long/Short symmetric).
3. CQRS sınırı değişmez — Command/Query/Event kontratları aynı.
4. Frontend Vue dashboard değişmez (Option C); Option A'da yeni "Pending Limit" badge eklenir (4h iş).
5. ADR-0006 testnet-first guard, ADR-0008 TradingMode enum, ADR-0011 sizing, ADR-0020 fee accounting — etkilenmez.

---

## Alternatifler

### A. Sadece Option B uygula (Next-Bar Confirmation)

Bar close candidate, sonraki bar trend devam confirmation. **Reddedildi (Loop 105 için):**
- 8+ commit, yeni aggregate (PendingSignal) veya StrategySignal status, multi-bar state.
- ADR-0024 §24.6 "yeni aggregate açma" kararı bozulur — concept duplication.
- Frekans -%50 (yarı candidate discard).
- Loop disiplini (`loop_discipline.md`) "hızlı çözüm" prensibine ters.
- Loop 110+ değerlendirilir (Option C+A bittikten sonra quality booster olarak).

### B. Option A'dan başla, C'yi atla

Direkt Limit order implement. **Reddedildi:**
- 4 commit + 1 migration + 2 worker = ~2 gün iş; halbuki Option C ~30dk + 0 risk.
- Eğer R:R simetri sorunu zaten WR-bazlı değil entry-timing-bazlıysa, Option C başarısız olur ve **kanıtlı** olarak Option A'ya geçilir. Aksi halde Option C tek başına yeterli olabilir; Option A'nın frekans-düşürücü etkisi gereksiz.
- KISS ihlali — en küçük değişiklikten başla.

### C. R:R 1:3 (TP daha agresif)

TpAtrMul 1.8, SlAtrMul 0.6 → R:R 1:3, BE_WR %25. **Reddedildi:**
- Loop 80-104 verisi: %30 WR ile R:R 1:2.5 fiili 1:15 oldu. R:R 1:3 nominal artarsa hit oranı **daha da düşer** — fiili R:R 1:20+ riski.
- TP %0.60+ 5dk içinde nadiren hit olur (volatilite veri).
- ADR-0023 §23.10 "TP %0.60+ 8 dk içinde hit oranı düşük" zaten reddetmişti.

### D. Bar close değil bar mid-point emit

Bar yarısında (2:30 anında 5m bar için) tetikleme. **Reddedildi:**
- KlineClosedEvent kontratı bozulur — yarım bar veri eksik (close henüz yok, indicator değerleri yarım).
- Pattern detection bar tamamlanmadan run edilemez (engulfing 2-bar pattern, donchian 20-bar window).
- WS supervisor + KlineClosedEvent altyapısı tamamen elden geçer (~10 commit).

### E. Sliding window emit (her tick yeniden değerlendir)

Bar close yerine tick-by-tick pattern eval. **Reddedildi:**
- Pattern tanımları bar-bazlı (engulfing = 2-bar; bb-squeeze = 6-bar BBW). Tick-bazlı eval pattern semantiğini bozar.
- Performans yükü 60× artar (bar/dk → tick/sn).
- Mevcut ADR-0024 §24.7 BarSnapshot bar-aligned; refactor maliyeti çok yüksek.

### F. ML model yön + entry timing

LSTM/Transformer entry timing decision. **Reddedildi (şimdilik):**
- ADR-0024 §C ve ADR-0025 §E aynı gerekçeyle reddedilen yaklaşım — veri yetersiz (140 trade total).
- Loop 105-110 manuel parametre tune ile veri biriktir; Loop 120+ değerlendirilir.

---

## Migration Notları

**Loop 105 (Option C):**
1. **0 EF migration** — sadece Strategy seed güncelleme.
2. Strategy seed (Infrastructure/Persistence/Configurations veya ApplicationDbContext.HasData): tüm aktif stratejilerin `ParametersJson` içinde:
   - `TpAtrMultiplier: 0.6` (1.5 → 0.6)
   - `MinTpPct: 0.0012` (0.0040 → 0.0012)
   - `MaxTpPct: 0.003` (0.010 → 0.003)
   - `TpAtrMultiplierLow: 0.5` (1.3 → 0.5)
   - `TpAtrMultiplierHigh: 0.7` (1.8 → 0.7)
   - SL parametreleri **dokunulmaz** (zaten 0.6 / 0.0012 / 0.003).
3. Bot restart sonrası `POST /api/risk/circuit-breaker/reset` (X-Admin-Key) — `reference_circuit_breaker_reset.md`.
4. DB veri silmez — eski Strategy row'ları yeni paramlarla **update** edilir (HasData migration auto-update).
5. Eski Position/Order/Signal verisi **kalır** (audit). Sadece yeni emit'ler yeni paramlarla.

**Loop 106-107 (Option A — Loop 105 başarısızsa):**
1. **1 EF migration** `AddOrderPendingExpiresAt`:
   ```csharp
   migrationBuilder.AddColumn<DateTimeOffset>(
       name: "PendingExpiresAt",
       table: "Orders",
       nullable: true);
   migrationBuilder.CreateIndex(
       name: "IX_Orders_Pending_Limit_Filtered",
       table: "Orders",
       columns: new[] { "Status", "Type", "PendingExpiresAt" },
       filter: "[Status] = 1 AND [Type] = 2");  // New + Limit
   ```
2. Yeni 2 BackgroundService DI registration (Api/Program.cs):
   - `services.AddHostedService<PendingLimitFillWorker>();`
   - `services.AddHostedService<PendingLimitOrderTimeoutWorker>();`
3. PaperFillSimulator Limit branch — `IPaperFillSimulator.SimulateAsync` Limit type için Pending döner (yeni `PaperFillOutcome.Pending` factory).
4. Eski Market path **silinmez** — Limit + Market dual desteklenir (Option A pivot reversibility). Reviewer disipliniyle: hangi path emit edilir log'da görünmeli.

---

## Implementation Order — backend-dev için

### Loop 105 (Option C — 1 commit)

**Commit 1:** `Loop 105 R:R simetri tune — Option C (ADR-0026 §26.6)`
- `src/Infrastructure/Persistence/SeedData/StrategySeedData.cs` (veya HasData lokasyonu) — tüm aktif Strategy'lerin ParametersJson içinde TP multiplier/min/max + tier multiplier güncel.
- `src/Application/Strategies/Patterns/PatternComposerOptions.cs` — default değerleri Loop 105 paramlarına çek (TpAtrMultiplier 0.6, MinTpPct 0.0012, MaxTpPct 0.003, TpAtrMultiplierLow 0.5, TpAtrMultiplierHigh 0.7).
- `appsettings.Development.json` (eğer override varsa) — paralel update.
- Migration **YOK**.
- Test: mevcut composer unit testleri parametre değişikliği ile **kırılır mı?** kontrol; gerekirse expected R:R değerleri güncelle (sayı assertion'lar).
- Commit message:
  ```
  Loop 105 R:R simetri (ADR-0026 §26.6 Option C)

  TpAtrMul 1.5→0.6, MinTp 0.40%→0.12%, MaxTp 1.0%→0.30%
  Tier mult: 0.5/0.6/0.7 (low/mid/high)
  SL parametreleri korundu — R:R 1:1 simetri.

  ADR-0023 R:R 1:2.5 superseded by ADR-0026.
  ```

**Reviewer kontrol:**
- Strategy seed JSON parse'lı (FluentAssertions test).
- ADR-0023 §23.4 tablosu "Superseded by ADR-0026" işaretli.
- 0 deprecated kod (eski 1.5/1.8 multiplier sayı sabitleri yok).

**Tester (Playwright):**
- Loop 105 boot 30dk içinde dashboard:
  - Strategy ParametersJson rendered (TP=0.0012-0.003).
  - ≥3 emit (composer threshold değişmedi, frekans korunmalı).
  - En az 1 close (TP yakın → hit oranı artmalı).
  - Loop 105 4h sonu: ≥10 trade, ≥3 TP hit, net > -$0.50.

### Loop 106-107 (Option A — Loop 105 başarısızsa, 4 commit)

**Commit 2:** `Domain — Order.PlaceLimit + PendingExpiresAt (ADR-0026 §26.7 A.1)`
- `src/Domain/Orders/Order.cs` — `PendingExpiresAt` property + `PlaceLimit(...)` factory + `ExpirePending(...)` behavior.
- `src/Domain/Orders/Events/OrderPendingExpiredEvent.cs` — yeni event (audit).
- Test: `tests/Tests/Domain/Orders/OrderLimitTests.cs` (3 test).

**Commit 3:** `EF Migration — AddOrderPendingExpiresAt (ADR-0026 §26.7 A.1)`
- `src/Infrastructure/Persistence/Configurations/OrderConfiguration.cs` — PendingExpiresAt mapping + filtered index.
- `dotnet ef migrations add AddOrderPendingExpiresAt --project Infrastructure --startup-project Api`.

**Commit 4:** `Application — StrategySignalToOrderHandler Limit branch (ADR-0026 §26.7 A.2)`
- `src/Application/Orders/Commands/PlaceOrder/PlaceOrderCommand.cs` — `DateTimeOffset? PendingExpiresAt = null` field.
- `src/Infrastructure/Strategies/StrategySignalToOrderHandler.cs` — Market yerine Limit + offset hesabı + tick alignment + 5dk pendingExpiresAt.
- Test: 2 yeni handler test (Long limit < bar close, Short limit > bar close).

**Commit 5:** `Infrastructure — PendingLimitFillWorker + TimeoutWorker (ADR-0026 §26.7 A.3+A.4)`
- `src/Infrastructure/Trading/Paper/PendingLimitFillWorker.cs` — tick'te ask/bid cross detection + fill.
- `src/Infrastructure/Trading/PendingLimitOrderTimeoutWorker.cs` — 15sn polling + ExpirePending + IExchangeClient.CancelLiveOrderAsync (LiveTestnet).
- `src/Api/Program.cs` — DI registration (2 hosted service).
- Test: `tests/Tests/Infrastructure/Trading/PendingLimitFillWorkerTests.cs` (2 test).

**Reviewer kontrol noktaları (Option A):**
- `IExchangeClient.CancelLiveOrderAsync` LiveTestnet path'te çağrılıyor (mock test).
- Order PendingExpiresAt null = market davranışı geriye dönük korunur.
- 0 magic number — `LimitOffsetPct = 0.001m` const veya Options'tan oku.
- `OrderStatus.Expired` mevcut enum reuse — yeni status eklenmedi.

**Tester (Playwright, Loop 106 boot):**
- Dashboard'da "Pending Limit" badge görünür.
- 30dk içinde ≥3 emit, ≥1 fill, ≥1 expire (timeout).
- Limit fill price < bar close price (Long için) — screenshot kanıt.

---

## Kaynak

- ADR-0023 (risk-first-tp-sl-asymmetry) — superseded by ADR-0026 §26.6.
- ADR-0024 (pattern-based-scalping) — composer/detector altyapısı korunur.
- ADR-0025 (futures-short-pivot) — TradeDirection + IExchangeClient port korunur.
- ADR-0006 (testnet-first-policy) — mainnet guard korunur.
- Memory `feedback_frekans_kartopu.md` — 30+/h frekans kuralı.
- Memory `feedback_no_dead_code.md` — ADR-0023 §23.4 superseded işaretlenmesi + eski param sabitleri silinir.
- Memory `loop_discipline.md` — kâr olunaya kadar 4h loop disiplini.
- `loops/loop_104/halt-t90.md` — 25 loop pattern özeti, kök sorun "buy at top".
- DDD reference: Vaughn Vernon — *Implementing DDD* §10 (aggregate boundary "transactional consistency"); Order PendingExpiresAt aggregate-internal field, yeni aggregate gerekmez.
- Clean Architecture dependency rule: PendingLimitFillWorker Infrastructure'da, Order behavior Domain'de — yön ihlali yok.
- [Binance Futures API — Limit Order Reference](https://binance-docs.github.io/apidocs/futures/en/#new-order-trade) — fapi/v1/order LIMIT type + GTC timeInForce + cancel endpoint.
- User halt context (2026-05-04 19:37 UTC): "Loop 104 -$1.91 < eşik, 25 loop pattern net, architectural fix gerek".
