# Loop 81 — Architecture Plan: Pattern-Based Scalping Pivot

Status: Plan
Owner: architect → backend-dev → tester → reviewer
ADR: [0024-pattern-based-scalping](../../docs/adr/0024-pattern-based-scalping.md)
Memory ref: `trading_vision.md`, `feedback_frekans_kartopu.md`, `feedback_no_dead_code.md`

---

## 1. Hedef

ADR-0024 kararını koda dökmek. Mevcut `KmsMomentumEvaluator` + `BbReversalEvaluator` çiftini sil; yerine `IPatternDetector` + `BarSnapshot` + `PatternSignalComposer` + `PatternCompositeEvaluator` mimarisini koy. 10 pattern detector + 1 composer + 1 yeni evaluator + 1 migration + yeni seed.

**Başarı tanımı (DoD):**
- `dotnet build` warn-free.
- `dotnet test` tüm yeşil (mevcut + yeni unit testler).
- Loop 81 boot sonrası 30dk içinde **≥3 emit, ≥1 fill** (frekans kuralı sağlandı).
- Dashboard Strategies sayfası 5 PatternComposite stratejiyi gösterir, Status=Active.
- ContextJson içinde "patterns" array gözükür (SystemEvents → StrategySignalEmitted payload).

---

## 2. Etkilenen / Eklenecek / Silinecek Dosyalar

### 2.1 Domain (1 dosya — minimal)
- `src/Domain/Strategies/StrategyEnums.cs` — KMS=1, BBR=2 silinir; `PatternComposite=3` eklenir; xml-doc güncellenir (Loop 81 ref).

### 2.2 Application (yeni dosyalar — additive)
- `src/Application/Strategies/Patterns/BarSnapshot.cs` (record)
- `src/Application/Strategies/Patterns/IPatternDetector.cs`
- `src/Application/Strategies/Patterns/PatternEvaluation.cs` (record)
- `src/Application/Strategies/Patterns/IPatternRegistry.cs`
- `src/Application/Strategies/Patterns/IPatternSignalComposer.cs`
- `src/Application/Strategies/Patterns/CompositeSignalDecision.cs` (record)
- `src/Application/Strategies/Patterns/PatternComposerOptions.cs`

### 2.3 Application (silinecek)
- `src/Application/Strategies/Indicators/KmsMomentumSnapshot.cs` **DELETE**
- `src/Application/Strategies/Indicators/BbReversalSnapshot.cs` **DELETE**
- `src/Application/Strategies/Indicators/DonchianBreakoutIndicatorSnapshot.cs` **DELETE** (henüz tracked değil ama orphan; kullanılmıyor)

### 2.4 Application (refactor)
- `src/Application/Strategies/Indicators/IMarketIndicatorService.cs` — `TryGetKmsMomentumSnapshot` + `TryGetBbReversalSnapshot` SİL; tek method `TryGetBarSnapshot(string symbol)` döner `BarSnapshot?`.

### 2.5 Infrastructure (yeni — Patterns klasörü)
- `src/Infrastructure/Strategies/Patterns/PatternRegistry.cs`
- `src/Infrastructure/Strategies/Patterns/WeightedScorePatternComposer.cs`
- `src/Infrastructure/Strategies/Patterns/Detectors/BullishEngulfingDetector.cs`
- `src/Infrastructure/Strategies/Patterns/Detectors/HammerReversalDetector.cs`
- `src/Infrastructure/Strategies/Patterns/Detectors/BollingerLowerReversalDetector.cs`
- `src/Infrastructure/Strategies/Patterns/Detectors/BollingerSqueezeBreakoutDetector.cs`
- `src/Infrastructure/Strategies/Patterns/Detectors/RsiOversoldRecoveryDetector.cs`
- `src/Infrastructure/Strategies/Patterns/Detectors/Ema9SlopeMomentumDetector.cs`
- `src/Infrastructure/Strategies/Patterns/Detectors/DonchianBreakoutDetector.cs`
- `src/Infrastructure/Strategies/Patterns/Detectors/VolumeSurgeConfirmDetector.cs`
- `src/Infrastructure/Strategies/Patterns/Detectors/SpreadGuardDetector.cs`
- `src/Infrastructure/Strategies/Patterns/Detectors/AdxRegimeDetector.cs`
- `src/Infrastructure/Strategies/Evaluators/PatternCompositeEvaluator.cs`

### 2.6 Infrastructure (silinecek)
- `src/Infrastructure/Strategies/Evaluators/KmsMomentumEvaluator.cs` **DELETE**
- `src/Infrastructure/Strategies/Evaluators/BbReversalEvaluator.cs` **DELETE**
- `src/Infrastructure/Strategies/Evaluators/DonchianBreakoutEvaluator.cs` **DELETE** (orphan)

