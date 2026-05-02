# 0024. Pattern-Based Scalping Pivot — Composite Detector + Weighted Score

Date: 2026-05-02
Status: Proposed (Loop 80 sabit -$8.26 + L80 t270 90dk 0 emit sonrası)
Relates to: ADR-0014 (superseded), ADR-0015, ADR-0023
Memory ref: `trading_vision.md`, `feedback_frekans_kartopu.md`, `feedback_no_dead_code.md`

> **Özet:** Loop 67-80 boyunca KMS (oversold-recovery) + BBR (range mean-reversion) AND-gate evaluator çifti tek-tip "indikator skor zinciri" mantalitesini takip etti. Pazar rejim değişimine adapte olamadı (Loop 80: 270dk **7 emit / 3 close / -$0.518**, hedef 30+/h ihlal). Loop 81+ için stratejik pivot: tek monolitik evaluator yerine **N adet bağımsız `IPatternDetector`**, ortak `BarSnapshot`, **`PatternSignalComposer`** ile ağırlıklı skor toplama, tek bir `StrategyType.PatternComposite` üzerinden emit. Mevcut `KmsMomentumEvaluator` + `BbReversalEvaluator` + `KmsMomentumSnapshot` + `BbReversalSnapshot` + ilgili 10 seed silinir (ADR-0014 tarzı temiz reset).

---

## Context

### 24.1 Loop 67-80 Tıkanma

| Loop | Strateji | Süre | Trade | PnL | Halt sebebi |
|---|---|---|---|---|---|
| 67-78 | KMS tek başına | ~10 loop | düşük freq | -$6.74 | EMA200 / BBW / ADX gate üst üste eklendi → ya 0 emit ya big SL |
| 79 | KMS + BBR (Range) | 1 loop | 0/2 | -$0.50 | BBR 0 trade (volume+ADX gate yok) |
| 80 | KMS + BBR + ADX | 1 loop | 7/270dk | -$0.518 | 90dk üstüste 0 emit, 1.5 emit/h (hedef 30+/h) |

**Kök sorun (mimari):** Tek "score evaluator" modeli **single-shape**. Her yeni gözlem (Wilder ADX, EMA200, BBW threshold) aynı `KmsMomentumEvaluator` Parameters sınıfına yeni bool flag eklemekle çözüldü. Sonuç:
1. Cyclomatic complexity patladı (`KmsMomentumEvaluator` 520 LOC, 35 parametre, 3 hard-gate + 6 puanlı skor + 3-tier geometri).
2. Yeni pattern eklemek = aynı evaluator'a yeni `if (snapshot.X && p.YEnabled)` bloğu = OCP ihlali.
3. Pattern bağımsız ölçülemiyor (which gate yedi false-skip / hangi koşul emit verdi izlenemiyor — log var, ama 6 koşul AND zincirinde **hangisi** baskın bilinmiyor).
4. **Yeni gözlem (engulfing, hammer, fakeout, breakout-volume-spike, range-rejection vs.) için yer yok** — visión `trading_vision.md` 5-10 chart pattern detector ister.

### 24.2 Vision (Memory `trading_vision.md`)

Kullanıcı vizyonu: **"Mevcut 3 strateji silinecek. 5-10 chart pattern detector, 5-10dk scalping, ağırlıklı sinyal, kartopu hedef."**

Şu anki mimari bunun antitezi: 1 evaluator + 35 parametre + AND gate. Vizyon: **N detector + her biri 0..1 skor + ağırlıklı toplam ≥ threshold ⇒ emit.**

### 24.3 Frekans Kuralı (CLAUDE.md §12 + Memory `feedback_frekans_kartopu.md`)

> "5 coin min + sürekli işlem + kartopu kar. Bot saatte 30+ işlem yapmak zorunda (ideal 150/h). 0 emit > 1 saat → ANINDA filtre gevşet veya pivot."

Loop 80: **1.5 emit/h, 0 trade/h** son 3 saat. Kuralı **fena halde** ihlal ediyor. Pivot zorunlu.

