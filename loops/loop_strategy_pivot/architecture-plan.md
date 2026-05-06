# Loop Strategy Pivot — Architecture Plan

**Date:** 2026-05-03
**Author:** architect
**Refs:** ADR-0027 (Strategy Pivot), ADR-0024 (paused), ADR-0025 (preserved), ADR-0026 (preserved)
**Branch:** `development` (CLAUDE.md §10 — branch + PR yok)
**Status:** Draft (binance-expert tavsiyesi sonrası §3 spesifikleşir)

---

## 1. Amaç

Loop 112+ için strateji ailesi pivotu mimari planı. ADR-0027 §27.13'ün operasyonel detaylandırması:
- Atomik commit listesi (10-14 commit, aile-bağımsız çekirdek + aile-spesifik 6-10).
- Bağımlılık grafiği (commit'ler arası sıralama).
- Risk haritası (her commit için olası kırılma alanı + mitigation).
- Kod envanteri: silinen / korunan / askıya alınan / yeni eklenen dosyalar.

---

## 2. Kod Envanteri

### 2.1 KORUNAN (silinmez, dokunulmaz veya minimal değişir)

#### Domain
- `src/Domain/Strategies/Strategy.cs` — aggregate root (Type + Status + ParametersJson + EmitSignal)
- `src/Domain/Strategies/StrategySignal.cs` — entity (Direction, SuggestedPrice/Stop/TP, ContextJson)
- `src/Domain/Strategies/StrategyEnums.cs` — sadece **enum extension** (yeni StrategyType değeri eklenir; PatternComposite=3 silinmez)
- `src/Domain/Common/TradeDirection.cs` (ADR-0025) — Long/Short
- `src/Domain/Positions/Position.cs` — Open/Mark/Close/Trailing/BE Direction-aware (ADR-0025 + Loop 111 fix)
- `src/Domain/Orders/Order.cs` — Place/Limit/Cancel/Expire/RegisterFill (ADR-0026 Option A pre-implement edilirse Limit branch genişler)
- `src/Domain/Balances/VirtualBalance.cs` — futures wallet (ADR-0025)
- `src/Domain/Risk/RiskProfile.cs` — Leverage + MaintenanceMarginRatio (ADR-0025)

#### Application
- `src/Application/Strategies/Evaluation/IStrategyEvaluator.cs` — port DEĞİŞMEZ (Aile B Grid seçilirse multi-emit refactor — ADR-0027 §27.10)
- `src/Application/Strategies/Evaluation/StrategyEvaluation.cs` (record) — DEĞİŞMEZ
- `src/Application/Abstractions/Exchange/IExchangeClient.cs` — port DEĞİŞMEZ (Aile D seçilirse `GetFundingRateAsync` method eklenir)
- `src/Application/Strategies/Patterns/IPatternDetector.cs` — DOKUNULMAZ (paused)
- `src/Application/Strategies/Patterns/IPatternSignalComposer.cs` — DOKUNULMAZ (paused)
- `src/Application/Strategies/Patterns/BarSnapshot.cs` — DOKUNULMAZ (paused)
- `src/Application/Strategies/Patterns/PatternComposerOptions.cs` — DOKUNULMAZ (paused)
- `src/Application/Strategies/Patterns/CompositeSignalDecision.cs` — DOKUNULMAZ (paused)
- `src/Application/Strategies/Patterns/PatternEvaluation.cs` — DOKUNULMAZ (paused)
- `src/Application/Strategies/Patterns/IPatternRegistry.cs` — DOKUNULMAZ (paused)

#### Infrastructure
- `src/Infrastructure/Strategies/Evaluators/PatternCompositeEvaluator.cs` — DOKUNULMAZ (paused, DI register kalır)
- `src/Infrastructure/Strategies/Evaluators/Indicators.cs` — **PAYLAŞILIR** (yeni evaluator de import eder; yeni helper method'lar additive eklenebilir)
- `src/Infrastructure/Strategies/Evaluators/EvaluatorParameterHelper.cs` — PAYLAŞILIR
- `src/Infrastructure/Strategies/Patterns/PatternRegistry.cs` — DOKUNULMAZ (paused)
- `src/Infrastructure/Strategies/Patterns/WeightedScorePatternComposer.cs` — DOKUNULMAZ (paused)
- `src/Infrastructure/Strategies/Patterns/Detectors/*.cs` — 17 dosya DOKUNULMAZ (paused)
- `src/Infrastructure/Strategies/StrategyEvaluatorRegistry.cs` — DOKUNULMAZ (zaten plug-in)
- `src/Infrastructure/Strategies/StrategyEvaluationHandler.cs` — DOKUNULMAZ (zaten Active iterate)
- `src/Infrastructure/Strategies/StrategySignalToOrderHandler.cs` — DOKUNULMAZ (Aile B seçilirse multi-signal handle refactor)
- `src/Infrastructure/Binance/BinanceFuturesClient.cs` — DOKUNULMAZ (Aile D seçilirse fundingRate method eklenir)
- `src/Infrastructure/Trading/Paper/FuturesPaperFillSimulator.cs` — DOKUNULMAZ
- `src/Infrastructure/Positions/MarkToMarketWorker.cs` — DOKUNULMAZ (Loop 111 fix sonrası)
- `src/Infrastructure/Trading/PendingLimitOrderTimeoutWorker.cs` (varsa, ADR-0026 Option A) — DOKUNULMAZ

#### Test
- `tests/Tests/Domain/**/*.cs` — DOKUNULMAZ
- `tests/Tests/Application/Strategies/Patterns/*.cs` — DOKUNULMAZ (paused)
- `tests/Tests/Infrastructure/Strategies/Patterns/*.cs` — DOKUNULMAZ (paused)
- `tests/Tests/Infrastructure/Binance/*.cs` — DOKUNULMAZ

### 2.2 ASKIYA ALINAN (Status=Paused, kod silinmez)

| Tablo / Concept | Durum | Geri dönüş |
|---|---|---|
| `Strategies` row Type=3 (PatternComposite) | Status=2 (Paused) | UPDATE Status=3 + bot restart |
| `IPatternDetector` 17 implementation | Code unchanged, DI register kalır | Status flip yeter |
| `WeightedScorePatternComposer` | Code unchanged, DI register kalır | Status flip yeter |
| `PatternCompositeEvaluator` | Code unchanged, registry resolve kalır | Status flip yeter |
| `PatternComposerOptions` defaults | Mevcut Strategy.ParametersJson (re-aktivasyonda kullanılır) | (yok) |

### 2.3 YENİ EKLENEN

#### Çekirdek (4 commit, aile-bağımsız)

| Yeni dosya / change | Konum | Boyut tahmini |
|---|---|---|
| StrategyType enum genişlemesi (1 yeni değer) | `src/Domain/Strategies/StrategyEnums.cs` | +5 satır |
| `Loop112StrategyPivot` migration | `src/Infrastructure/Persistence/Migrations/<ts>_Loop112StrategyPivot.cs` | ~50 satır |
| StrategyEvaluatorRegistry resolve null log | `src/Infrastructure/Strategies/StrategyEvaluatorRegistry.cs` | +5 satır (audit) |
| StrategyList Vue Status badge | `wwwroot/js/components/StrategyList.js` | +20 satır |

#### Aile-spesifik (binance-expert tavsiyesi sonrası — TBD)

`§3 Aile-Spesifik Plan` altında 4 alternatif (A/B/C/D) için detay.

### 2.4 SİLİNEN

**Hiçbir dosya silinmez.** ADR-0027 Karar B (pattern paused, deprecated değil). Loop 130 audit ADR-0030 ile değerlendirilir.

---

## 3. Aile-Spesifik Plan (binance-expert tavsiyesi sonrası 1 alternatif kalır)

### 3.A. Swing Trade (Aile A — düşük komplekslik, 6 commit)

#### A.1 Yeni dosyalar
- `src/Application/Strategies/Swing/SwingTradeOptions.cs` (~30 satır — EmaShortPeriod, EmaLongPeriod, AdxMin, RsiMin, RsiMax, AtrSlMul, RrRatio, MaxHoldHours)
- `src/Infrastructure/Strategies/Evaluators/SwingTradeEvaluator.cs` (~250 satır)
- `tests/Tests/Infrastructure/Strategies/Evaluators/SwingTradeEvaluatorTests.cs` (~200 satır, 6 test)

#### A.2 Mevcut dosya değişimleri
- `src/Domain/Strategies/StrategyEnums.cs` — `SwingTrade=4` ekle
- `src/Api/Program.cs` — `services.AddSingleton<IStrategyEvaluator, SwingTradeEvaluator>()`
- `src/Infrastructure/Persistence/Migrations/<ts>_Loop112StrategyPivot.cs` — 5 yeni Strategy seed (Type=SwingTrade=4)
- `src/Infrastructure/Strategies/Evaluators/Indicators.cs` — opsiyonel EMA50/EMA200 helper additive

#### A.3 Test stratejisi
- Long pullback emit (1)
- Short pullback emit (1)
- No higher TF trend (skip) (1)
- ADX < threshold (skip) (1)
- RSI extremum dışı (skip) (1)
- Cooldown active (skip) (1)

#### A.4 Frekans tahmini
- 5 coin × 3-10 emit/h = 15-50/h. **CLAUDE.md §12 sınırda — kabul.**

#### A.5 Riskler
- 5m bar emit + 24h hold ⇒ pozisyon başına Funding 3 kez ödenir (8h bir). RiskProfile.MaxFundingFeePerHour korunmalı.
- Higher-TF kline (1h) `IExchangeClient.GetKlinesAsync` REST polling — bot boot'ta cache + 1h bir refresh worker gerek (yoksa her 5m emit'te REST çağrısı = rate limit).

### 3.B. Grid Trading (Aile B — orta komplekslik, 10 commit)

#### B.1 Yeni dosyalar
- `src/Domain/Strategies/Grid/GridLevel.cs` (value object: Index, Price, Side, Status)
- `src/Domain/Strategies/Grid/GridState.cs` (sub-aggregate: Levels, BasePrice, StepPct, Cycle)
- `src/Application/Strategies/Grid/GridTradingOptions.cs` (~40 satır)
- `src/Infrastructure/Strategies/Evaluators/GridTradingEvaluator.cs` (~300 satır)
- `src/Infrastructure/Strategies/Grid/GridStateRepository.cs` (~80 satır)
- `src/Infrastructure/Workers/GridStateRebalanceWorker.cs` (~150 satır — range bozulduğunda cancel-all + new grid)
- `tests/Tests/Application/Strategies/Grid/GridTradingEvaluatorTests.cs` (~300 satır, 9 test)
- `tests/Tests/Infrastructure/Workers/GridStateRebalanceWorkerTests.cs` (~150 satır, 3 test)

#### B.2 Mevcut dosya değişimleri (BREAKING)
- **`src/Application/Strategies/Evaluation/IStrategyEvaluator.cs`** — `Task<StrategyEvaluation?>` → `Task<IReadOnlyList<StrategyEvaluation>>`
- `src/Infrastructure/Strategies/Evaluators/PatternCompositeEvaluator.cs` — single → list with 1-or-0 item adapter
- `src/Infrastructure/Strategies/StrategyEvaluationHandler.cs` — list iterate fan-out (her StrategyEvaluation için ayrı StrategySignal emit)
- `src/Infrastructure/Strategies/StrategySignalToOrderHandler.cs` — multi-symbol değil **multi-level**; `clientOrderId` schema `grid-{StrategyId}-{Level}-{Cycle}-{ModeSuffix}`
- `src/Domain/Strategies/Strategy.cs` — `GridStateJson` nullable property
- `src/Infrastructure/Persistence/Configurations/StrategyConfiguration.cs` — GridStateJson mapping
- `src/Domain/Strategies/StrategyEnums.cs` — `GridTrading=5`
- `src/Api/Program.cs` — DI register (Evaluator + Repository + Worker)

#### B.3 Migration
- `<ts>_AddStrategyGridState`: `Strategies.GridStateJson NVARCHAR(MAX) NULL` (additive, backfill null).
- `<ts>_Loop112StrategyPivot`: PatternComposite Paused + 5 GridTrading Strategy seed.

#### B.4 Test stratejisi
- Range tespit emit (3) — düşük volatilite tespit, Mid volatilite skip, yüksek volatilite skip.
- Multi-level emit per cycle (4) — 5 level × Long/Short ayrım.
- Range bozulma cancel-all (2).
- Cycle increment idempotency (2) — aynı cycle'da duplicate emit yok.
- Worker rebalance (3) — range bozuldu → cancel + new grid başlatma.

#### B.5 Frekans tahmini
- 5 coin × 50/h = 250/h. **CLAUDE.md §12 mükemmel uyumlu.**

#### B.6 Riskler
- **Range tespit yanlışsa felaket:** Sahte range'de breakout olur, tüm grid ters yönde dolar, kümülatif zarar büyük (5 level × $0.20 SL = -$1.00 / cycle). Mitigation: Range guard sıkı (24h spread / mid < %1.5), ATR < %0.6 zorunlu.
- **PendingLimitFillWorker yük artışı:** 5 coin × 5 level × 2 direction = 50 simultaneous pending order. DB query optimization gerekli (filtered index `IX_Orders_Pending_Limit_Filtered` zaten ADR-0026'da öneri).
- **IStrategyEvaluator interface refactor diğer evaluator'ları kırar:** PatternCompositeEvaluator + future evaluator'lar adapt etmeli. Test mock'ları toplu güncellenir.

### 3.C. Breakout (Aile C — düşük komplekslik, 6 commit)

#### C.1 Yeni dosyalar
- `src/Application/Strategies/Breakout/BreakoutOptions.cs` (~30 satır)
- `src/Infrastructure/Strategies/Evaluators/BreakoutEvaluator.cs` (~250 satır)
- `tests/Tests/Infrastructure/Strategies/Evaluators/BreakoutEvaluatorTests.cs` (~200 satır, 6 test)

#### C.2 Mevcut dosya değişimleri
- `src/Domain/Strategies/StrategyEnums.cs` — `Breakout=6` ekle
- `src/Api/Program.cs` — DI register
- `src/Infrastructure/Persistence/Migrations/<ts>_Loop112StrategyPivot.cs` — 5 Breakout Strategy seed
- `src/Infrastructure/Strategies/Evaluators/Indicators.cs` — Donchian + ATR expansion helper'ları kullanımda (mevcut, additive değil)

#### C.3 Migration
- 0 schema migration (sadece data seed).

#### C.4 Test stratejisi
- Long Donchian high break + ATR genişleme + volume surge ⇒ emit Long (1)
- Short Donchian low break + simetrik ⇒ emit Short (1)
- Donchian break ama volume düşük (skip) (1)
- Donchian break ama ATR genişlemedi (skip) (1)
- Cooldown active (skip) (1)
- MTF gate (mevcut ADR-0025 kuralı korunur) ters yön ⇒ skip (1)

#### C.5 Frekans tahmini
- 5 coin × 10-20/h = 50-100/h. **CLAUDE.md §12 uyumlu.**

#### C.6 Riskler
- **Sahte breakout (Loop 85-91 zaten yaşandı):** Volume + ATR + MTF triple-gate filtreleyerek. Ama %100 değil. Mitigation: ADR-0026 Option A (Limit pullback) BreakoutEvaluator'da da uygulanabilir — bar close emit yerine limit @ close × (1 - 0.0015) (Long) → entry "retest" yakalar.
- **Pattern subsystem'in alt kümesi:** `DonchianBreakoutDetector` (paused) ile semantik çakışma → reviewer "neden iki yer?" sorgusu. Cevap: Pattern altyapısı multi-detector compose; Breakout standalone single-evaluator. Mimari ayrı, semantik benzerlik tesadüfi.

### 3.D. Arbitrage (Aile D — yüksek komplekslik, 14 commit)

#### D.1 Yeni dosyalar
- `src/Domain/MarketData/FundingRateSnapshot.cs` (entity)
- `src/Application/Abstractions/Exchange/FundingRateDto.cs` (record)
- `src/Application/Strategies/Arbitrage/ArbitrageOptions.cs` (~40 satır)
- `src/Infrastructure/Strategies/Evaluators/ArbitrageEvaluator.cs` (~350 satır)
- `src/Infrastructure/Workers/FuturesFundingRateWorker.cs` (~150 satır)
- `tests/Tests/Application/Strategies/Arbitrage/ArbitrageEvaluatorTests.cs` (~300 satır, 8 test)
- `tests/Tests/Infrastructure/Workers/FuturesFundingRateWorkerTests.cs` (~150 satır, 4 test)

#### D.2 Mevcut dosya değişimleri (BREAKING)
- **`src/Application/Abstractions/Exchange/IExchangeClient.cs`** — yeni method `GetFundingRateAsync(symbol, ct)`
- `src/Infrastructure/Binance/BinanceFuturesClient.cs` — `/fapi/v1/fundingRate` impl
- `src/Domain/Strategies/Strategy.cs` — `SecondarySymbol` nullable property (paired trade için)
- `src/Infrastructure/Persistence/Configurations/StrategyConfiguration.cs` — SecondarySymbol mapping
- `src/Infrastructure/Strategies/StrategyEvaluationHandler.cs` — multi-symbol StrategyEvaluation (paired trade) handle
- `src/Infrastructure/Strategies/StrategySignalToOrderHandler.cs` — paired symbol fan-out (iki simultaneous order, atomic)
- `src/Domain/Strategies/StrategyEnums.cs` — `Arbitrage=7`
- `src/Api/Program.cs` — DI register

#### D.3 Migration
- `<ts>_AddFundingRateSnapshots`: yeni tablo (Id, Symbol, Rate, Timestamp).
- `<ts>_AddStrategySecondarySymbol`: `Strategies.SecondarySymbol NVARCHAR(20) NULL`.
- `<ts>_Loop112StrategyPivot`: PatternComposite Paused + 5 Arbitrage Strategy seed.

#### D.4 Test stratejisi
- Funding rate threshold + ⇒ Short emit (1)
- Funding rate threshold − ⇒ Long emit (1)
- Funding rate aralık içinde (skip) (1)
- Cross-pair correlation > %95 + spread > %0.5 ⇒ paired trade (Long A + Short B) (2)
- Spread normal aralıkta (skip) (1)
- Worker 8h funding poll (1)
- Worker rate limit guard (1)
- Worker resilience reconnect (1)

#### D.5 Frekans tahmini
- 5 coin × 5-15/h = 25-75/h. **CLAUDE.md §12 sınırda.**

#### D.6 Riskler
- **Strategy aggregate "1 strategy = 1 symbol" kuralı bozulur:** `SecondarySymbol` paired trade için gerekli; aggregate boundary tartışması (DDD review gerekir).
- **Funding rate worker güvenilirliği:** 8h bir polling — kayma olmamalı. ADR-0002 BinanceWsSupervisor pattern kullanılır.
- **Mainnet'e geçişte funding rate volatilitesi:** Testnet funding rate sabit/sentetik; mainnet gerçek piyasa. PaperFillSimulator funding fee accounting için ADR-0025 §25.16 öngördü ama implement edilmedi — bu commit'te entegre.
- **Kompleksite > değer:** 14 commit + interface refactor + aggregate refactor. Loop 112 için **çok ağır**. Loop 120+ ileriye atılması daha uygun.

---

## 4. Bağımlılık Grafiği

### 4.1 Çekirdek Commit Sırası (aile-bağımsız, 4 commit)

```
[Commit 1] Domain — StrategyType enum genişlemesi
              │
              ▼
[Commit 2] Migration — Loop112StrategyPivot
              │ (depends on Commit 1: enum değer)
              ▼
[Commit 3] Application — Registry resolve null log + Handler audit
              │ (independent — paralel olabilir Commit 1 sonrası)
              ▼
[Commit 4] Frontend — Vue Status badge
              │ (independent — paralel)
              ▼
[ÇEKİRDEK BİTTİ — bot Active Strategy=0, emit yok]
```

**Kritik nota:** Çekirdek tek başına bot'u "0 emit" durumuna sokar (PatternComposite Paused + henüz NewType evaluator yok). Bu **kabul edilemez** (CLAUDE.md §12 0 emit > 1h pivot kuralı). **Çekirdek + aile-spesifik tek bir loop deploy'unda yapılmalı**, ara state'te bot kapalı veya feature-flag.

### 4.2 Aile-Spesifik Commit Sırası

#### Aile A (Swing) — 6 commit linear

```
[5] SwingTradeOptions
   ▼
[6] Indicators.cs EMA50/EMA200 helper additive (paralel mümkün)
   ▼
[7] SwingTradeEvaluator higher-TF trend resolution
   ▼
[8] SwingTradeEvaluator pullback entry + geometry
   ▼
[9] SwingTradeEvaluatorTests (6 test)
   ▼
[10] DI register Program.cs
```

#### Aile B (Grid) — 10 commit, BREAKING refactor

```
[5] Domain — GridLevel VO + GridState
   ▼
[6] Migration — AddStrategyGridState
   ▼
[7] BREAKING — IStrategyEvaluator multi-emit refactor (TÜM evaluator + handler kırılır)
   ▼
[8] PatternCompositeEvaluator adapt single → list (1 item)
   ▼ (paralel)
[9] StrategySignalToOrderHandler multi-level fan-out
   ▼
[10] GridTradingOptions + GridTradingEvaluator
   ▼
[11] Application range detect + multi-level emit
   ▼
[12] Infrastructure GridStateRepository + idempotency
   ▼
[13] Test (range + multi-level + cancel-all)
   ▼
[14] Worker GridStateRebalanceWorker + DI
```

**Kritik:** Commit 7 (interface refactor) ve Commit 8 (PatternComposite adapt) **aynı PR/commit'te birlikte** atılmalı — yoksa build kırılır.

#### Aile C (Breakout) — 6 commit linear

```
[5] BreakoutOptions
   ▼
[6] Indicators.cs Donchian + ATR helper (mevcut, audit)
   ▼
[7] BreakoutEvaluator entry + volume surge
   ▼
[8] BreakoutEvaluator geometry (R:R 1:1.5, MaxHold 4h)
   ▼
[9] BreakoutEvaluatorTests (6 test)
   ▼
[10] DI register + seed Strategy
```

#### Aile D (Arbitrage) — 14 commit, çoklu BREAKING

```
[5] Domain — FundingRateSnapshot entity
   ▼
[6] Migration — AddFundingRateSnapshots
   ▼
[7] BREAKING — IExchangeClient.GetFundingRateAsync method
   ▼
[8] BinanceFuturesClient impl /fapi/v1/fundingRate
   ▼
[9] Worker FuturesFundingRateWorker
   ▼
[10] BREAKING — Strategy.SecondarySymbol nullable
   ▼
[11] Migration — AddStrategySecondarySymbol
   ▼
[12] ArbitrageOptions + ArbitrageEvaluator
   ▼
[13] StrategyEvaluationHandler paired symbol handle
   ▼
[14] StrategySignalToOrderHandler paired fan-out atomic
   ▼
[15] ArbitrageEvaluatorTests (8 test)
   ▼
[16] WorkerTests (4 test)
   ▼
[17] DI register + seed
   ▼
[18] Tester Playwright + reviewer kontrol
```

---

## 5. Risk Haritası

### 5.1 Çekirdek Riskleri

| # | Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|---|
| C1 | Çekirdek deploy + aile-spesifik gecikirse 0 emit > 1h | Orta | Yüksek | Çekirdek + aile-spesifik tek deploy'da; veya feature flag (NewType strategy seed Status=Draft, son commit'te Active'e flip) |
| C2 | StrategyType enum reuse audit (1 ve 2 silinmiş; 3 PatternComposite) | Düşük | Düşük | Yeni değer 4 veya sonrası; ordinal monoton (CLAUDE.md §13 deprecated yasağına saygı) |
| C3 | Migration `Loop112StrategyPivot` UPDATE sırası bozulursa Strategies hatası | Düşük | Orta | `WHERE Type = 3` guard + Idempotent INSERT (`WHERE NOT EXISTS`) |
| C4 | Vue Status badge data-binding kırılırsa dashboard render bozulur | Düşük | Düşük | Tester Playwright zorunlu (CLAUDE.md §9) |

### 5.2 Aile A Riskleri

| # | Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|---|
| A1 | Frekans 30/h altı kalır (3-10/h × 5 coin sınırda) | Orta | Yüksek | Loop 112 t60 check; eğer <20/h → A2 ekle (lower threshold) veya ek 2-3 coin paralel |
| A2 | Higher-TF kline REST polling rate limit (1m × 5 coin = 300 req/h) | Düşük | Orta | 1h kline cache + 1h refresh worker; mevcut KlineSubscriber 1h interval ekleme |
| A3 | 24h hold × 3 funding payment per position (8h cycle) | Orta | Düşük | RiskProfile.MaxFundingFeePerHour korunmalı; ADR-0025 §25.13 default 0.001 |
| A4 | MTF trend 1h slow → emit yön yanlış (mean revert) | Düşük | Orta | RSI extremum + ADX filtreler, ADR-0026 R:R 1:1 simetri kayıp limitler |

### 5.3 Aile B Riskleri

| # | Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|---|
| B1 | Sahte range tespiti → tüm grid breakout'ta dolar (felaket) | Yüksek | Yüksek | Range guard sıkı; ATR < %0.6 + 24h spread/mid < %1.5; GridStateRebalanceWorker breakout tespit ederse cancel-all |
| B2 | IStrategyEvaluator multi-emit refactor build break | Yüksek | Yüksek | Commit 7+8 atomic; tüm evaluator + test mock'lar tek commit'te; reviewer-csharp-architecture skill ile audit |
| B3 | 50 simultaneous pending order DB query yük | Orta | Orta | Filtered index `IX_Orders_Pending_Limit_Filtered` (ADR-0026 öneri); PendingLimitFillWorker batch query (50 row tek scan) |
| B4 | Multi-level idempotency clientOrderId collision | Düşük | Yüksek | Schema `grid-{StrategyId}-{Level}-{Cycle}-{ModeSuffix}` benzersiz; cycle int monoton; EF unique constraint |
| B5 | Cycle increment race condition (worker + handler aynı anda) | Düşük | Orta | Strategy aggregate domain event `GridCycleAdvanced`; cycle değişimi aggregate-internal, transaction-scoped |

### 5.4 Aile C Riskleri

| # | Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|---|
| C1 | Sahte breakout (Loop 85-91 pattern) | Yüksek | Yüksek | Volume + ATR + MTF triple-gate; ADR-0026 Option A Limit pullback entry |
| C2 | DonchianBreakoutDetector (paused) ile semantik çakışma reviewer sorgusu | Düşük | Düşük | ADR-0027 §27.11 dokümante (alt küme açıklaması); kod ayrılığı net |
| C3 | Breakout-only frekans yetersiz (volatilite olmayan dönemler) | Orta | Orta | 5 coin paralel; ATR genişleme şartı dinamik (geçmiş 10 bar ortalaması) |

### 5.5 Aile D Riskleri

| # | Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|---|
| D1 | Strategy aggregate paired symbol kuralı bozulur | Yüksek | Yüksek | DDD review architect skill; aggregate boundary tartışması; SecondarySymbol nullable additive (mevcut single-symbol davranışı backward compat) |
| D2 | Funding rate worker 8h interval kayma | Düşük | Orta | ADR-0002 supervisor pattern; cron-style trigger; missed-cycle warn |
| D3 | Testnet funding rate sentetik → mainnet'e geçişte realism crash | Yüksek | Yüksek | Loop 120+ erteleme önerisi (ADR-0027 §27.12 alt notu) |
| D4 | Paired symbol atomic order — bir bacak fail olursa diğer bacak orphan | Yüksek | Yüksek | `StrategySignalToOrderHandler` saga pattern (commit/rollback); test coverage zorunlu |
| D5 | Komplekslik (14+ commit) Loop 112 timeline dışı | Yüksek | Yüksek | binance-expert tavsiyesinde Aile D **en son seçenek** olarak işaretlenmeli |

---

## 6. Reviewer Kontrol Listesi (her commit sonrası)

### 6.1 Tüm commit'ler için
- [ ] CLAUDE.md §3 SOLID/DRY/KISS uyumu (reviewer-csharp-architecture skill)
- [ ] Lazy loading yok (CLAUDE.md §3 zorunluluk 4)
- [ ] Result<T> pattern (CLAUDE.md §3 zorunluluk 5)
- [ ] Repository-per-aggregate (CLAUDE.md §3 zorunluluk 6)
- [ ] Anemic model yok (CLAUDE.md §3)
- [ ] Magic string/number yok (CLAUDE.md src/CLAUDE.md yasaklar)
- [ ] async/await + CancellationToken (CLAUDE.md src/CLAUDE.md kural 9)
- [ ] Secret commit yok (CLAUDE.md §3 zorunluluk 2)

### 6.2 ADR-0027 spesifik
- [ ] Pattern altyapısı 17 detector dosyası **dokunulmadı** (grep `IPatternDetector` impl count = 17)
- [ ] PatternCompositeEvaluator DI register **kalır**
- [ ] StrategyType enum yeni değer ordinal monoton (CLAUDE.md §13)
- [ ] Migration veri silmez (`DELETE FROM` yok; sadece UPDATE Status + INSERT yeni Strategy)
- [ ] Yeni evaluator Application'da port impl, Infrastructure'da concrete (Clean Architecture dependency rule)
- [ ] Yeni evaluator IPatternDetector veya BarSnapshot import etmez (paused subsystem'e bağımlılık yok)
- [ ] StrategyEvaluatorRegistry resolve count = 2 (PatternComposite + NewType)
- [ ] CLAUDE.md §10 branch+PR yok — `development` branch'a doğrudan commit + push

### 6.3 Aile B Grid spesifik
- [ ] IStrategyEvaluator interface refactor **atomic commit** (build break yok)
- [ ] PatternCompositeEvaluator adapt edildi (single → list with 1 item)
- [ ] clientOrderId schema `grid-{StrategyId}-{Level}-{Cycle}-{ModeSuffix}` benzersiz
- [ ] Filtered index `IX_Orders_Pending_Limit_Filtered` mevcut (ADR-0026)

### 6.4 Aile D Arbitrage spesifik
- [ ] Strategy.SecondarySymbol nullable additive (backward compat)
- [ ] Saga pattern paired order (rollback test coverage)
- [ ] FundingRateSnapshot aggregate boundary doğru (architect-ddd-review skill)

---

## 7. Tester (Playwright) Done-Definition (her aile)

Loop 112 boot 30dk içinde:

### 7.1 Çekirdek done
- [ ] Dashboard "Strategies" 2 grup (Paused PatternComposite gri + Active NewType yeşil)
- [ ] StrategyList Vue render 0 console error
- [ ] DB Strategies row count = 6 (1 paused + 5 active)

### 7.2 Aile A Swing
- [ ] ≥3 emit 30dk içinde (NewType=4)
- [ ] StrategySignal.ContextJson `"type": "swing-trade"` field'ı dolu
- [ ] En az 1 fill, en az 1 close (TP veya SL)
- [ ] Position MaxHoldDuration ~24h (Loop 111 fix sonrası null değil)

### 7.3 Aile B Grid
- [ ] ≥10 emit 30dk içinde (multi-level)
- [ ] Aynı StrategyId × farklı Level → ayrı StrategySignal
- [ ] DB Orders status=New + Type=Limit count >0 (pending levels)
- [ ] Range bozulma simulation → cancel-all worker çalışır (logs)

### 7.4 Aile C Breakout
- [ ] ≥5 emit 30dk içinde
- [ ] Volume surge + ATR genişleme audit log payload
- [ ] En az 1 Long + 1 Short emit (simetrik)

### 7.5 Aile D Arbitrage
- [ ] FundingRateSnapshot table insert count >0 (worker çalıştı)
- [ ] Paired StrategySignal (Symbol + SecondarySymbol) emit
- [ ] Saga rollback test pass (manuel fail injection)

### 7.6 Genel (tüm aileler)
- [ ] 0 unhandled exception (logs)
- [ ] ADR-0006 mainnet guard ihlali yok (`AllowMainnet=false`)
- [ ] CB Healthy (POST /api/risk/circuit-breaker/reset gerekirse)
- [ ] Pattern Composite (Paused) emit count = 0

---

## 8. Geri Dönüş Planı (Pivot Başarısız Olursa)

### 8.1 Loop 112 t60 başarı kriteri
- ≥15 emit, ≥3 close, net realized > -$1.00, 0 unhandled exception.

### 8.2 Loop 112 t240 (4h sonu)
- ≥30 emit, WR ≥ %40, net realized > -$2.00.

### 8.3 Başarısızsa geri dönüş prosedürü
1. Bot durdur.
2. SQL: `UPDATE Strategies SET Status = 3 WHERE Type = 3; UPDATE Strategies SET Status = 2 WHERE Type = <NewType>;`
3. Bot restart + CB reset.
4. PM checkpoint: "Pivot başarısız, pattern composite re-aktive". Bu durum kullanıcıya raporlanır.
5. Yeni ADR (ADR-0028) tetiklenir — başarısızlık postmortem + alternatif aile seçimi.

### 8.4 30 loop boyunca pivot başarılıysa (kümülatif net pozitif)
- ADR-0030 hazırlanır: PatternComposite altyapısı silinir (deprecated kod yasağı CLAUDE.md §13).
- 17 detector + composer + evaluator + 60+ test silme commit'i.
- Loop 130+ tamamen yeni evaluator üzerinde devam.

---

## 9. binance-expert Tavsiyesi Bekleme Çubuğu

binance-expert'in 4 aile arasından (A/B/C/D) **1 aile** önerisi geldiğinde:

1. ADR-0027 §27.7 Karar C "**SEÇİLDİ**" işaretlenir.
2. Diğer 3 aile §Alternatifler altına taşınır.
3. Bu architecture-plan.md §3 ilgili aile alt başlığı **kalır**, diğerleri "(seçilmedi)" notuyla korunur (audit).
4. Backend-dev handoff: "Aile X için Commit 1-N implement et."

**Architect tahmini (binance-expert tavsiyesi gelmeden):**
- Aile **C (Breakout)** en yüksek olasılıkla seçilir: düşük komplekslik (6 commit), pattern altyapısının doğal devamı, frekans uyumlu, kompozit risk düşük.
- Aile A (Swing) ikinci olasılık: en düşük commit ama frekans sınırı.
- Aile B (Grid) üçüncü: yüksek frekans avantajı ama range guard riski yüksek.
- Aile D (Arbitrage) Loop 120+ ertelenir.

Bu tahmin **kararı bağlamaz**; binance-expert tavsiyesi nihai.

---

## 10. Onay & İmza

- architect: bu plan ADR-0027'ye uyumlu (✓)
- binance-expert: TBD (tavsiye bekleniyor)
- backend-dev: handoff sonrası (binance-expert tavsiyesi sonrası)
- reviewer: her commit sonrası
- tester: §7 done-definition zorunlu

**Logging kontratı:** ADR-0027 ve bu plan üretildiğinde MCP `agent-bus.append_decision` (agent_id="architect", task_id="strategy_pivot_adr") çağrılır.
