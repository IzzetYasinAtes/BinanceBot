# Loop 92 — Architecture Plan: Spot → Futures Pivot

Date: 2026-05-03
Author: architect
ADR Ref: [`docs/adr/0025-futures-short-pivot.md`](../../docs/adr/0025-futures-short-pivot.md)
Status: Ready for backend-dev pickup
Memory ref: `trading_vision.md`, `feedback_frekans_kartopu.md`, `feedback_no_dead_code.md`

---

## 1. Plan Özeti

Loop 91 sonu Paper $500 reset edilmiş, Spot Testnet long-only mimaride 12 loop boyunca −$17.04 net zarar. Loop 92 itibarıyla **radikal pivot**: Binance USDT-M Futures Testnet üzerine geçiş + Long+Short simetrik emit. Spot kodu CLAUDE.md §13 gereği tamamen silinir. ADR-0024 PatternComposite altyapısı korunur, sadece detector seti çiftlenir ve composer Direction-aware yapılır.

**Bütçe:** 14 atomik commit, ~2-3 saat backend-dev + 30 dk reviewer + 30 dk tester. Tek `development` branch (CLAUDE.md §10).

---

## 2. Commit Sırası (14 Atomik Adım)

### Commit 1 — Domain: TradeDirection enum + Position/StrategySignal Direction