### 24.4 Halihazırdaki DDD/Clean İskeleti — Korunacaklar

Pivot **strateji aile**ini değiştirir, **aggregate** ve **CQRS sınır**larını değiştirmez:
- `Strategy` aggregate (Domain) — `Type`, `Status`, `ParametersJson`, `EmitSignal` davranışı **olduğu gibi** kalır.
- `StrategySignal` entity — `SuggestedPrice/Stop/TakeProfit/ContextJson` alanları **olduğu gibi**. ContextJson içinde "hangi pattern hangi skor verdi" payload'ı taşınır.
- `IStrategyEvaluator` port (Application) — **olduğu gibi**. Sadece **tek bir implementation** kalır: `PatternCompositeEvaluator`.
- `StrategyEvaluatorRegistry` — **olduğu gibi**, ama tek entry: `StrategyType.PatternComposite ⇒ PatternCompositeEvaluator`.
- `StrategyEvaluationHandler` (`KlineClosedEvent` notification) — **olduğu gibi**.
- `StrategySignalToOrderHandler`, `MarkToMarketWorker` (BE move + trailing), `ICooldownService`, `IBookTickerReader`, sizing, risk profile — **hiçbiri etkilenmez**.

Yeni katmanlar (Application + Infrastructure) **additive**: `IPatternDetector`, `BarSnapshot`, `PatternSignalComposer`, `IPatternRegistry`. **Domain'e dokunulmaz** (dependency rule korunur).

---

## Decision

### 24.5 Yeni Mimari Topolojisi

