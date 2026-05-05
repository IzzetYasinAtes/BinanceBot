# Loop 105 — Architecture Plan

ADR-0026 referans: `docs/adr/0026-entry-timing-fix.md`

Karar özeti: **C → A → B sırası.** Loop 105 sadece Option C (parametre tune, kod yok). Loop 106-107 Option A (eğer 105 başarısız). Option B (Loop 110+) şimdilik reddedildi.

---

## Loop 105 — Option C (1 commit)

### Commit 1: `Loop 105 R:R simetri tune — Option C (ADR-0026 §26.6)`

**Değişen dosyalar:**
- `src/Application/Strategies/Patterns/PatternComposerOptions.cs` — default değerleri Loop 105'e çek.
- `src/Infrastructure/Persistence/Configurations/StrategyConfiguration.cs` veya `SeedData/StrategySeedData.cs` — tüm aktif Strategy'lerin `ParametersJson` güncel (TP simetri).
- `appsettings.Development.json` (varsa override) — paralel update.
- (opsiyonel) `tests/Tests/Application/Strategies/Patterns/WeightedScorePatternComposerTests.cs` — geometri tier expected değer assertion'lar yeni R:R'a hizalanır.

**Parametre matrisi:**
| Parametre | Loop 104 | Loop 105 |
|---|---|---|
| TpAtrMultiplier | 1.5 | **0.6** |
| MinTpPct | 0.0040 | **0.0012** |
| MaxTpPct | 0.010 | **0.003** |
| TpAtrMultiplierLow | 1.3 | **0.5** |
| TpAtrMultiplierHigh | 1.8 | **0.7** |
| SlAtrMultiplier | 0.6 | 0.6 (korunur) |
| MinSlPct | 0.0012 | 0.0012 (korunur) |
| MaxSlPct | 0.003 | 0.003 (korunur) |
| SlAtrMultiplierLow | 0.7 | 0.7 (korunur) |
| SlAtrMultiplierHigh | 0.5 | 0.5 (korunur) |

**Risk:** Düşük — sadece JSON parametre + Options default. Geri dönüş 1 commit revert. Migration yok.

**Boot sonrası:**
1. Bot restart.
2. `POST /api/risk/circuit-breaker/reset` (X-Admin-Key) — `reference_circuit_breaker_reset.md`.
3. 4h loop, halt eşiği -$0.50.

**Başarı kriteri:** ≥10 trade, ≥3 TP hit, net > -$0.50, WR ≥ %45.

---

## Loop 106-107 — Option A (4 commit, sadece Loop 105 başarısız ise)

### Bağımlılık grafiği

```
Commit 2 (Domain Order.PlaceLimit)
        │
        ▼
Commit 3 (EF Migration AddOrderPendingExpiresAt)
        │
        ▼
Commit 4 (StrategySignalToOrderHandler Limit branch)
        │
        ▼
Commit 5 (PendingLimitFillWorker + TimeoutWorker + DI)
```

Sıralı zorunlu — Commit 2 olmadan Commit 3 EF schema bilgisi yok; Commit 4 olmadan Commit 5'in test ettiği path tetiklenmez.

### Commit 2: `Domain — Order.PlaceLimit + PendingExpiresAt`