### 2.7 Infrastructure (refactor)
- `src/Infrastructure/Strategies/Indicators/MarketIndicatorService.cs` — iki snapshot method'u silinir, `TryGetBarSnapshot` eklenir; içerik mevcut Indicators.cs helper'larıyla zaten hazır (RSI/EMA/ATR/BB/ADX/Donchian/TradeCount). `BarSnapshot.SpreadPct` için constructor `IBookTickerReader` inject — snapshot build sırasında okunur.
- `src/Infrastructure/Strategies/Evaluators/Indicators.cs` — pure functions, dokunulmaz; tüm detector'lar paylaşır.
- `src/Infrastructure/DependencyInjection.cs` —
  - SİL: `AddSingleton<IStrategyEvaluator, KmsMomentumEvaluator>()`
  - SİL: `AddSingleton<IStrategyEvaluator, BbReversalEvaluator>()`
  - EKLE: 10 `AddSingleton<IPatternDetector, …>()`
  - EKLE: `AddSingleton<IPatternRegistry, PatternRegistry>()`
  - EKLE: `AddSingleton<IPatternSignalComposer, WeightedScorePatternComposer>()`
  - EKLE: `AddSingleton<IStrategyEvaluator, PatternCompositeEvaluator>()`

### 2.8 Persistence (migration)
- `src/Infrastructure/Persistence/Migrations/<timestamp>_Loop81PatternPivot.cs` — Strategies + StrategySignals + Positions + Orders + OrderFills için `DELETE FROM`. (Loop 67 reset migration örnek alınır.)

### 2.9 Config
- `src/Api/appsettings.json` — `Strategies.Seed` alanı tamamen yeniden yazılır:
  - 10 eski seed (5 KMS + 5 BBR) **silinir**.
  - Yeni 5 seed (`BTC-Pattern`, `ETH-Pattern`, `XRP-Pattern`, `SOL-Pattern`, `ADA-Pattern`) — Type=`PatternComposite`, ortak ParametersJson (per-coin override istenirse weight overrides).

### 2.10 Tests
- `tests/Tests/Application/Strategies/Patterns/<DetectorName>Tests.cs` — her detector için ayrı dosya (10 dosya × ~3 case = ~30 unit test).
- `tests/Tests/Application/Strategies/Patterns/WeightedScorePatternComposerTests.cs` (~8 case).
- `tests/Tests/Infrastructure/Strategies/PatternCompositeEvaluatorTests.cs` (~4 case — IMarketIndicatorService mock, real composer).
- Mevcut testler: `CooldownServiceTests.cs` korunur. KMS/BBR'ye özel test varsa silinir (bkz. step 11).

---

## 3. Deploy Sırası (atomik commit'ler)