```
┌──────────────────────── DOMAIN (no change) ────────────────────────┐
│  Strategy aggregate · StrategySignal entity · StrategyType enum    │
│  (Type yeni değer: PatternComposite = 3 — KMS=1, BBR=2 silinir)    │
└──────────────────────────────────────────────────────────────────────┘
                                ▲
                                │ uses (snapshot dto + interface)
                                │
┌─────────────────────── APPLICATION (additive) ──────────────────────┐
│                                                                     │
│  IStrategyEvaluator (unchanged port)                                │
│       └─ PatternCompositeEvaluator (single impl)                    │
│                                                                     │
│  Pattern subsystem:                                                 │
│    BarSnapshot               ← shared OHLCV + indicator dto         │
│    IPatternDetector          ← per-pattern port (1 detector = 1 pat)│
│    PatternEvaluation         ← per-detector output (score + ctx)    │
│    IPatternSignalComposer    ← weighted aggregator port             │
│    CompositeSignalDecision   ← composer output (emit/skip + payload)│
│    IPatternRegistry          ← read-only collection of detectors    │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
                                ▲
                                │ implements
                                │
┌────────────────────── INFRASTRUCTURE (rewritten) ───────────────────┐
│                                                                     │
│  PatternCompositeEvaluator (IStrategyEvaluator)                     │
│    1. Build BarSnapshot via IMarketIndicatorService                 │
│    2. Iterate IPatternRegistry → PatternEvaluation list             │
│    3. PatternSignalComposer.Compose(...) → CompositeSignalDecision  │
│    4. Map decision → StrategyEvaluation (TP/SL/qty/ctx)             │
│                                                                     │
│  Patterns/ (one file per detector — SRP):                           │
│    BullishEngulfingDetector       (candlestick — 2-bar reversal)    │
│    HammerReversalDetector         (candlestick — single-bar wick)   │
│    BollingerLowerReversalDetector (mean-revert: lower band touch)   │
│    BollingerSqueezeBreakoutDetector (volatility expansion)          │
│    RsiOversoldRecoveryDetector    (momentum: dip → rising RSI)      │
│    Ema9SlopeMomentumDetector      (trend: short EMA up)             │
│    DonchianBreakoutDetector       (range break: 20-bar high)        │
│    VolumeSurgeConfirmDetector     (gating: trade-count surge)       │
│    SpreadGuardDetector            (gating: liquidity)               │
│    AdxRegimeDetector              (gating: ADX 18-25 sweet spot)    │
│                                                                     │
│  Composer:                                                          │
│    WeightedScorePatternComposer (IPatternSignalComposer)            │
│      - Sums detector scores × weights (config-driven).              │
│      - Hard-gate set: any detector marked HardGate=true with        │
│        score==0 ⇒ skip.                                             │
│      - Default geometry: TP/SL ATR-multiplier from snapshot.        │
│                                                                     │
│  IndicatorMarketService (existing — refactored):                    │
│    - TryGetBarSnapshot(symbol, ind config) ⇒ BarSnapshot            │
│    - Old TryGetKmsMomentumSnapshot / TryGetBbReversalSnapshot       │
│      removed.                                                       │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

### 24.6 Aggregate Boundary Kararı

**Soru:** Pattern signal kendi başına bir aggregate mi (PatternSignal) yoksa StrategySignal'in bir parçası mı?

**Karar:** **Yeni aggregate açma.** Pattern ham çıktısı (`PatternEvaluation`) bir **value object**'tir; persistence yoktur. Sadece `StrategySignal.ContextJson` içinde **payload** olarak saklanır:

```jsonc
{
  "type": "pattern-composite",
  "totalScore": 6.5,
  "requiredScore": 5.0,
  "patterns": [
    {"name":"BullishEngulfing","score":1.0,"weight":1.5,"contribution":1.5,"hardGate":false},
    {"name":"RsiOversoldRecovery","score":0.8,"weight":1.0,"contribution":0.8,"hardGate":false},
    {"name":"VolumeSurgeConfirm","score":1.0,"weight":1.0,"contribution":1.0,"hardGate":true},
    ...
  ],
  "geometry": { "tpAtrMul":1.8, "slAtrMul":0.75, "maxHoldMin":45 }
}
```

**Gerekçe:** Pattern detector sonucu emit/skip kararı için **anlık** bilgidir. Tarihsel pattern sayımı ileride istenirse, ContextJson zaten persistent (StrategySignals tablosu) — read-model üzerinden EF JSON query veya audit job çıkartır. Ayrı tablo + migration gereksiz (KISS).

**Alternatif (reddedildi):** PatternSignal aggregate + PatternSignals tablosu. Maliyet (ek tablo + migration + repo + bandwidth) faydadan büyük; pattern verisi **strateji emit'inin türevi** — kendi başına aggregate olacak iş kuralı yok ("pattern X 3 kez tekrarlandığında strateji aktive olur" gibi davranış yok).

### 24.7 BarSnapshot — DRY Tek Kaynak

Mevcutta `KmsMomentumSnapshot` ve `BbReversalSnapshot` aynı buffer'dan **örtüşen** alanlar üretiyor (RSI, EMA, ATR, ADX, BBW, TradeCount). Tek kaynak:

```csharp
// Application/Strategies/Patterns/BarSnapshot.cs
public sealed record BarSnapshot(
    string Symbol,
    DateTimeOffset BarOpenTime,
    DateTimeOffset BarCloseTime,
    // Last closed bar OHLCV
    decimal Open, decimal High, decimal Low, decimal Close, decimal Volume,
    int TradeCount,
    // Prev bar (for 2-bar pattern detection — engulfing, etc.)
    decimal PrevOpen, decimal PrevHigh, decimal PrevLow, decimal PrevClose,
    int PrevTradeCount,
    // Common indicators (computed once per bar, shared by all detectors)
    decimal Rsi14, decimal Rsi14Prev,
    decimal Ema9, decimal Ema9Prev,
    decimal Ema200,
    decimal Atr14,
    decimal BollingerLower, decimal BollingerMiddle, decimal BollingerUpper,
    decimal BollingerBandWidth,
    decimal Adx14,
    decimal AvgTradeCount20,
    decimal DonchianHigh20, decimal DonchianLow20,
    // Lookups for detectors that need full window
    IReadOnlyList<Kline> RecentBars);