**Dosyalar:**
- `D:/repos/BinanceBot/src/Domain/Common/TradeDirection.cs` (yeni)
- `D:/repos/BinanceBot/src/Domain/Strategies/StrategySignal.cs` (Direction property + Emit factory parametre)
- `D:/repos/BinanceBot/src/Domain/Positions/Position.cs` (Side → Direction rename, PositionSide value object silinir, MarkToMarket/Close/MoveStopToBreakEven/UpdatePeakAndCheckTrailing Direction-aware; PeakMarkPrice → ExtremeMarkPrice rename)
- `D:/repos/BinanceBot/src/Domain/Positions/Events/*.cs` (event payload Direction'a güncel)
- `D:/repos/BinanceBot/src/Domain/ValueObjects/PositionSide.cs` **SİL**
- `D:/repos/BinanceBot/tests/Tests/Domain/Positions/PositionShortTests.cs` (yeni — 8 test)
- `D:/repos/BinanceBot/tests/Tests/Domain/Positions/PositionTests.cs` (Direction=Long parametre eklenir)

**Done:** `dotnet test tests/Tests` 0 fail. Position aggregate Long ve Short MarkToMarket/Close PnL formülü test geçer.

**Rollback:** Migration 2 öncesi commit revert. PositionSide value object schema'ya henüz dokunulmadığı için domain-only.

---

### Commit 2 — EF Migration: AddTradeDirectionToSignalAndPosition

**Dosyalar:**
- `D:/repos/BinanceBot/src/Infrastructure/Persistence/Configurations/PositionConfiguration.cs` (Direction kolon, Side mapping silinir, IX_Positions_Mode_Status_Direction)
- `D:/repos/BinanceBot/src/Infrastructure/Persistence/Configurations/StrategySignalConfiguration.cs` (Direction kolon)
- `D:/repos/BinanceBot/src/Infrastructure/Persistence/Migrations/<timestamp>_AddTradeDirectionToSignalAndPosition.cs` (auto)

**Komut:**
```powershell
dotnet ef migrations add AddTradeDirectionToSignalAndPosition `
  --project src/Infrastructure --startup-project src/Api
```

**Done:** Migration script `Up()` Direction kolonu DEFAULT 1 ile ekler, mevcut satırları backfill eder. `dotnet ef database update` smoke test geçer.

**Rollback:** `dotnet ef migrations remove`. Migration henüz prod DB'ye uygulanmamışsa risk yok; uygulanmışsa `dotnet ef database update <previous-migration>` ile geri sar.

---

### Commit 3 — Application: IExchangeClient port refactor

**Dosyalar:**
- `D:/repos/BinanceBot/src/Application/Abstractions/Exchange/IExchangeClient.cs` (yeni — 8 method, ExchangeAccountInfo/ExchangePositionDto records)
- `D:/repos/BinanceBot/src/Application/Abstractions/Exchange/MarginType.cs` (yeni enum: Isolated/Crossed)
- `D:/repos/BinanceBot/src/Application/Abstractions/Binance/IBinanceTrading.cs` **SİL**
- `D:/repos/BinanceBot/src/Application/Abstractions/Binance/BinanceDtos.cs` (PlaceOrderRequest'e Direction parametresi)
- Tüm `IBinanceTrading` reference'ları `IExchangeClient`'a refactor (PlaceOrderCommand handler, FillOrderHandler vs.)

**Done:** Solution `dotnet build` 0 hata. `IBinanceTrading` referansı `grep -r "IBinanceTrading"` → 0 sonuç.

**Rollback:** Commit revert; eski IBinanceTrading geri gelir, infra impl 4. commit'te değişeceği için birbirine bağımlı.

---

### Commit 4 — Infrastructure: BinanceFuturesClient REST

**Dosyalar:**
- `D:/repos/BinanceBot/src/Infrastructure/Binance/BinanceFuturesClient.cs` (yeni — IExchangeClient impl, `/fapi/v1/...` endpoints)
- `D:/repos/BinanceBot/src/Infrastructure/Binance/Handlers/FuturesSignatureHandler.cs` (yeni — HMAC-SHA256 signing, mevcut BinanceSignatureHelper reuse)
- `D:/repos/BinanceBot/src/Infrastructure/Binance/BinanceTradingClient.cs` **SİL**
- `D:/repos/BinanceBot/src/Infrastructure/Binance/BinanceMarketDataClient.cs` **SİL**
- `D:/repos/BinanceBot/src/Infrastructure/Binance/BinanceOptions.cs` (RestBaseUrl default `https://testnet.binancefuture.com`)
- `D:/repos/BinanceBot/tests/Tests/Infrastructure/Binance/BinanceFuturesClientTests.cs` (yeni — 6 mock HTTP test)

**Done:** 6 unit test geçer; PlaceLiveOrderAsync mock testnet endpoint'inde başarılı. `grep -r "api/v3/order" src/` → 0 sonuç.

**Rollback:** Spot kod silindiği için zor — eğer 4. commit fail ederse 3. commit'i de revert et.

---

### Commit 5 — Infrastructure: FuturesWsSupervisor + UserDataStream

**Dosyalar:**
- `D:/repos/BinanceBot/src/Infrastructure/Binance/Streams/FuturesWsSupervisor.cs` (yeni — reconnect + heartbeat + replay)
- `D:/repos/BinanceBot/src/Infrastructure/Binance/Streams/FuturesStreamParser.cs` (yeni — kline + bookTicker + userData)
- `D:/repos/BinanceBot/src/Infrastructure/Binance/Streams/FuturesUserDataStreamWorker.cs` (yeni — listenKey lifecycle)
- `D:/repos/BinanceBot/src/Infrastructure/Binance/Streams/BinanceWsSupervisor.cs` **SİL**
- `D:/repos/BinanceBot/src/Infrastructure/Binance/Streams/BinanceStreamParser.cs` **SİL** (veya rewrite — Spot-spesifik mantık temizle)
- `D:/repos/BinanceBot/src/Infrastructure/Binance/Streams/BinanceStreamBus.cs` (genel kalır, Spot-spesifik özellik temizle)
- `D:/repos/BinanceBot/tests/Tests/Infrastructure/Binance/BinanceStreamBusTests.cs` (Futures parser test'lerine güncel)

**Done:** Supervisor reconnect smoke test geçer (testnet WS). 0 stream message silently drop (reviewer-ws-resiliency skill).

**Rollback:** Stream subsystem komple yenilendiği için commit revert tek seçenek.

---

### Commit 6 — Infrastructure: FuturesPaperFillSimulator

**Dosyalar:**
- `D:/repos/BinanceBot/src/Infrastructure/Trading/Paper/FuturesPaperFillSimulator.cs` (yeni — IPaperFillSimulator impl, margin akışı)
- `D:/repos/BinanceBot/src/Infrastructure/Trading/Paper/PaperFillSimulator.cs` **SİL**
- `D:/repos/BinanceBot/src/Infrastructure/Trading/Paper/PaperFeeSimulator.cs` (taker rate 0.04% futures default; konfig-driven kalır)
- `D:/repos/BinanceBot/tests/Tests/Infrastructure/Trading/PaperFillSimulatorTests.cs` **SİL** (Spot test)
- `D:/repos/BinanceBot/tests/Tests/Infrastructure/Trading/PaperFillSimulator_MarketMinNotionalTests.cs` **SİL veya migrate** (filtre validation Futures'ta da geçerli — migrate)
- `D:/repos/BinanceBot/tests/Tests/Infrastructure/Trading/FuturesPaperFillSimulatorTests.cs` (yeni — 7 test)

**Done:** 7 test geçer (Long open margin, Long close +PnL, Long close −PnL, Short open margin, Short close +PnL, Short close −PnL, insufficient margin reject).

**Rollback:** Spot simulator silindiği için rollback domain commit 1+2'ye kadar geri sarmayı gerektirir.

---

### Commit 7 — Domain: VirtualBalance Futures genişlemesi

**Dosyalar:**
- `D:/repos/BinanceBot/src/Domain/Balances/VirtualBalance.cs` (WalletBalance/AllocatedMargin/UnrealizedPnl + 4 yeni davranış; eski ApplyFill silinir)
- `D:/repos/BinanceBot/src/Domain/Balances/Events/VirtualBalanceEvents.cs` (PositionMarginAllocatedEvent, PositionMarginReturnedEvent, FundingFeeAppliedEvent eklenir; FillAppliedEvent silinir)
- `D:/repos/BinanceBot/tests/Tests/Domain/Balances/VirtualBalanceTests.cs` **rewrite** (eski ApplyFill testleri silinir)
- `D:/repos/BinanceBot/tests/Tests/Domain/Balances/VirtualBalanceFuturesTests.cs` (yeni — 6 test: open margin, close +/- PnL, funding fee, mode-guard)

**Done:** 6 test geçer. Cash invariance: `final_wallet = initial_wallet + Σ realized_pnl − Σ funding_fee` (allocated margin pozisyon kapandığında dönüyor).

**Rollback:** Commit revert; eski ApplyFill geri gelir AMA Commit 6 FuturesPaperFillSimulator buna bağımlı — domino revert.

---

### Commit 8 — EF Migration: RenameVirtualBalanceCurrentToWallet

**Dosyalar:**
- `D:/repos/BinanceBot/src/Infrastructure/Persistence/Configurations/VirtualBalanceConfiguration.cs` (WalletBalance/AllocatedMargin/UnrealizedPnl mapping)
- `D:/repos/BinanceBot/src/Infrastructure/Persistence/Configurations/PositionConfiguration.cs` (PeakMarkPrice → ExtremeMarkPrice rename)
- `D:/repos/BinanceBot/src/Infrastructure/Persistence/Migrations/<timestamp>_RenameVirtualBalanceCurrentToWallet.cs` (auto)

**Komut:**
```powershell
dotnet ef migrations add RenameVirtualBalanceCurrentToWallet `
  --project src/Infrastructure --startup-project src/Api
```

**Done:** Migration `Up()` doğru rename + DEFAULT 0 backfill. `dotnet ef database update` smoke test geçer; mevcut VirtualBalance satırları kayıpsız.

**Rollback:** `Down()` migration geri sarar; `dotnet ef database update <previous>`.

---

### Commit 9 — Infrastructure: MarkToMarketWorker Direction-aware + Liquidation guard

**Dosyalar:**
- `D:/repos/BinanceBot/src/Infrastructure/Positions/MarkToMarketWorker.cs` (SL/TP/BE/Trailing Direction-aware; liquidation guard yeni)
- `D:/repos/BinanceBot/tests/Tests/Infrastructure/Positions/MarkToMarketWorkerShortTests.cs` (yeni — 4 test)
- `D:/repos/BinanceBot/tests/Tests/Infrastructure/Positions/MarkToMarketWorkerBreakEvenTests.cs` (Direction=Long parametre eklenir; aynı testler Long-only kalır)
- `D:/repos/BinanceBot/tests/Tests/Infrastructure/Positions/MarkToMarketWorkerTrailingStopTests.cs` (Direction parametre)

**Done:** 4 yeni Short test geçer. Long testler aynı geçer (regression yok). Liquidation guard %80 marginRatio'da CloseSignalPositionCommand dispatch eder (test).

**Rollback:** Commit revert. Worker stateless — DB değişimi yok, kod-only revert.

---

### Commit 10 — Application: 7 yeni Short pattern detector + IPatternDetector.Direction

**Dosyalar:**
- `D:/repos/BinanceBot/src/Application/Strategies/Patterns/IPatternDetector.cs` (Direction property eklenir; PatternDirection enum yeni)
- `D:/repos/BinanceBot/src/Application/Strategies/Patterns/BearishEngulfingDetector.cs` (yeni)
- `D:/repos/BinanceBot/src/Application/Strategies/Patterns/ShootingStarDetector.cs` (yeni)
- `D:/repos/BinanceBot/src/Application/Strategies/Patterns/BollingerUpperReversalDetector.cs` (yeni)
- `D:/repos/BinanceBot/src/Application/Strategies/Patterns/BollingerSqueezeBreakDownDetector.cs` (yeni)
- `D:/repos/BinanceBot/src/Application/Strategies/Patterns/RsiOverboughtPullbackDetector.cs` (yeni)
- `D:/repos/BinanceBot/src/Application/Strategies/Patterns/Ema9SlopeDownDetector.cs` (yeni)
- `D:/repos/BinanceBot/src/Application/Strategies/Patterns/DonchianBreakdownDetector.cs` (yeni)
- Mevcut 10 detector'a `Direction` property: 7 long-bias = Long, 3 ortak (Volume/Spread/Adx) = Neutral.
- Tests: 7 yeni × 3 senaryo = 21 yeni unit test.

**Done:** 21 yeni test geçer. Mevcut 30+ long detector test regression yok (Direction property eklendi, davranış aynı).

**Rollback:** Commit revert. Detector dosyaları izole.

---

### Commit 11 — Application: WeightedScorePatternComposer Direction-aware

**Dosyalar:**
- `D:/repos/BinanceBot/src/Application/Strategies/Patterns/WeightedScorePatternComposer.cs` (iki-kova logic)
- `D:/repos/BinanceBot/src/Application/Strategies/Patterns/CompositeSignalDecision.cs` (Direction field eklenir)
- `D:/repos/BinanceBot/tests/Tests/Application/Strategies/Patterns/WeightedScorePatternComposerTests.cs` (10 yeni test: 4 Long emit, 4 Short emit, 2 both-qualified skip)

**Done:** 10 test geçer. Mevcut composer testleri (varsa) Long-direction varsayımıyla regression yok.

**Rollback:** Commit revert.

---

### Commit 12 — Application: PatternCompositeEvaluator MTF gate Direction-aware

**Dosyalar:**
- `D:/repos/BinanceBot/src/Application/Strategies/Patterns/PatternCompositeEvaluator.cs` (composer çıktısı Direction'a göre MTF gate Long/Short asymmetric)
- `D:/repos/BinanceBot/tests/Tests/Application/Strategies/Patterns/PatternCompositeEvaluatorTests.cs` (4 yeni test: slope > 0 + Short skip, slope < 0 + Long skip, eligible cases)

**Done:** 4 yeni test geçer. Composer çıktısı StrategySignal'a Direction propagate eder.

**Rollback:** Commit revert.

---

### Commit 13 — DI / Composition Root + appsettings + Spot kod kalıntı temizle

**Dosyalar:**
- `D:/repos/BinanceBot/src/Api/Program.cs` (Futures-only DI: 17 detector singleton, FuturesKlineWorker/FuturesBookTickerWorker/FuturesUserDataStreamWorker/FuturesFundingRateWorker hosted services)
- `D:/repos/BinanceBot/src/Api/appsettings.json` (Trading:Mode=Futures, Binance.RestBaseUrl Futures testnet)
- `D:/repos/BinanceBot/src/Api/appsettings.Development.json` (testnet override)
- `D:/repos/BinanceBot/src/Infrastructure/Workers/FuturesKlineWorker.cs` (yeni)
- `D:/repos/BinanceBot/src/Infrastructure/Workers/FuturesBookTickerWorker.cs` (yeni)
- `D:/repos/BinanceBot/src/Infrastructure/Workers/FuturesFundingRateWorker.cs` (yeni — 8h bir polling)
- Eski Spot KlineSubscriber/BookTickerSubscriber **SİL** (varsa).
- `grep -r "BinanceSpot" src/` → 0 sonuç.
- `grep -r "stream.binance.com" src/` → 0 sonuç (Futures `fstream` URL'si).

**Done:** `dotnet build` 0 hata. `dotnet run` boot OK. Logs Futures testnet'e bağlandı (`wss://stream.binancefuture.com`).

**Rollback:** Eski composition root geri yüklenirse Spot endpoint'leri lazım — ama Spot kodu silindiği için tek yol commit 1-13 hep birden revert.

---

### Commit 14 — RiskProfile Futures fields + Loop92 reset migration + Frontend Direction badge + Tester Playwright

**Dosyalar:**
- `D:/repos/BinanceBot/src/Domain/RiskProfiles/RiskProfile.cs` (Leverage/MaintenanceMarginRatio/MaxFundingFeePerHour field + 3 davranış: SetLeverage, SetMaintenanceMarginRatio, ApplyFundingFeeAndCheck)
- `D:/repos/BinanceBot/src/Infrastructure/Persistence/Configurations/RiskProfileConfiguration.cs` (kolon + HasData seed güncel)
- `D:/repos/BinanceBot/src/Infrastructure/Risk/RiskProfileSeeder.cs` (default leverage=1, maint=0.80, maxFunding=0.001)
- `dotnet ef migrations add AddRiskProfileFuturesFields`
- `dotnet ef migrations add Loop92FuturesPivotReset` (DELETE FROM Positions/Orders/OrderFills/StrategySignals — idempotent, Loop 91 zaten temiz)
- `D:/repos/BinanceBot/src/Api/wwwroot/js/components/PositionList.js` (veya equivalent — Direction badge: Long yeşil arrow-up, Short kırmızı arrow-down)
- `D:/repos/BinanceBot/src/Api/wwwroot/js/components/SignalList.js` (Direction kolonu eklenir)
- `D:/repos/BinanceBot/src/Api/wwwroot/js/components/BalanceCard.js` (WalletBalance + AllocatedMargin + UnrealizedPnl ayrı satır)
- `D:/repos/BinanceBot/tests/Tests/Domain/RiskProfiles/RiskProfileTests.cs` (3 yeni test: leverage guard, maint margin trigger, funding circuit)
- Tester agent: Playwright senaryo `tests/e2e/loop92-pivot.spec.ts` (boot → 30dk → ≥3 emit, ≥1 Short, dashboard render).

**Done:**
- `dotnet test` full pass (60+ yeni test).
- `dotnet ef database update` migration sırası çalışır.
- Bot boot 30dk: ≥3 emit, ≥1 Short emit, 0 unhandled exception, dashboard 3 yeni component (Direction badge, WalletBalance breakdown, FundingRate ledger) render.
- Reviewer onay: ADR-0006 guard aktif, Spot kalıntısı 0 dosya, fee abstraction config-driven.

**Rollback:** Loop92 reset migration veri silici — rollback yok. Yeni Loop 93 boot bağımsız restart.

---

## 3. Bağımlılık Grafiği

```
Commit 1 (Domain TradeDirection)
    ↓
Commit 2 (Migration Direction kolon) ← Commit 1 gerek
    ↓
Commit 3 (IExchangeClient port)
    ↓
Commit 4 (BinanceFuturesClient REST) ← Commit 3 gerek
    ↓
Commit 5 (FuturesWsSupervisor) ← Commit 4 gerek (REST endpoint paralel)
    ↓
Commit 6 (FuturesPaperFillSimulator) ← Commit 1+4 gerek (Direction + Exchange port)
    ↓
Commit 7 (VirtualBalance Futures) ← Commit 6 paralel (cüzdan davranışı)
    ↓
Commit 8 (Migration Wallet rename) ← Commit 7 gerek
    ↓
Commit 9 (MarkToMarketWorker Direction) ← Commit 1+7 gerek
    ↓
Commit 10 (7 Short detector + IPatternDetector.Direction)
    ↓
Commit 11 (WeightedScorePatternComposer Direction-aware) ← Commit 10 gerek
    ↓
Commit 12 (PatternCompositeEvaluator MTF Direction) ← Commit 11 gerek
    ↓
Commit 13 (DI Composition Root + Workers) ← Commit 1-12 hepsi gerek
    ↓
Commit 14 (RiskProfile + reset + Frontend + Tester)
```

**Paralel iş izni:**
- Commit 5 (WS) + Commit 6 (Simulator) paralel olabilir (farklı namespace).
- Commit 10 (detector) Commit 1-9'dan tamamen bağımsız — backend-dev başka kişi varsa paralel başlayabilir.

---

## 4. Risk Haritası

| Risk | Şiddet | Olasılık | Mitigation |
|---|---|---|---|
| Futures testnet WS stability (kline drop / disconnect) | Orta | Orta | reviewer-ws-resiliency skill commit 5 review; subscribe replay zorunlu; 30s reconnect cap. |
| Futures fee rate ADR-0020 fee abstraction'ı kırar | Yüksek | Düşük | PaperFeeSimulator config-driven (commit 6); taker rate 0.04% appsettings; mevcut accounting invariance test reuse. |
| Composer "both qualified skip" oranı %20+ → frekans kaybı | Orta | Orta | Commit 14 sonrası tester loop 92 t60 ölçer; composer logic re-tune Loop 93 (ADR güncellemesi gerekmez, options config). |
| Long+Short aynı bar'da composer'a girip çakışma | Düşük | Düşük | Composer karar logic test'inde (commit 11) 2 senaryo doğrular; "both qualified" guarded skip ile UI kararsızlığı yok. |
| Migration sırası bozulursa data loss | Yüksek | Düşük | 4 migration atomik komut (`dotnet ef database update`); CI smoke test sırayı dev'de doğrular; prod'da Loop92Reset zaten DB sıfırlar (idempotent). |
| Spot kodu silinirken kullanılan helper unutulur (örn. BinanceSignatureHelper) | Düşük | Orta | Commit 4 sonrası `grep -r "BinanceSignatureHelper" src/` izleme; Futures handler reuse eder, silinmez. Reviewer commit 13 kontrol noktası. |
| ADR-0006 mainnet guard kazara delinir (Futures mainnet aksi belirtilmediyse) | Çok yüksek | Düşük | appsettings `AllowMainnet=false` default; `BinanceOptions` validation `MainnetRestBaseUrl` aktif sadece flag true; commit 13 reviewer assert. |
| Liquidation guard yanlış formül → false-positive close | Orta | Düşük | MarginRatio formülü Position aggregate'inde test (commit 9, 4 test); domain method `CalculateMarginRatio()` invariant guard. |
| Futures testnet API key spot anahtarı zannedilirse 401 | Düşük | Orta | Setup adımı user-secrets dokümante (ADR §25 Migration Notları); Loop 92 boot script API key validation eklenir. |
| Funding rate worker 8h cycle'ı ilk loop içinde tetiklenmezse audit boş | Düşük | Yüksek | İlk funding tick boot+1h içinde yapay tetiklenir (test path); production 8h cycle korunur. Commit 13 worker `RunImmediateOnBoot=true` config flag. |
| Direction badge frontend'de yanlış renk kodlama (Long=kırmızı kazara) | Düşük | Düşük | Tester agent commit 14 Playwright screenshot review; gözle doğrula. |

---

## 5. Done-Definition (PM Checkpoint Gates)

Loop 92 "ready" denmesi için:

- [ ] 14 commit hepsi `development` branch'a push'lu (CLAUDE.md §10).
- [ ] `dotnet test` 0 fail (hedef ~120 yeni/değişen test).
- [ ] `dotnet ef database update` migration sırası lokal MSSQL'de smoke test geçer.
- [ ] Bot boot 30dk: ≥3 emit, ≥1 Short emit, 0 unhandled exception (logs).
- [ ] Dashboard render:
  - [ ] Position listesi Direction badge.
  - [ ] Signal listesi Direction kolonu.
  - [ ] BalanceCard WalletBalance + AllocatedMargin + UnrealizedPnl ayrı.
- [ ] Reviewer kontrol noktaları (commit 14 sonrası):
  - [ ] `grep -r "api/v3/order" src/` → 0.
  - [ ] `grep -r "stream.binance.com:9443" src/` → 0.
  - [ ] `grep -r "IBinanceTrading" src/` → 0.
  - [ ] `grep -r "PositionSide" src/` → 0 (PositionSide value object silindi).
  - [ ] `grep -r "PaperFillSimulator" src/` → 0 (FuturesPaperFillSimulator kaldı).
  - [ ] ADR-0006 `AllowMainnet=false` aktif.
- [ ] ADR-0025 Status: Proposed → Accepted (PM onayı sonrası).
- [ ] Tester Playwright `loop92-pivot.spec.ts` yeşil.

---

## 6. Backend-Dev Handoff Notları

- **Test-driven order:** Her commit Domain → Migration → Infrastructure → Application → Composition Root sırasıyla. TDD: önce test yaz, sonra implement (Domain commit 1+7, Application commit 10-12 için kritik).
- **Result<T> disiplini:** IExchangeClient (commit 3) tüm method'lar `Result<T>` döndürür (Ardalis.Result). Exception-for-flow yasağı (CLAUDE.md §5).
- **Async + CancellationToken:** Tüm async method `CancellationToken ct` parametresi alır (CLAUDE.md src §9).
- **ILogger structured:** Hard-coded string concat yasak; `LogInformation("Direction={Direction}", direction)` (CLAUDE.md src §8).
- **AsNoTracking() read path:** Workers (MarkToMarketWorker, FuturesUserDataStreamWorker) read query'lerinde zorunlu (CLAUDE.md src §4).
- **agent-bus MCP:** Her commit sonrası `append_decision` ile karar log'u; ben (architect) ADR-0025 yazıldığında one-shot çağırırım, backend-dev her commit'te tekrarlar.

---

## 7. ADR-0025 Status Yaşam Döngüsü

| Faz | Status | Tetik |
|---|---|---|
| Bu plan yazıldı | Proposed | architect ADR yazımı |
| backend-dev commit 1 push | Proposed (kabul edilmedi henüz) | implementation başlangıcı |
| Loop 92 t60 tester yeşil | Accepted | tester done-definition + reviewer onay |
| Loop 93+ regression Loop 92 ölçütlerini bozarsa | Superseded by ADR-NNNN | yeni ADR kararı |

---

## 8. Kaynak

- [`docs/adr/0025-futures-short-pivot.md`](../../docs/adr/0025-futures-short-pivot.md) — bu planın doğduğu ADR.
- [`docs/adr/0024-pattern-based-scalping.md`](../../docs/adr/0024-pattern-based-scalping.md) — pattern subsystem altyapısı.
- [`docs/adr/0008-trading-modes.md`](../../docs/adr/0008-trading-modes.md) — TradingMode enum kontratı.
- [`docs/adr/0006-testnet-first-policy.md`](../../docs/adr/0006-testnet-first-policy.md) — mainnet guard.
- [`docs/adr/0023-risk-first-tp-sl-asymmetry.md`](../../docs/adr/0023-risk-first-tp-sl-asymmetry.md) — R:R 1:2.5 korunur.
- [Binance USDT-M Futures REST API](https://binance-docs.github.io/apidocs/futures/en/) — endpoint reference.
- [Binance Futures Testnet](https://testnet.binancefuture.com/) — ayrı API key + WS host.
- Memory `trading_vision.md`, `feedback_frekans_kartopu.md`, `feedback_no_dead_code.md`, `feedback_no_session_split.md`.