### Commit 1 — Application port'ları (8 dosya, no behavior change)
1. `BarSnapshot`, `IPatternDetector`, `PatternEvaluation`, `IPatternRegistry`, `IPatternSignalComposer`, `CompositeSignalDecision`, `PatternComposerOptions` dosyalarını yarat.
2. `IMarketIndicatorService` ÜZERİNE `BarSnapshot? TryGetBarSnapshot(string)` ekle (eski iki method'u şimdilik bırak — backward compat geçici).
3. `dotnet build` yeşil.

### Commit 2 — MarketIndicatorService refactor
1. `TryGetBarSnapshot` impl'i — mevcut iki method'un overlap'i tek build path'e indirgenir; SpreadPct için ctor'a `IBookTickerReader` inject.
2. Eski iki method gövdesi `TryGetBarSnapshot` üstünde adapter yazılarak korunur (geçici, Commit 6'da silinir).
3. `dotnet test` yeşil.

### Commit 3 — Detector'lar (10 dosya, izole)
1. Her detector ayrı commit veya gruplanmış (önerilen: 3 grup × ~3 detector). Hepsi pure function, IPatternDetector impl. Stateless singleton.
2. Unit testler aynı commit içinde (TDD; detector + test paralel).
3. `dotnet test` yeşil; mevcut KMS/BBR hâlâ aktif.

### Commit 4 — PatternRegistry + Composer
1. `PatternRegistry` IEnumerable<IPatternDetector> tüketir, kayıt sırasını tutar.
2. `WeightedScorePatternComposer` impl + 8 unit test.
3. DI: registry + composer kaydı (henüz aktif değil — evaluator yok).
4. `dotnet test` yeşil.

### Commit 5 — PatternCompositeEvaluator + DI swap
1. `PatternCompositeEvaluator : IStrategyEvaluator` impl. Type=`PatternComposite`.
2. DI: KMS + BBR evaluator kayıtları SİLİNİR; PatternCompositeEvaluator EKLENİR.
3. **Önemli:** Bu commit'te DB henüz reset edilmedi. Eski strategy row'ları (Type=1/2) hâlâ var — registry resolve null döner ⇒ handler "No evaluator for type" warn loglar, downtrade yok ama emit de yok. Bot çalışır, sıfır trade üretir. Beklenen.
4. `dotnet test` yeşil.

### Commit 6 — Domain enum + Snapshot/Evaluator silme + IMarketIndicatorService temizliği
1. `StrategyType` enum'dan `KlineMomentumSpread5m=1` ve `BollingerBandReversal5m=2` SİL; `PatternComposite=3` kalır.
2. `KmsMomentumSnapshot.cs`, `BbReversalSnapshot.cs`, `DonchianBreakoutIndicatorSnapshot.cs` SİL.
3. `KmsMomentumEvaluator.cs`, `BbReversalEvaluator.cs`, `DonchianBreakoutEvaluator.cs` SİL.
4. `IMarketIndicatorService` eski iki method'u SİL; sadece `TryGetBarSnapshot` kalır.
5. `MarketIndicatorService` adapter wrapper'ları SİL; tek native impl kalır.
6. `dotnet build` yeşil (eski seed Type kolonu artık parse edilemez — Commit 7 ile çözülür).

### Commit 7 — appsettings seed swap + Migration
1. `appsettings.json` `Strategies.Seed` 10 satır SİL → 5 yeni satır EKLE (`<COIN>-Pattern`, Type=PatternComposite).
2. Migration `Loop81PatternPivot` üret: `DELETE FROM Strategies; DELETE FROM StrategySignals; DELETE FROM Positions; DELETE FROM Orders; DELETE FROM OrderFills;`. Down: no-op (data restore yok).
3. `dotnet ef migrations add Loop81PatternPivot --project src/Infrastructure --startup-project src/Api`.
4. **Bot restart bu commit sonrası migration'ı otomatik uygular** (ADR-0001 auto-migration).

### Commit 8 — Tester smoke + reviewer
1. PM `tester` agent'ı çağırır: Playwright ile bot UI gez, /api/strategies 5 PatternComposite Active doğrula, 30dk gözlem (≥3 emit beklentisi).
2. PM `reviewer` agent'ı çağırır: dependency rule, Result<T>, structured logging, EF AsNoTracking spot check.
3. Reviewer onay → PM kullanıcıya rapor.

---

## 4. Risk + Rollback

| Risk | Mitigation |
|---|---|
| Yeni detector calibration kötü → 0 emit veya çok loss | Commit 7 öncesi (Commit 5) **kontrollü gri zaman** — eski eval kayıtları silinmiş ama yeni pattern aktif değil, sıfır trade. 30dk gözlem; bar build'in çalıştığı doğrulanır. |
| Migration DB sıfırlama yanlış zamanlama | Migration sadece deploy edildiği zaman çalışır; manual rollback `dotnet ef migrations remove` + `dotnet ef database update <prevMigration>`. |
| Composer hard-gate yanlış set → tüm emit'ler skip | Commit 4'te composer unit test "all detectors hard-gate-pass ⇒ emit" senaryosu zorunlu. |
| ContextJson çok büyük → SystemEvents tablosu şişer | Throttle mevcut (StrategySignalSkippedEvent per-minute throttle reuse). Emit ContextJson <2KB hedef (10 detector × ~150 byte). |
| Loop 67 KMS reset tekrarı / DB istate karmaşası | Migration'da explicit `IF EXISTS` koruması, idempotent. Loop 67 migration örnek pattern. |

**Acil rollback:** PM kullanıcıya "rollback?" sorar → backend-dev `git revert <commit-7>` + `dotnet ef database update <prevMigration>` + restart. Eski KMS+BBR evaluator dosyaları git history'den `git checkout HEAD~N -- <path>` ile geri çağrılır (ADR-0014 superseded olur, yeni ADR-0025 yazılır).

---

## 5. Çıktı Listesi (PM özeti için)

- ADR: `docs/adr/0024-pattern-based-scalping.md` (oluşturuldu)
- Plan: `loops/loop_81/architecture-plan.md` (bu dosya)
- Sonraki adım: PM → backend-dev handoff (Commit 1'den başla, sıralı 8 commit).
- Test: tester agent Loop 81 boot smoke + 30dk frekans gözlemi.
- Reviewer: dependency rule + DRY + frekans hedefi (≥30 emit/h).

---

## 6. Açık Sorular (Backend-dev'e)

1. `BarSnapshot.RecentBars` — full 200 bar mı (memory ağır), yoksa son 30 bar yeter mi (Donchian20 + Engulfing 2-bar için)? **Öneri:** son 30 bar; Donchian/Hammer için bu yeter. Memory hafif.
2. `SpreadGuardDetector` snapshot.SpreadPct mi okur, kendi `IBookTickerReader` mı tüketir? **ADR karar:** snapshot.SpreadPct (pure detector). Snapshot build sırasında IBookTickerReader sorgulanır.
3. Per-coin weight override JSON formatı — Dictionary<string, decimal>? **Önerilen:** evet, key=detector.Name (snake_case).
4. Composer logging — her bar 50 detector eval log spam riski. Default LogLevel=Trace, sadece composer Decision LogLevel=Information. **Onay alındı kabul edilir.**