```

`IMarketIndicatorService` tek bir `TryGetBarSnapshot(symbol)` döner. Detector'lar **stateless function**: `BarSnapshot in → PatternEvaluation out`.

### 24.8 IPatternDetector Sözleşmesi

```csharp
// Application/Strategies/Patterns/IPatternDetector.cs
public interface IPatternDetector
{
    /// <summary>Stable identifier — log + ContextJson key. Snake_case.</summary>
    string Name { get; }

    /// <summary>Default ağırlık, composer config override edebilir.</summary>
    decimal DefaultWeight { get; }

    /// <summary>True ise composer hard-gate uygular (score 0 ⇒ emit skip).</summary>
    bool IsHardGate { get; }

    /// <summary>Pure function. Stateless. ≤1ms.</summary>
    PatternEvaluation Evaluate(BarSnapshot snapshot);
}

public sealed record PatternEvaluation(
    string Name,
    decimal Score,        // 0.0 — 1.0 (continuous, soft-fuzzy)
    string? Reason,       // optional log/audit explanation
    object? Payload);     // optional rich context (numbers detector found relevant)
```

**Skor 0..1** kararı: Continuous (0/0.5/1 vs.). False/true ikilisi yerine, "yarı-tetiklenme" mümkün — composer ağırlıklarla harmanlar. Detector output range disiplini: **<0 veya >1 invalid** (composer Clamp ya da log warning).

### 24.9 IPatternSignalComposer

```csharp
public interface IPatternSignalComposer
{
    CompositeSignalDecision Compose(
        BarSnapshot snapshot,
        IReadOnlyList<PatternEvaluation> evaluations,
        PatternComposerOptions options);
}

public sealed record CompositeSignalDecision(
    bool Emit,
    decimal TotalScore,
    decimal RequiredScore,
    string? SkipReason,
    // Geometry (only set when Emit==true)
    decimal? EntryPrice,
    decimal? StopPrice,
    decimal? TakeProfitPrice,
    int? MaxHoldMinutes,
    string ContextJson);