**Yeni:**
- `Order.PendingExpiresAt : DateTimeOffset?` field.
- `Order.PlaceLimit(...)` factory (Order.Place'i delege eder, sonra PendingExpiresAt set).
- `Order.ExpirePending(now)` behavior — Status=New ise Expired'a geçir + event raise.
- `Domain/Orders/Events/OrderPendingExpiredEvent.cs` — yeni event.

**Test:** 4 unit test (PlaceLimit valid, PlaceLimit invalid pendingExpiresAt geçmiş, ExpirePending happy path, ExpirePending PartialFilled durumda guard).

**Risk:** Düşük — domain saf, dış bağımlılık yok.

### Commit 3: `EF Migration — AddOrderPendingExpiresAt`

```sql
ALTER TABLE Orders ADD PendingExpiresAt DATETIMEOFFSET NULL;
CREATE INDEX IX_Orders_Pending_Limit_Filtered ON Orders (Status, Type, PendingExpiresAt)
  WHERE Status = 1 AND Type = 2;
```

**Backfill:** Eski Market orderlar `PendingExpiresAt = NULL` (default).

**Test:** Migration up/down idempotent (Tests/Infrastructure/Persistence/MigrationTests.cs ekle).

**Risk:** Orta — schema breaking ama additive (NULL default). Mevcut testler etkilenmez.

### Commit 4: `Application — StrategySignalToOrderHandler Limit branch`

**Değişen:**
- `PlaceOrderCommand` record — `DateTimeOffset? PendingExpiresAt = null` field eklenir.
- `PlaceOrderCommandHandler.Handle` — `Order.PlaceLimit` factory'sini Type=Limit + PendingExpiresAt!=null durumunda çağır.
- `StrategySignalToOrderHandler.Handle` — Long: limit = barClose × 0.999; Short: limit = barClose × 1.001; tick alignment; 5dk pendingExpiresAt; PlaceOrderCommand Type=Limit, Tif=Gtc.
- Const: `LimitOffsetPct = 0.001m` (PatternComposerOptions'a opsiyonel ekle).

**Test:** 2 yeni handler test (Long, Short limit fiyat doğrulaması) + 1 PlaceOrderCommand test (Limit + PendingExpiresAt path).

**Risk:** Orta — handler core logic değişir, mevcut Market path **korunur** (PendingExpiresAt null = Market). Backward compatible.

### Commit 5: `Infrastructure — PendingLimitFillWorker + PendingLimitOrderTimeoutWorker`

**Yeni:**
- `Infrastructure/Trading/Paper/PendingLimitFillWorker.cs` — BackgroundService, 1sn polling, BookTicker ask/bid Limit fiyatını kestiyse Order.RegisterFill + VirtualBalance margin akış.
- `Infrastructure/Trading/PendingLimitOrderTimeoutWorker.cs` — BackgroundService, 15sn polling, PendingExpiresAt < now ise Order.ExpirePending + Paper'da DB-only / LiveTestnet'te IExchangeClient.CancelLiveOrderAsync.
- `Api/Program.cs` — `services.AddHostedService<PendingLimitFillWorker>(); services.AddHostedService<PendingLimitOrderTimeoutWorker>();`.
- `Infrastructure/Trading/Paper/FuturesPaperFillSimulator.cs` — Limit branch: `OrderType.Limit` ise Pending döner (yeni `PaperFillOutcome.Pending(cid)` factory).

**Test:** 4 yeni test (cross detection Long+Short, timeout expire Paper+LiveTestnet).

**Risk:** Yüksek — yeni concurrent worker, race condition riski (aynı tick'te fill + timeout aynı order). Mitigation: DB transaction, Status guard (`if (order.Status != OrderStatus.New) return;`).

---

## Risk Haritası

| Risk | Olasılık | Etki | Mitigation |
|---|---|---|---|
| Loop 105 Option C tek başına yetmez | Orta | Düşük (1 loop kaybı) | Loop 106 Option A devreye girer; ADR-0026 §26.5 sıralama gerekçeli. |
| Option C frekans yan etkisi | Düşük | Düşük | Composer threshold değişmedi, cooldown aynı; sadece TP kapısı yakın → hit oranı artmalı. |
| Option A frekans -%50 | Yüksek | Orta | 5dk pendingExpiresAt kısa; bar interval 1m → her bar yeni emit şansı. CLAUDE.md §12 "0 emit > 1h" guard hâlâ aktif. |
| Option A worker race condition | Düşük | Yüksek | Order.Status guard her behavior'da; DB transaction; reviewer test coverage zorunlu. |
| Limit fill realism testnet/mainnet farkı | Orta | Düşük | Testnet'te Limit fill simple ask cross; mainnet geçişte queue position re-değerlendirme (ADR'de not). |
| ADR-0023 superseded işaretleme unutulur | Düşük | Düşük | Reviewer checklist: ADR-0023 status header güncel mi? |
| Strategy seed migration çakışması | Düşük | Orta | HasData EF Core auto-update; mevcut row'lar Update statement ile değişir, drop-recreate yok. |

---

## Reviewer Checklist

**Loop 105 (Option C):**
- [ ] ADR-0023 §23.4 tablosu "Superseded by ADR-0026 §26.6" işaretli.
- [ ] PatternComposerOptions defaultları yeni R:R 1:1 simetri.
- [ ] Strategy seed JSON parse'lı (test).
- [ ] Mevcut composer testleri yeni paramlarla geçiyor (assertion update).
- [ ] 0 deprecated kod (eski 1.5/1.8 multiplier sabitleri yok).
- [ ] CLAUDE.md §12 frekans kuralı korundu (composer threshold değişmedi).

**Loop 106-107 (Option A):**
- [ ] `Order.PendingExpiresAt` aggregate-internal, dış mutation yok.
- [ ] `OrderType.Limit` reuse (yeni enum eklenmedi).
- [ ] `OrderStatus.Expired` reuse (yeni status eklenmedi).
- [ ] PlaceOrderCommand backward compatible (PendingExpiresAt null = Market).
- [ ] IExchangeClient.CancelLiveOrderAsync LiveTestnet path'te çağrılıyor (mock test).
- [ ] PendingLimitFillWorker + TimeoutWorker DI registered.
- [ ] Migration filtered index doğru (`Status=1 AND Type=2`).
- [ ] 0 magic number — LimitOffsetPct const veya Options.

**Tester (Playwright):**

Loop 105 boot 30dk:
- [ ] Dashboard Strategy ParametersJson TP=0.0012-0.003 görünüyor.
- [ ] ≥3 emit (frekans korunmuş).
- [ ] ≥1 close.

Loop 106 boot 30dk (eğer Option A devreye girerse):
- [ ] "Pending Limit" badge dashboard'da.
- [ ] ≥3 emit, ≥1 fill, ≥1 expire (timeout).
- [ ] Long limit fill price < bar close price (screenshot kanıt).

---

## Loop Disiplini Akış

```
Loop 105 boot (Option C tune)
        │
        ├─ 4h sonu: net > -$0.50 + ≥3 TP hit?
        │       │
        │       ▼ EVET
        │     Loop 106 (Option C kalıcı, devam)
        │       │
        │       ▼
        │     Pozitif streak doğrulandı mı?
        │       │
        │       ├─ EVET → Option A optional quality booster
        │       └─ HAYIR → Option C tune devam
        │
        └─ HAYIR (4h sonu negatif)
                │
                ▼
              Loop 106 (Option A implement, 4 commit)
                │
                ▼
              4h sonu: net pozitif?
                │
                ├─ EVET → Loop 107+ devam, Option B Loop 110'a ertelenir
                └─ HAYIR → Loop 107 Option A tune (offsetPct, timeout)
                            ├─ Hâlâ negatif → Loop 110 Option B değerlendirilir
                            └─ Pozitif → devam
```

`loop_discipline.md` "kâr olunaya kadar devam" — her 4h checkpoint'te halt eşiği kontrol; bozuk → hemen halt + fix + yeni loop.