```

**Composer iş kuralları:**
1. Detector'lar Registry'den geldiği için tek bir `IReadOnlyList<IPatternDetector>` kaynağı vardır.
2. Her detector'a **ağırlık** ata (config; default = `DefaultWeight`).
3. **Hard-gate disiplini:** `IsHardGate==true && Score==0` ⇒ `Emit=false, SkipReason="hard_gate:<name>"`.
4. **Ağırlıklı skor:** `total = Σ(score × weight)`.
5. `total >= RequiredScore` ⇒ Emit; aksi ⇒ skip.
6. Geometri **ATR-bazlı** (mevcut KMS pattern korunur): TP/SL ATR multiplier × clip [%min, %max], MaxHold skor-katmanlı (4/5/6 puan = farklı tier).
7. Cooldown gate composer dışında — `PatternCompositeEvaluator` `ICooldownService` üzerinden Emit kararından sonra, signal yayını öncesi uygular (mevcut KMS/BBR pattern aynı).

### 24.10 StrategyType Enum Kararı

**Karar:** **Tek yeni değer** — `PatternComposite = 3`. Her pattern ayrı `StrategyType` **DEĞİL**.

**Gerekçe:**
- Strategy aggregate "stratejik niyet" anlamına gelir (BTC için ne yapıyoruz?). Pattern ise stratejinin **iç parçası**.
- 5 coin × 10 pattern = 50 strateji = 50 satır seed = parametre kombinatoryal patlaması. Korkunç.
- Tek strateji per coin (`BTC-Composite`, `ETH-Composite`...) + ortak detector listesi = N coin × 1 strateji = 5 satır seed.
- Ağırlıklar / threshold strateji parametresi (per-coin override mümkün).

Eski enum değerleri (`KlineMomentumSpread5m=1`, `BollingerBandReversal5m=2`) **silinir** — Loop 67 KMS reset migration'ı pattern'ı tekrar uygulanır (`Loop81PatternPivot` migration: Strategies + StrategySignals + Positions + Orders + OrderFills full delete; reused enum ordinali için risk yok zaten DB sıfır).

```csharp
public enum StrategyType
{
    /// <summary>
    /// Loop 81 — Pattern-composite scalper. Stateless per-pattern detector
    /// listesi (IPatternDetector) ortak BarSnapshot üzerinden çalışır;
    /// PatternSignalComposer ağırlıklı skoru toplar, threshold üstü emit.
    /// Geometri ATR-bazlı (KMS-pattern devam, ADR-0023 R:R 1:2.5).
    /// </summary>
    PatternComposite = 3,
}
```

(Numara 3 — 1 ve 2 silinmiş ordinal, reused değil; new strategy starts at 3 to maintain monotonic enum history.)

### 24.11 Detector Initial Seti (10 detector)

| # | Detector | Tür | DefaultWeight | HardGate | Skor mantığı |
|---|---|---|---|---|---|
| 1 | BullishEngulfingDetector | Candlestick | 1.5 | false | Prev bear bar + curr bull bar; curr body prev'i kapsıyor ⇒ 1.0; close prev open üstü ama body küçük ⇒ 0.5; aksi 0. |
| 2 | HammerReversalDetector | Candlestick | 1.0 | false | Lower-shadow ≥ 2× body, upper-shadow küçük, alt %25 range içinde close ⇒ 1.0; benzer ama daha zayıf ⇒ 0.5. |
| 3 | BollingerLowerReversalDetector | Mean-revert | 1.5 | false | Close < BollingerLower × (1 + buffer) AND Rsi14 < 35 AND Rsi14 > Rsi14Prev ⇒ 1.0; sadece touch ⇒ 0.5. |
| 4 | BollingerSqueezeBreakoutDetector | Volatility | 1.0 | false | BBW past 6 bars min'i ≤ %0.4 + curr close > BollingerUpper ⇒ 1.0 (squeeze release); BBW genişledi ama break yok ⇒ 0.5. |
| 5 | RsiOversoldRecoveryDetector | Momentum | 1.0 | false | Rsi14 < 40 AND Rsi14 > Rsi14Prev ⇒ 1.0; 40-50 aralığı + rising ⇒ 0.5; yukarı ⇒ 0. |
| 6 | Ema9SlopeMomentumDetector | Trend | 1.0 | false | Ema9 > Ema9Prev AND Close > Ema9 ⇒ 1.0; sadece slope ⇒ 0.5; aksi 0. |
| 7 | DonchianBreakoutDetector | Range break | 1.5 | false | Curr close > Donchian20High of last 20 closed bars ⇒ 1.0; close > prev Donchian + retest ⇒ 0.5. |
| 8 | VolumeSurgeConfirmDetector | Confirm | 1.0 | true | CurrentTradeCount > AvgTradeCount20 × 1.2 ⇒ 1.0; 0.8-1.2 arası ⇒ 0.5; <0.8 ⇒ 0 (hard-gate ⇒ skip). |
| 9 | SpreadGuardDetector | Liquidity | 1.0 | true | (Ask-Bid)/Ask < %0.05 ⇒ 1.0; %0.05-0.10 ⇒ 0.5; >%0.10 ⇒ 0 (hard-gate). NOT: BookTicker erişimi composer'a inject edilir (snapshot bar-aligned değil). |
| 10 | AdxRegimeDetector | Regime | 1.0 | false | Adx14 ∈ [18, 35] ⇒ 1.0 (sweet spot); ∈ [12,18] veya [35,45] ⇒ 0.5; aksi 0. **Hard-gate DEĞİL** — Loop 80 ADX hard-gate 0 emit'e yol açtı, soft skor ile freq korunur. |

**Toplam tavan:** `Σ(weight × 1.0)` = 1.5+1.0+1.5+1.0+1.0+1.0+1.5+1.0+1.0+1.0 = **11.5 puan**.
**Default emit threshold:** `RequiredScore = 5.0` (~%43 — agresif emit, frekans odaklı). Per-coin override config'te.

### 24.12 Geometri — ATR-Bazlı, ADR-0023 R:R 1:2.5 Korunur

```jsonc
// per-coin override mümkün; default ortak
{
  "TpAtrMultiplier": 1.5,   // R:R 1:2.5 için
  "SlAtrMultiplier": 0.6,
  "MinTpPct": 0.0040, "MaxTpPct": 0.010,
  "MinSlPct": 0.0012, "MaxSlPct": 0.003,
  "MaxHoldMinutes": 45,     // 9 bar × 5m
  "BeMoveTriggerPct": 0.0010, "BeMoveOffsetPct": 0.0002
}
```

Skor-tier multiplier (mevcut KMS pattern devam):
- Skor 5-7 ⇒ Low tier (TpMul 1.3, SlMul 0.7, MaxHold 30dk).
- Skor 7-9 ⇒ Mid tier (default).
- Skor 9-11.5 ⇒ High tier (TpMul 1.8, SlMul 0.5, MaxHold 60dk).

### 24.13 DI Registration

```csharp
// Infrastructure/DependencyInjection.cs (özet)

// 1. Detector'ları singleton kaydet (stateless, paylaşımlı).
services.AddSingleton<IPatternDetector, BullishEngulfingDetector>();
services.AddSingleton<IPatternDetector, HammerReversalDetector>();
services.AddSingleton<IPatternDetector, BollingerLowerReversalDetector>();
services.AddSingleton<IPatternDetector, BollingerSqueezeBreakoutDetector>();
services.AddSingleton<IPatternDetector, RsiOversoldRecoveryDetector>();
services.AddSingleton<IPatternDetector, Ema9SlopeMomentumDetector>();
services.AddSingleton<IPatternDetector, DonchianBreakoutDetector>();
services.AddSingleton<IPatternDetector, VolumeSurgeConfirmDetector>();
services.AddSingleton<IPatternDetector, SpreadGuardDetector>();
services.AddSingleton<IPatternDetector, AdxRegimeDetector>();

// 2. Registry — IEnumerable<IPatternDetector> tüketir.
services.AddSingleton<IPatternRegistry, PatternRegistry>();

// 3. Composer — singleton, stateless.
services.AddSingleton<IPatternSignalComposer, WeightedScorePatternComposer>();

// 4. Tek IStrategyEvaluator — Composite.
services.AddSingleton<IStrategyEvaluator, PatternCompositeEvaluator>();

// SİL: KmsMomentumEvaluator + BbReversalEvaluator kayıtları.
```

### 24.14 Options Pattern — `PatternComposerOptions`

```csharp
// Application/Strategies/Patterns/PatternComposerOptions.cs
public sealed class PatternComposerOptions
{
    public decimal RequiredScore { get; set; } = 5.0m;
    public Dictionary<string, decimal> WeightOverrides { get; set; } = new();
    // Geometry
    public decimal TpAtrMultiplier { get; set; } = 1.5m;
    public decimal SlAtrMultiplier { get; set; } = 0.6m;
    public decimal MinTpPct { get; set; } = 0.004m;
    public decimal MaxTpPct { get; set; } = 0.010m;
    public decimal MinSlPct { get; set; } = 0.0012m;
    public decimal MaxSlPct { get; set; } = 0.003m;
    public int MaxHoldMinutes { get; set; } = 45;
    public int CooldownBarsAfterSignal { get; set; } = 3;
    public decimal BeMoveTriggerPct { get; set; } = 0.001m;
    public decimal BeMoveOffsetPct { get; set; } = 0.0002m;
    // Skor-tier override
    public int LowScoreTier { get; set; } = 5;
    public int HighScoreTier { get; set; } = 9;
    public decimal TpAtrMultiplierLow { get; set; } = 1.3m;
    public decimal TpAtrMultiplierHigh { get; set; } = 1.8m;
    public decimal SlAtrMultiplierLow { get; set; } = 0.7m;
    public decimal SlAtrMultiplierHigh { get; set; } = 0.5m;
    public int MaxHoldMinutesLow { get; set; } = 30;
    public int MaxHoldMinutesHigh { get; set; } = 60;
}
```

Per-coin parametre Strategy aggregate'in `ParametersJson`'unda kalır (mevcut pattern). Seed'lerde bu options instance'ı serialize edilir.

### 24.15 Test Stratejisi

| Katman | Test türü | Örnek |
|---|---|---|
| Domain | Unit (Strategy aggregate) | Mevcut testler aynı kalır — Type=PatternComposite ile çalışır. |
| Application | Pattern detector unit | Her detector için 3 senaryo: full-trigger / partial / no-trigger. Stateless ⇒ saf BarSnapshot in/out. ≥30 unit test toplam. |
| Application | Composer unit | Hard-gate skip / weight override / score threshold / tier mapping — 8 test. |
| Infrastructure | PatternCompositeEvaluator integration | IMarketIndicatorService mock + composer real → emit/skip karar zinciri. 4 test. |
| Infrastructure | Cooldown integration | Mevcut `CooldownServiceTests` dokunulmaz. |
| End-to-end | Playwright (tester agent) | Loop 81 boot sonrası 30dk içinde ≥3 emit, ≥1 fill, dashboard render. |

---

## Consequences

### Pozitif

1. **OCP — yeni pattern eklemek tek dosya:** `IPatternDetector` impl + DI satırı + (opsiyonel) seed weight override. Mevcut detector'lara/composer'a dokunma yok.
2. **SRP per detector:** Her dosya 1 pattern, ≤80 LOC, kolay test edilir.
3. **DRY snapshot:** Tek `BarSnapshot` 10 detector'ı besler. Mevcut iki snapshot'ın overlap'i (RSI, ATR, BBW, ADX) elenir.
4. **Frekans çözümü:** Soft scoring + hard-gate'i sadece **liquidity + volume**'da bırakmak emit oranını dramatik artırır (Loop 80 → hedef 30+/h).
5. **Audit:** ContextJson her emit'te hangi pattern hangi puanı verdi tam payload — postmortem 5dk'ya iner.
6. **Vision uyumu:** Memory `trading_vision.md` "5-10 chart pattern detector + ağırlıklı sinyal" birebir.
7. **Deprecated kod silinir (kural §13):** KMS+BBR evaluator + 2 snapshot + 10 seed satırı + Indicators.cs içinde sadece KMS/BBR'ye özel hiçbir şey kalmaz. Indicators.cs **yardımcı pure functions** olarak korunur (tüm detector'lar paylaşır).

### Negatif / Tradeoff

1. **Kısa vadeli regression riski:** 13 loop'tur tune edilen KMS+BBR parametre seti silinir; yeni detector'ların **gerçek piyasa skorları kalibre edilmedi**. İlk 1-2 loop frekans yüksek ama kalite düşük olabilir. Mitigation: ADR-0023 SL clip (%0.30 max) hâlâ aktif — single-trade max kayıp $0.30.
2. **Composer = central choke point:** Tüm emit/skip kararı tek dosyadan geçer. Bug burada production'ı durdurur. Mitigation: yüksek test coverage (composer unit ≥10 senaryo).
3. **BookTicker → SpreadGuard injection:** SpreadGuardDetector pure değildir (anlık BookTicker okur). Ya `BarSnapshot.SpreadPct` field eklenir (snapshot composer önce build edilir), ya da SpreadGuardDetector özel constructor `IBookTickerReader` alır. **Karar:** snapshot'a eklemek temiz — `IMarketIndicatorService` snapshot build sırasında BookTickerReader'ı sorgular. Detector'lar tamamen pure kalır.
4. **DB reset:** Migration `Loop81PatternPivot` Strategies + Signals + Positions + Orders + OrderFills full delete ister. Loop 67 reset migration'ından sonra çift reset normalleşti — kurum sermaye Paper $500'dan başlar.
5. **Logging volume:** Her bar 10 detector × 5 coin = 50 evaluation log. Composer skip dec log'u throttle edilmeli (mevcut StrategySignalSkippedEvent throttle pattern reuse).

### Nötr

1. CQRS sınırı **değişmez** — handler/command/query DTOlar aynı.
2. `Strategy` aggregate, `StrategySignal` entity, `StrategyEvaluatorRegistry`, `StrategyEvaluationHandler` — kod **sıfır satır** değişiklik.
3. MarkToMarketWorker, sizing, risk profile, fee accounting — **etkilenmez**.
4. Frontend Vue dashboard "active strategies" sayımı 5'e düşer (10 yerine) — UI değişmez, sadece liste kısalır.

---

## Alternatifler

### A. Mevcut KMS+BBR'yi koruyup parametre tune etmek

Loop 80 → 81 sadece BBW/ADX gevşet. **Reddedildi:** 13 loop boyunca tune denendi, frekans hedefini hâlâ tutturmuyor. Yapısal sorun (single-shape evaluator), parametre değil.

### B. Pattern başına ayrı Strategy aggregate (10 strateji × 5 coin = 50 strateji)

Detector = Strategy. **Reddedildi:** Kombinatoryal patlama, seed JSON 50 satır, ağırlıklı toplama yok (her pattern bağımsız emit ⇒ aynı bar'da çoklu duplicate fill riski).

### C. ML-tabanlı sinyal composer (lightweight gradient boosting)

Detector çıktıları feature; XGBoost gibi modelle emit kararı. **Reddedildi (şimdilik):** Veri seti yetersiz (KMS+BBR ile sadece ~25 emit toplam, ML training için <1000 örnek). Loop 81-90 manual weight ile veri biriktir, sonra ADR-0025 olarak ele al.

### D. Event-sourcing PatternSignal aggregate

Her pattern detection bir domain event. **Reddedildi:** Aşırı mühendislik. Pattern verisi türev, source-of-truth StrategySignal'dir.

---

## Migration Notları

1. **`Loop81PatternPivot` migration** — Strategies + StrategySignals + Positions + Orders + OrderFills tablo verisi `DELETE FROM`. Schema değişmez (PatternSignal tablosu **yok**). RiskProfiles korunur (config-seed her boot zaten override eder).
2. **Bot restart CB reset etmez** (Memory `reference_circuit_breaker_reset.md`); deploy sonrası `POST /api/risk/circuit-breaker/reset` (X-Admin-Key) gereksinimi loop boot script'ine eklenir.
3. Eski enum değerleri (KMS=1, BBR=2) `StrategyType.cs`'den **silinir**. `PatternComposite=3` yeni başlar; ordinal reuse yok (DB Type kolonunda 1/2 zaten DELETE'ten sonra mevcut değil).

## Kaynak

- ADR-0014 (pattern-based-scalping-reform) — bu kararla **superseded**, tarihsel ders olarak kalır.
- ADR-0015 (vwap-ema-hybrid) — `IMarketIndicatorService` shared snapshot deseni temel alındı.
- ADR-0023 (risk-first-tp-sl) — geometri R:R 1:2.5 korunur.
- Memory `trading_vision.md` — 5-10 chart pattern detector + ağırlıklı sinyal vizyonu.
- Memory `feedback_frekans_kartopu.md` — 30+/h frekans kuralı pivot tetikleyicisi.
- Memory `feedback_no_dead_code.md` — eski evaluator/snapshot/seed silme zorunluluğu.
- Loop 80 check-t270 — 90dk 0 emit, kuralı ihlal kanıtı.
- DDD reference: Vaughn Vernon — *Implementing DDD* §10 (Aggregate boundary "transactional consistency"); pattern detection **stateless evaluation** ⇒ aggregate gerekmiyor.
- Clean Architecture dependency rule: `IPatternDetector` Application'da, impl Infrastructure'da — Domain saf kalır.
