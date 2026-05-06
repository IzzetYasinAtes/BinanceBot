# 0027. Strateji Ailesi Pivot — Pattern Composite Erteleme + IStrategyEvaluator Çoğul Mimari

Date: 2026-05-03
Status: Proposed (Loop 111 lifecycle bug-fix sonrası — 43 loop 0 pozitif kapatma, fee/gross math negatif)
Relates to: ADR-0014 (superseded), ADR-0023 (superseded by 0026), ADR-0024 (Inactive — supersede edilmiyor, askıya alınıyor), ADR-0025 (korunur), ADR-0026 (R:R simetri tune — korunur)
Memory ref: `loop_discipline.md`, `trading_vision.md`, `feedback_frekans_kartopu.md`, `feedback_no_dead_code.md`, `feedback_no_session_split.md`

> **Özet:** Loop 80-110 boyunca tek-tip `PatternComposite` (ADR-0024) ailesi 43 loop boyunca **0 pozitif kapanış** verdi (~140-180 trade, kümülatif net **−$26.5+**, R:R fiili 1:15). Loop 111 lifecycle bug-fix dalgası (MarkToMarketWorker SL semantik, MaxHold timeout aktif, trailing peak SL update, PaperTrade reset realize) altyapı kayıplarını kapatır AMA stratejinin **edge** sorununu çözmez. Bu ADR ile tek monolitik strateji ailesi yerine **çoğul `IStrategyEvaluator` plug-in mimarisi** (Senaryo C) benimsenir; mevcut `PatternComposite` altyapısı **silinmez** ama `Status=Paused` olarak askıya alınır (Inactive deposu) — yeni strateji ailesi (binance-expert tavsiyesinden seçilen) **bağımsız `IStrategyEvaluator` implementasyonu** olarak eklenir, paralel test edilir, kazanan kalıcılaşır. Plug-in mimari 4 strateji ailesinin (swing, grid, breakout, arbitrage) **HEPSİNİ** taşıyabilir; binance-expert tavsiyesi geldiğinde §27.6'da seçim finalleştirilir. ADR-0024 deprecate **edilmiyor** — pattern altyapısı re-aktive edilebilir. ADR-0025 (Futures + TradeDirection + IExchangeClient) ve ADR-0026 (R:R simetri Loop 105 tune + Loop 106 Limit Pullback — implement edildiğinde) tamamen korunur.

---

## Context

### 27.1 Loop 80-110 Pattern Composite — 43 Loop 0 Pozitif

ADR-0024 Loop 81'de "tek monolitik evaluator yerine N pattern detector + WeightedScorePatternComposer + PatternCompositeEvaluator" kararı verildi. ADR-0025 Loop 92'de Spot → Futures pivotu yapıldı, simetrik 17 detector dosyasıyla Long/Short kova mimarisi eklendi. ADR-0026 Loop 105'te R:R 1:1 simetri tune'u uygulandı.

| Aralık | Loop | Mimari kilit | WR | Avg Win | Avg Loss | Realized |
|---|---|---|---|---|---|---|
| Spot Long-only | 80-91 | ADR-0024 baseline | düşük | - | -$0.40 | -$17.04 |
| Futures Long+Short | 92-104 | ADR-0025 + R:R 1:2.5 (ADR-0023) | %30 | $0.04 | -$0.60 | -$7.5 |
| R:R simetri | 105-110 | ADR-0026 Option C | ~%30-40 | $0.18 | -$0.24 | -$1.96 |
| Lifecycle bug-fix | 111 | (henüz çalışıyor) | ? | ? | ? | ? |

**Kümülatif:** ~43 loop, ~140-180 trade, **net −$26.5+**, **0 pozitif kapanış** (1 anlık tepe +$2.42 realize edilemedi — Loop 110 PaperTrade reset force-close DELETE), Paper $500 → ~$473.

**Pattern composite "edge" sorunu — kanıt:**
1. **R:R 1:2.5 nominal → 1:15 fiili** (avg win $0.04, avg loss $0.60) — TP nadir hit, SL baskın (ADR-0026 §26.2).
2. **R:R 1:1 simetri (Loop 105+) hâlâ pozitif değil** — TP yakınlaştı ama WR %50'yi geçmedi. Yapısal entry timing problemi (ADR-0026 §26.2: "buy at top" pattern).
3. **Pullback Limit (ADR-0026 Option A) henüz implement edilmedi** — Loop 105 zero-code tune'un tek başına yeterli olmadığı kanıtlandı.
4. **Pattern bias mismatch:** 17 detector (engulfing, hammer, donchian, RSI recovery, vb.) **bar-close + 5dk scalp** tasarlandı; testnet likidite + spread realism mainnet'ten farklı; "sahte breakout pattern" Loop 85 boyunca tekrarlandı (loops/loop_85, loops/loop_91 raporları).
5. **Frekans kuralı (CLAUDE.md §12) ihlali:** L91 3 trade/h, L100-104 ~5 trade/h — 30/h hedefi karşılanmadı; pattern tabanlı bar-close emit yapısal olarak frekans tavanı düşük.

### 27.2 Loop 111 Bug-Fix Sonrası Durum

PM brief'inde sıralanan bug'lar (lifecycle):

1. **MarkToMarketWorker BookTicker query** — `Symbol` VO `.Contains()` EF translate edemiyor → fallback. Fix: `Symbol.Value` projection.
2. **Signal freshness** — 5dk window MaxHold null bırakıyordu → MarkToMarket'ta MaxHoldDuration null check.
3. **Trailing peak SL update** — `Position.UpdatePeakAndCheckTrailing` yeni peak tespit ederken SL'yi yukarı taşımıyordu.
4. **PaperTrade reset force-close DELETE** — realize değil, doğrudan satır siliyordu (Loop 110'un +$2.42 anlık peak'i kayboldu). Fix: realize-and-keep variant.

**Bu fix'ler altyapı doğruluğunu kapatır AMA strateji edge sorununu çözmez.** Yani:
- ✓ Lifecycle düzelir → "açılan pozisyon doğru kapanır, peak hesabı doğru, reset audit korunur".
- ✗ "Stratejinin pozitif beklenti sağlaması" değişmez — pattern composite hâlâ R:R fiili negatif, bar-close emit "buy at top" devam eder.

Loop 112+ için karar: **strateji ailesi seviyesinde pivot** — sadece bug-fix yetmez, edge yenilemesi gerek.

### 27.3 Mevcut Mimari Envanteri — Korunan / Askıya Alınan / Yeni

```
┌──────────────────────────── DOMAIN (KORUNUR) ────────────────────────────┐
│  Strategy aggregate (Type, Status, ParametersJson, EmitSignal)           │
│  StrategySignal entity (Direction, SuggestedPrice/Stop/TP, ContextJson)  │
│  Position aggregate (Direction, Open/Mark/Close/Trailing/BE)             │
│  Order aggregate (Place/Limit/Cancel/Expire/RegisterFill)                │
│  TradeDirection enum, StrategyType enum                                  │
└──────────────────────────────────────────────────────────────────────────┘

┌─────────────────────── APPLICATION (PORTLAR KORUNUR) ────────────────────┐
│  IStrategyEvaluator (port) — ÇOĞUL implementation desteği eklenecek     │
│  IExchangeClient (ADR-0025 — KORUNUR)                                    │
│  IPaperFillSimulator (ADR-0025 + ADR-0026 — KORUNUR)                     │
│  IPatternDetector + IPatternSignalComposer + BarSnapshot                 │
│   → PASIF (Inactive deposu, Strategy.Status=Paused)                      │
└──────────────────────────────────────────────────────────────────────────┘

┌──────────────────────── INFRASTRUCTURE (KISMEN PASIF) ───────────────────┐
│  PatternCompositeEvaluator (IStrategyEvaluator impl) — KORUNUR (Paused)  │
│  17 IPatternDetector dosyası (Long+Short+Neutral) — KORUNUR (Paused)     │
│  WeightedScorePatternComposer — KORUNUR                                  │
│  BinanceFuturesClient (ADR-0025) — KORUNUR                               │
│  FuturesPaperFillSimulator (ADR-0025) — KORUNUR                          │
│  MarkToMarketWorker (ADR-0025 + Loop 111 fix) — KORUNUR                  │
│  PendingLimitFillWorker (ADR-0026 Option A — implement edilirse) — KORUNUR
│                                                                          │
│  → YENİ: <NewStrategy>Evaluator (binance-expert tavsiyesinden seçilen)   │
│         IStrategyEvaluator paralel impl, kendi Strategy seed'i           │
└──────────────────────────────────────────────────────────────────────────┘
```

**Net sınır:** Pattern altyapısı **silinmez** (CLAUDE.md §13 deprecated kod yasağı bu kez tetiklenmiyor — kod **deprecated değil**, **askıya alınmış** ve plug-in mimari sayesinde paralel/alternatif evaluator olarak yaşatılabilir). Yeni strateji ailesi bağımsız bir `IStrategyEvaluator` impl'i olarak eklenir; iki evaluator kayıtlı kalır, **Strategy seed'inde Status=Active olan hangisiyse** o emit eder.

### 27.4 Kullanıcı Vizyonu — `trading_vision.md` Reinterpret

Memory `trading_vision.md`: **"5-10 chart pattern detector, 5-10dk scalping, ağırlıklı sinyal, kartopu hedef."**

ADR-0024 bu vizyonu **birebir** uyguladı; 43 loop sonra net negatif. Vizyon **yanlış** değil, ama **tek** uygulama yolu olmadığı kanıtlandı. Plug-in mimari ile:
- Pattern composite (vizyon birebir) — **askıya alınır**, ileride re-aktive edilirse kolayca kalkar.
- Yeni aile (swing / grid / breakout / arbitrage) — vizyonun **dolaylı** yorumu (kartopu hedef + 30+/h frekans + ağırlıklı sinyal aynı kalabilir, "5-10 chart pattern detector" geçici olarak bekletilir).

Memory'ye kontradiksiyon **yok**: pattern detector vizyonu `trading_vision.md`'de tek mutlak gereksinim değil — "kartopu kar" finanssal hedef, "ağırlıklı sinyal" mimari prensip, "5-10dk scalping" zaman ölçeği. Bunlar plug-in evaluator içinde de uygulanabilir.

### 27.5 Frekans + Disiplin Kuralları (CLAUDE.md §11-12, Memory `loop_discipline.md`)

- **§11 saat dilimi yok** — yeni strateji 24/7 uniform.
- **§12 5 coin + 30+/h** — yeni evaluator bu hedefi tutmak zorunda.
- **§13 deprecated kod yasak** — pattern composite **askıya alma** ≠ deprecate; plug-in registry içinde Status=Paused olarak yaşar. Re-aktive olmazsa Loop 130+'da yeniden değerlendirilir (silmek için ayrı ADR).
- `loop_discipline.md` "kâr olunaya kadar devam" — pivot doğru zamanlamada (Loop 111 lifecycle fix sonrası; bug-fix tek başına yetmediği zaman strateji aile pivotu).

### 27.6 binance-expert Tavsiyesi (Bekleniyor)

Bu ADR yazılırken binance-expert paralel çalışıyor; tavsiye 4 aile arasından (swing-trade, grid, breakout, arbitrage) öneri verecek. ADR-0027 §27.7'de **çerçeve** her 4 aile için hazırdır; binance-expert tavsiyesi geldiğinde §27.7 ilgili alt başlık "**SEÇİLDİ**" olarak işaretlenir, diğer üçü "alternatif" altına taşınır. Architecture-plan.md commit listesi seçilen aile için spesifikleşir.

---

## Decision

### 27.7 Karar — Senaryo C (Plug-in `IStrategyEvaluator`) + Pattern Pause + Yeni Aile

**Üç ana karar:**

#### Karar A: Senaryo C (plug-in mimari) seçildi.

Senaryo A (pattern subsystem sil) **reddedildi:**
- 43 loop pattern altyapısı yatırımı (17 detector dosyası + composer + evaluator + 60+ test) silinince **geri dönüş kapısı kapanır**.
- `trading_vision.md` "5-10 chart pattern detector" tam silinmez, askıya alınır — vizyona saygı.
- Yeni strateji ailesi 30 loop sonra başarısız olursa pattern aileye dönüş bir-iki commit'e iner (sadece Strategy.Status=Active flip).
- CLAUDE.md §13 "deprecated kod yasağı" — pattern altyapısı deprecated değil, **paused active art**. Çoğul `IStrategyEvaluator` paradigması altında bu ayrım meşru.

Senaryo B (hibrit — pattern korunur + yeni eklenir, config flag) **reddedildi:**
- Senaryo C'nin alt kümesi; ama "config flag ile aktif olan seçilir" tasarımı plug-in'den daha az esnek.
- Senaryo B'de Strategy aggregate'in Status=Active'i **tek bir** evaluator için açılır; halbuki çoğul registry'de Strategy başına Type farklı olabilir, **5 coin × 5 strateji** matrisi mümkün.

**Senaryo C tasarımı:**

```csharp
// Application/Strategies/Evaluation/IStrategyEvaluator.cs (mevcut, KORUNUR)
public interface IStrategyEvaluator
{
    StrategyType Type { get; }            // hangi StrategyType bu evaluator'ı tetikler
    Task<StrategyEvaluation?> EvaluateAsync(
        long strategyId, string parametersJson, string symbol,
        IReadOnlyList<Kline> closedBars, CancellationToken ct);
}

// Infrastructure/Strategies/StrategyEvaluatorRegistry.cs (KORUNUR)
public sealed class StrategyEvaluatorRegistry : IStrategyEvaluatorRegistry
{
    private readonly IReadOnlyDictionary<StrategyType, IStrategyEvaluator> _byType;
    public StrategyEvaluatorRegistry(IEnumerable<IStrategyEvaluator> evaluators)
        => _byType = evaluators.ToDictionary(e => e.Type);
    public IStrategyEvaluator? Resolve(StrategyType type)
        => _byType.GetValueOrDefault(type);
}
```

**Mevcut altyapı zaten plug-in tasarımında** — `StrategyEvaluatorRegistry` `IEnumerable<IStrategyEvaluator>` constructor injection ile çoğul desteği var. Şu an tek entry (`PatternComposite=3`) var; ADR-0027 yeni `StrategyType` enum değer(ler)i ekler ve yeni `IStrategyEvaluator` impl(s) DI'ya register eder. **Application/Domain port değişmez**; yalnızca Infrastructure ve seed katmanı genişler.

#### Karar B: Pattern composite **paused**, silinmez.

ADR-0024 `PatternComposite=3` enum değeri **korunur**. Strategy aggregate'inde Type=PatternComposite olan kayıtların Status'ü **Paused**'a alınır (Loop 112+ migration'ı). PatternCompositeEvaluator + 17 detector + composer + 60+ test **dokunulmaz**. Re-aktivasyon: Strategy.Status=Active flip + bot restart yeterli.

ADR-0024 status: **Active (paused)** — supersede edilmez, alternatif evaluator olarak yaşar. `feedback_no_dead_code.md` kuralı: "deprecated kod silinir" — **paused ≠ deprecated**. Loop 130+ yeniden değerlendirilir (eğer 20 loop boyunca tek kez bile re-aktive edilmediyse silmek için ayrı ADR yazılır).

#### Karar C: Yeni strateji ailesi seçimi binance-expert tavsiyesine bağlı (4 aile çerçevesi).

binance-expert paralel çalışıyor; tavsiyesi 4 aileden birini seçecek. ADR-0027 §27.8-27.11 **HER 4 AİLE** için mimari iskeleti hazırlar:

| Aile | StrategyType ordinal | Evaluator dosyası | Yeni infra ihtiyacı | Frekans uyumu (30+/h) | Kompleksite |
|---|---|---|---|---|---|
| **A. Swing trade** (multi-bar trend follow) | `SwingTrade=4` | `SwingTradeEvaluator` | yok (mevcut Kline + IExchangeClient yeter) | düşük (3-10/h) — KURAL İHLAL RİSKİ | düşük |
| **B. Grid trading** (range-bound) | `GridTrading=5` | `GridTradingEvaluator` + `GridLevel` value object | yeni `GridState` snapshot tablosu (multi-level pending limit) | yüksek (50+/h) — uyumlu | orta |
| **C. Breakout** (Donchian + ATR expansion) | `Breakout=6` | `BreakoutEvaluator` | yok (mevcut Kline + ATR/Donchian indicator yeter; pattern altyapısının ALT KÜMESİ — DonchianBreakoutDetector reuse mümkün) | orta (10-20/h) — uyumlu | düşük |
| **D. Arbitrage** (funding/cross-pair) | `Arbitrage=7` | `ArbitrageEvaluator` | yeni `FundingRateSnapshot` table + multi-symbol simultaneous order | düşük-orta (5-15/h) — sınırda | yüksek |

binance-expert tavsiyesi geldiğinde `Decision Karar C` finalleştirilir; kalan 3 aile **alternatif** (§Alternatifler) altına taşınır. Bu ADR yazıldığı an aile seçimi **TBD** — sadece §27.13 Implementation Order için aile-spesifik commit listesi parametrize edilir.

### 27.8 Mimari Topoloji — Plug-in IStrategyEvaluator (Senaryo C tam)

```
┌─────────────────── DOMAIN (DEĞİŞMEZ) ────────────────────┐
│  StrategyType enum                                        │
│    PatternComposite = 3   (paused, ADR-0024)             │
│    SwingTrade      = 4    (yeni, opsiyonel)              │
│    GridTrading     = 5    (yeni, opsiyonel)              │
│    Breakout        = 6    (yeni, opsiyonel)              │
│    Arbitrage       = 7    (yeni, opsiyonel)              │
│                                                           │
│  Strategy.Status:                                         │
│    Active=3   → registry'den evaluator alır, emit eder   │
│    Paused=2   → emit etmez (Pattern bu kovaya alınır)    │
│    Draft=1    → init state                                │
└───────────────────────────────────────────────────────────┘
                         ▲ uses
                         │
┌──────────────── APPLICATION (PORTLAR DEĞİŞMEZ) ──────────┐
│  IStrategyEvaluator (port) — Type ile registry resolve   │
│  IStrategyEvaluatorRegistry (port — already exists)      │
│                                                           │
│  Yeni aile için yeni Application-layer port'lar:         │
│    [Aile-specific] — bkz. §27.9-27.11                    │
└───────────────────────────────────────────────────────────┘
                         ▲ implements
                         │
┌─────────── INFRASTRUCTURE (PARALEL EVALUATOR'LAR) ───────┐
│  PatternCompositeEvaluator (Paused, korunur)             │
│  <NewStrategy>Evaluator (Active, binance-expert seçim)   │
│                                                           │
│  StrategyEvaluationHandler:                              │
│    foreach (Strategy.Status==Active) → resolve(Type)     │
│      → evaluator.EvaluateAsync(...)                      │
│      → emit signal                                       │
│                                                           │
│  Mevcut Pattern detector + composer dosyaları DOKUNULMAZ.│
└───────────────────────────────────────────────────────────┘
```

**Çoğul Strategy seed:**

```csharp
// Infrastructure/Persistence/Configurations/StrategyConfiguration.cs HasData
new Strategy { Id=1, Type=PatternComposite, Status=Paused, Symbol="BTCUSDT", ... },
new Strategy { Id=6, Type=<NewType>,        Status=Active, Symbol="BTCUSDT", ... },
new Strategy { Id=7, Type=<NewType>,        Status=Active, Symbol="ETHUSDT", ... },
// 5 coin × <NewType> = 5 yeni Active row
```

`StrategyEvaluationHandler` (mevcut): `KlineClosedEvent` ⇒ `Strategies.Where(Status=Active)` ⇒ registry resolve ⇒ evaluator.EvaluateAsync. **Hiçbir handler kodu değişmez.**

### 27.9 Aile A — SwingTrade (multi-bar trend follow)

**Niyet:** 1h/4h trend yönünde 5m bar entry, 6-24h hold. EMA50 > EMA200 + ADX > 25 + pullback to EMA21.

**Domain etkisi:** Sıfır. Mevcut Position.MaxHoldDuration null ya da uzun (24h) seçilir.

**Application port:**
```csharp
// Application/Strategies/Swing/SwingTradeEvaluator.cs
public sealed class SwingTradeEvaluator : IStrategyEvaluator
{
    public StrategyType Type => StrategyType.SwingTrade;
    public Task<StrategyEvaluation?> EvaluateAsync(...)
    {
        // 1. Higher TF trend: GetKlines(symbol, 1h, 200) → EMA50/EMA200
        // 2. ADX(14) on 1h
        // 3. 5m pullback: close < EMA21 + RSI(14) < 40 (Long) / > 60 (Short)
        // 4. Geometry: SL = ATR(1h) × 1.0, TP = SL × 2.0, MaxHold 24h.
    }
}
```

**Yeni infra:** Yok — mevcut `IExchangeClient.GetKlinesAsync` (testnet'te zaten var) + Indicator helper'ları yeter.

**Frekans tahmini:** 3-10 emit/h × 5 coin = 15-50/h. **CLAUDE.md §12 sınırda** — kural %30 genişlik tolere ediyor.

### 27.10 Aile B — GridTrading (range-bound)

**Niyet:** Range tespit edilen sembolde N seviyeli grid pending limit; her seviye dolduğunda ters yön TP. Range bozulursa tüm grid cancel.

**Domain etkisi:** Yeni `GridLevel` value object, `Strategy` aggregate'ine `GridState` (LevelCount, BasePrice, StepPct) eklenir. Migration: `Strategies.GridStateJson NVARCHAR(MAX) NULL`.

**Application port:**
```csharp
public sealed class GridTradingEvaluator : IStrategyEvaluator
{
    public StrategyType Type => StrategyType.GridTrading;
    public Task<StrategyEvaluation?> EvaluateAsync(...)
    {
        // 1. Range tespit: 24h high/low spread / mid < %2 (low volatility)
        // 2. Grid setup: BasePrice = midpoint, 5 level × ±%0.4
        // 3. Her level için ayrı StrategyEvaluation (multi-emit per cycle).
        //    NOT: IStrategyEvaluator.EvaluateAsync TEK StrategyEvaluation döner —
        //    grid 5 level için multi-emit interface refactor gerektirir.
    }
}
```

**INTERFACE REFACTOR ZORUNLULUĞU (Aile B seçilirse):**

```csharp
// Application/Strategies/Evaluation/IStrategyEvaluator.cs (POTANSİYEL CHANGE)
Task<IReadOnlyList<StrategyEvaluation>> EvaluateAsync(...)  // tek değil, çoklu döner
```

Bu **breaking change** — PatternComposite + diğer aileler de adapte olmalı. Maliyet 2-3 commit (handler güncelleme + tüm test mock'ları).

**Yeni infra:** `GridStateRepository`, `PendingLimitFillWorker` (ADR-0026 zaten var), multi-level idempotency (clientOrderId schema `grid-{StrategyId}-{Level}-{Cycle}`).

**Frekans tahmini:** 50+/h (5 level × range volatility) — **CLAUDE.md §12 mükemmel uyumlu**.

### 27.11 Aile C — Breakout (Donchian + ATR expansion)

**Niyet:** Donchian20 üst band kırma + ATR genişleme + volume surge. Pattern altyapısının **alt kümesi** — `DonchianBreakoutDetector` mevcut.

**Domain etkisi:** Sıfır.

**Application port:**
```csharp
public sealed class BreakoutEvaluator : IStrategyEvaluator
{
    public StrategyType Type => StrategyType.Breakout;
    public Task<StrategyEvaluation?> EvaluateAsync(...)
    {
        // 1. Donchian20High break (Long) / Low break (Short)
        // 2. ATR14 > ATR14_avg10 × 1.3 (volatilite genişledi)
        // 3. Volume current > avg20 × 1.5
        // 4. Geometry: SL = ATR × 1.5 (genişlemiş ATR daha geniş SL haklı kılar),
        //    TP = SL × 1.5 (R:R 1:1.5 — breakout follow-through), MaxHold 4h.
    }
}
```

**Mevcut `DonchianBreakoutDetector` reuse:** Pattern altyapısı paused ama detector dosyaları değiştirilebilir; BreakoutEvaluator `IPatternDetector`'ı **import etmez** — kendi minimal Donchian helper'ı yazar (Application'ın Pattern subsystem'ine bağımlılık ihlali olmasın). Indicator hesabı `Infrastructure/Strategies/Evaluators/Indicators.cs` (mevcut helper) **paylaşılır** — namespace zaten `BinanceBot.Infrastructure.Strategies.Evaluators` (tüm evaluator'lar erişir).

**Yeni infra:** Yok.

**Frekans tahmini:** 10-20/h × 5 coin = 50-100/h. **CLAUDE.md §12 uyumlu.**

### 27.12 Aile D — Arbitrage (funding rate / cross-pair)

**Niyet:** Funding rate threshold üstü → ters yön pozisyon (funding payment kazan). Veya BTCUSDT/ETHUSDT korelasyon spread → mean revert.

**Domain etkisi:** Yeni `FundingRateSnapshot` aggregate + tablo. Multi-symbol tek StrategySignal'da iki Direction (paired trade) — **Strategy aggregate refactor gerek** (1 strategy = 1 symbol kuralı bozulur). Yüksek mimari maliyet.

**Application port:** karmaşık — IStrategyEvaluator interface'i symbol parametre alıyor; pair için iki kez Evaluate çağırılıp eşleştirme dış handler'da yapılmalı.

**Yeni infra:** `FuturesFundingRateWorker` (ADR-0025 §25.14 öneri olarak vardı, implement edilmedi), `FundingRateSnapshot` table + migration.

**Frekans tahmini:** 5-15/h — **CLAUDE.md §12 sınırda.**

**Komplekslik:** En yüksek (3 yeni infra + Strategy aggregate refactor + handler chain değişimi). 20-25 commit.

### 27.13 Implementation Order — Aile-Bağımsız Plug-in İskeleti (10-14 commit)

binance-expert tavsiyesi gelmeden önce bağımsız çekirdek hazırlanabilir (commit 1-4). Aile seçildiğinde 5-14 spesifikleşir.

**Commit listesi (her satır ayrı atomik commit, `development` branch CLAUDE.md §10):**

#### Çekirdek (aile-bağımsız, 4 commit)

1. **Domain — StrategyType enum genişlemesi.**
   - `Domain/Strategies/StrategyEnums.cs` — `SwingTrade=4 | GridTrading=5 | Breakout=6 | Arbitrage=7` opsiyonlarından **seçilen** eklenir; diğer 3 değer **eklenmez** (gereksiz enum kalıntısı yok, CLAUDE.md §13).
   - `PatternComposite=3` korunur (Paused durum için enum hâlâ gerekli).
   - Test: `Domain.StrategyEnumTests` mevcut tek değer testi parametrize edilir.

2. **Migration — `Loop112StrategyPivot` (PatternComposite Paused + Yeni Strategy seed).**
   - `Infrastructure/Persistence/Migrations/<timestamp>_Loop112StrategyPivot.cs`:
     - `UPDATE Strategies SET Status = 2 WHERE Type = 3`  — Pattern → Paused.
     - 5 yeni Strategy row INSERT (Type=<NewType>, Status=Active, 5 coin × Active).
     - Eski PatternComposite ParametersJson dokunulmaz (re-aktive olursa lazım).
   - **Veri silmez** — pattern emit/signal/position/order audit kalır.
   - `dotnet ef migrations add Loop112StrategyPivot --project Infrastructure --startup-project Api`.

3. **Application — IStrategyEvaluator çoğul desteği audit + handler test.**
   - `Application/Strategies/Evaluation/IStrategyEvaluator.cs` — interface dokunulmaz (zaten plug-in).
   - `Infrastructure/Strategies/StrategyEvaluatorRegistry.cs` — `Resolve(Type)` metodunda `null` dönüş "evaluator missing" log + skip (mevcut davranış).
   - `Infrastructure/Strategies/StrategyEvaluationHandler.cs` — `Strategies.Where(s => s.Status == Active)` çoğul iterate (mevcut davranış); audit log "evaluating N strategies" eklenir.
   - Test: `StrategyEvaluatorRegistryTests` 2 entry registry resolve testi (PatternComposite + NewType).

4. **Frontend — Vue dashboard "active strategies" listesinde Paused gösterimi.**
   - `wwwroot/js/components/StrategyList.js` (veya equivalent) — Status badge: Active=yeşil, Paused=gri, Draft=sarı.
   - Tester Playwright: dashboard'da PatternComposite "Paused" gri, NewType "Active" yeşil görünür.

#### Aile-spesifik (binance-expert tavsiyesi sonrası, 6-10 commit)

Burada A/B/C/D aileden hangisi seçildiyse onun §27.9-27.12 ihtiyaç listesi commit zincirine açılır.

**Aile A (Swing) örneği — 6 commit:**

5. Application — `SwingTradeEvaluator.cs` iskelet + `SwingTradeOptions.cs`.
6. Infrastructure — `Indicators.cs`'e EMA50/EMA200 1h hesabı reuse helper'ı.
7. Application — `SwingTradeEvaluator` higher-TF trend resolution.
8. Application — `SwingTradeEvaluator` 5m pullback entry + geometry.
9. Test — `SwingTradeEvaluatorTests` 6 senaryo (Long pullback / Short pullback / no trend / no ADX / cooldown / max hold).
10. DI — `Api/Program.cs` `services.AddSingleton<IStrategyEvaluator, SwingTradeEvaluator>()` register.

**Aile B (Grid) örneği — 10 commit:**

5. Domain — `GridLevel` VO + `Strategy.GridState` config (additive).
6. Migration — `Strategies.GridStateJson NVARCHAR(MAX) NULL`.
7. **Application — IStrategyEvaluator multi-emit refactor** (`Task<IReadOnlyList<StrategyEvaluation>>`).
8. Application — `PatternCompositeEvaluator` adapt (single → list with 1 item).
9. Application — `GridTradingEvaluator.cs` + `GridTradingOptions.cs`.
10. Application — Range detection + multi-level signal emit.
11. Infrastructure — `GridStateRepository` + idempotent emit per cycle.
12. Test — Range detect (3) + multi-level emit (4) + range-break cancel-all (2).
13. Worker — `GridStateRebalanceWorker` (range bozulduğunda cancel + new grid).
14. DI — register + Program.cs.

**Aile C (Breakout) örneği — 6 commit:**

5. Application — `BreakoutEvaluator.cs` + `BreakoutOptions.cs`.
6. Infrastructure — `Indicators.cs` Donchian + ATR expansion helper (mevcut zaten kısmen var).
7. Application — Volume surge confirmation.
8. Application — Geometry (R:R 1:1.5, MaxHold 4h).
9. Test — `BreakoutEvaluatorTests` 6 senaryo.
10. DI — register + seed update.

**Aile D (Arbitrage) örneği — 14 commit:**

5. Domain — `FundingRateSnapshot` aggregate.
6. Migration — `FundingRateSnapshots` table.
7. Infrastructure — `FuturesFundingRateWorker` (8h polling).
8. Application — `IExchangeClient.GetFundingRateAsync(symbol)` port (yeni method).
9. Infrastructure — BinanceFuturesClient impl (`/fapi/v1/fundingRate`).
10. Application — `ArbitrageEvaluator.cs` funding-based emit.
11. Application — Strategy aggregate "paired symbol" refactor (additive Symbol2 nullable field).
12. Migration — `Strategies.SecondarySymbol` nullable.
13. Application — Multi-symbol StrategySignal emit (handler chain refactor).
14-18. Test + DI + integration.

### 27.14 Reviewer Kontrol Noktaları

- ADR-0024 PatternComposite altyapısı **silinmedi** (Pattern detector dosya sayısı 17 sabit).
- ADR-0025 IExchangeClient + TradeDirection korundu.
- ADR-0026 R:R simetri parametreleri korundu (yeni evaluator de bu R:R'a saygı duyuyor).
- Yeni `StrategyType` enum değeri tek (gereksiz 3 alternatif eklenmedi).
- Migration veri silmez (pattern signals/positions audit korunur).
- DI registry'de iki evaluator var (resolve count 2).
- `StrategyEvaluationHandler` çoğul `Active` strategy iterate eder.
- 0 deprecated kod yorumu eklenmedi.

### 27.15 Tester (Playwright) Done-Definition

Loop 112 boot 30dk içinde:
- Dashboard "Strategies" listesinde 2 grup: Paused (PatternComposite, gri) + Active (NewType, yeşil).
- ≥3 emit yeni evaluator'dan (StrategySignal.ContextJson içinde "type": "<new-type>").
- 0 emit Paused PatternComposite'ten.
- En az 1 fill, en az 1 close (TP veya SL).
- 0 unhandled exception.
- ADR-0006 mainnet guard ihlali yok.

---

## Consequences

### Pozitif

1. **Pattern altyapısı yatırımı korunur** — 43 loop boyunca yazılan 17 detector + composer + 60+ test re-aktivasyon hazır kalır. Geri dönüş maliyeti: Strategy.Status=Active flip + bot restart (1 SQL update + 1 deploy).
2. **Plug-in mimari OCP** — yeni strateji ailesi eklemek = yeni IStrategyEvaluator dosyası + DI satırı + Strategy seed. Mevcut evaluator'a dokunma sıfır.
3. **Çoğul Strategy seed esneklik** — 5 coin × 1 strateji **veya** 5 coin × 2 strateji (örneğin BTC+ETH SwingTrade, ADA+SOL+BNB Breakout) paralel çalışabilir. Konfigürasyon Strategy.Status flip ile.
4. **Future-proof** — binance-expert tavsiyesi kötü çıkarsa veya Loop 130'da yeni aile eklemek gerekirse, mimari hazır. ADR-0028 sadece yeni evaluator dosyası ekler.
5. **CLAUDE.md §13 uyumu (incelikli):** Pattern altyapısı **deprecated değil paused**; deprecated kod yasağı tetiklenmez. "20 loop hiç re-aktive edilmediyse silmek" gelecekteki ADR'a (örn. ADR-0030) bırakılır.
6. **ADR-0026 Option A / B yatırımı korunur** — Limit pullback worker pattern composite için tasarlandı ama yeni evaluator de Limit emit edebilir (`PlaceOrderCommand.Type=Limit` zaten mevcut, herhangi evaluator kullanır).
7. **Risk + Position altyapısı (ADR-0025) tüm aileler için ortak** — TradeDirection, Position.Direction, MarkToMarketWorker SL/TP/BE/Trailing; her yeni evaluator ücretsiz miras alır.

### Negatif / Tradeoff

1. **Pattern altyapısı "ölü kod" olarak yaşar** — disk + maintenance yükü. Mitigation: Loop 130'da audit; 20 loop re-aktive edilmedi ise silme ADR'ı.
2. **Dual evaluator karmaşıklığı** — debug ederken "hangi evaluator emit etti" log discipline zorunlu. Mitigation: `StrategySignal.ContextJson.type` field her zaman dolu, evaluator adı log'larda zorunlu.
3. **Aile B (Grid) seçilirse interface refactor** — `IStrategyEvaluator` multi-emit (`Task<IReadOnlyList<StrategyEvaluation>>`) tüm evaluator'ları etkiler. Maliyet 2-3 commit ek.
4. **Loop 112 başlangıçta yeni evaluator kalibre değil** — pattern composite gibi 5-10 loop tune kazanma süreci olabilir. Mitigation: ADR-0023 SL clip korundu (max single-trade kayıp $0.30); Loop 112 ilk 4h beklenen net -$0.50 ile -$2.00 arası, kabul.
5. **Migration `Loop112StrategyPivot` veri silmez ama Strategy.Status=Paused yan etkili** — eski PatternComposite emit'leri durur; Paper $500 sermaye yeni evaluator'dan. Audit yarım kalmaz.
6. **binance-expert tavsiyesi gecikirse pivot bekler** — ADR-0027 §27.7 Karar C TBD; Loop 112 evaluator olmadan başlayamaz. Mitigation: çekirdek 4 commit (StrategyType enum + migration + registry + frontend) tavsiye gelmeden yapılabilir; aile-spesifik 6-10 commit tavsiye sonrası.

### Nötr

1. ADR-0006 (testnet-first), ADR-0008 (TradingMode), ADR-0011 (sizing), ADR-0020 (fee accounting), ADR-0025 (Futures), ADR-0026 (R:R simetri + Limit pullback) — etkilenmez.
2. CQRS sınırı değişmez — aynı `KlineClosedEvent` → `StrategyEvaluationHandler` → `IStrategyEvaluator` zinciri.
3. Position aggregate, MarkToMarketWorker, FuturesPaperFillSimulator — etkilenmez.
4. Frontend Vue dashboard değişimi minimal: Status badge eklenir, evaluator type kolonu zaten ContextJson'dan render edilebilir.
5. Loop 111 lifecycle bug-fix sonuçları (MarkToMarketWorker SL semantik, MaxHold timeout, trailing peak SL update, PaperTrade reset) **tüm aileler için ortak fayda** — yeni evaluator de bu fix'lerden yararlanır.

---

## Alternatifler

### A. Senaryo A — Tam Pivot, Pattern Subsystem Sil (Reddedildi, §27.7 Karar A)

ADR-0024 superseded; 17 detector + composer + evaluator + 60+ test silinir; tek StrategyType (yeni); Loop 81 reset migration tekrarı.

**Reddedildi:**
- 43 loop yatırımı (17 detector dosyası + ~80 test) **geri dönüşsüz** kayıp olur.
- "Yeni strateji 30 loop sonra başarısız" senaryosunda pattern aileye dönüş "yeniden yaz" anlamına gelir (1-2 hafta backend-dev).
- `trading_vision.md` "5-10 chart pattern detector" vizyonu silmek **kullanıcı vizyonuyla çelişir** (askıya alma değil, vazgeçme).
- CLAUDE.md §13 deprecated kod yasağı zorlama yorumu — **paused ≠ deprecated**, plug-in registry içinde meşru yaşam alanı var.
- 15-20 commit ağır iş; çekirdek + aile-spesifik plug-in 10-14 commit ile aynı bilgi aktarımı sağlanır.

### B. Senaryo B — Hibrit, Config Flag (Reddedildi, §27.7 Karar A)

PatternComposite Inactive kalır; yeni StrategyType eklenir; bot config flag ile (örneğin `Strategy:ActiveType=NewType`) hangisinin emit edeceğini seçer.

**Reddedildi:**
- Senaryo C'nin **eksik alt kümesi** — flag tek bir aktif aileye izin verir; halbuki çoğul Strategy.Status=Active matrisi 5 coin × N aile esnekliği sağlar.
- "Inactive" status tanımı kafa karıştırıcı (Status enum: Draft/Paused/Active — 3 değer; Inactive ≠ Status.Paused mı?).
- Config flag = magic string runtime branching; plug-in registry compile-time DI register daha temiz.
- 10-15 commit, Senaryo C ile yakın maliyet, daha az esnek.

### C. Senaryo C — Plug-in IStrategyEvaluator (SEÇİLDİ, §27.7 Karar A)

Bu ADR'ın ana kararı. Detayları yukarıda.

### D. Strateji ailesi seçimi: Swing (Aile A)

§27.9 — düşük komplekslik, mevcut altyapı yeterli. Frekans 30+/h sınırda — ölçek için 3-5 coin paralel SwingTrade gerekli.

**Avantaj:** En düşük commit (6); hemen başlatılabilir.
**Dezavantaj:** 5-10dk scalping vizyonuyla ters (swing 6-24h hold); CLAUDE.md §12 frekans kuralı sınırda.

### E. Strateji ailesi seçimi: Grid (Aile B)

§27.10 — orta komplekslik, IStrategyEvaluator multi-emit refactor gerek.

**Avantaj:** Frekans 50+/h kolay sağlanır (range bound vol pencerelerinde); kartopu vizyonu (`feedback_frekans_kartopu.md`) ideal eşleşme.
**Dezavantaj:** Range tespit yanlışsa breakout zamanında zarar büyük (tüm grid ters yönde dolar); range bozulma cancel-all worker karmaşık.

### F. Strateji ailesi seçimi: Breakout (Aile C)

§27.11 — düşük komplekslik, mevcut Donchian + ATR helper'ları reuse.

**Avantaj:** Pattern altyapısının "alt kümesi" — kavramsal süreklilik (`DonchianBreakoutDetector` zaten test edilmiş); kanıtlı algoritma (Turtle Trading vb. tarihsel).
**Dezavantaj:** "Sahte breakout" Loop 85-91'de pattern composite'in zaten yaşadığı sorun — breakout-only bu riski 2× yaşar; volume + ATR confirmation ile mitigation ama %100 değil.

### G. Strateji ailesi seçimi: Arbitrage (Aile D)

§27.12 — yüksek komplekslik, yeni domain concept (FundingRateSnapshot) + multi-symbol Strategy refactor.

**Avantaj:** Funding payment "neredeyse risk-free" gelir (yön tarafsız, sadece rate yüksekken); cross-pair mean revert güçlü edge.
**Dezavantaj:** 14-18 commit; Strategy aggregate 1-symbol kuralı bozulur; mainnet'te likidite/funding kalibre ayrı süreç. Loop 112'de **çok ağır** — Loop 120+ ileriye atılması daha uygun.

### H. Pattern composite "kalıcı sil + yeni aile yaz" — sıralı yaklaşım

Önce ADR-0028 PatternComposite silinir (pattern reset migration), sonra ADR-0029 yeni aile eklenir.

**Reddedildi:**
- İki ADR + iki migration + iki test pass döngüsü = ~3 hafta backend-dev.
- Plug-in mimari ile aynı sonuç tek ADR'da (paused) elde edilir.
- Pattern silmenin acelesi yok — disk maliyeti ihmal edilebilir, maintenance yükü minimal (test'ler ayrı namespace, build koparmaz).

---

## Migration Notları

1. **`Loop112StrategyPivot` migration:**
   - `UPDATE Strategies SET Status = 2 WHERE Type = 3` — PatternComposite Paused.
   - `INSERT INTO Strategies (Type=<NewType>, Status=3, Symbol, ParametersJson, ...)` × 5 coin.
   - **Veri silmez**: StrategySignals, Positions, Orders, OrderFills audit korunur.
   - Idempotent: `WHERE NOT EXISTS` guard ile re-run safe.

2. **Migration sırası (binance-expert tavsiyesi gelmeden çekirdek):**
   - StrategyType enum güncellemesi (kod commit 1) → migration **gerekmez** (enum int kolon, yeni değer için schema değişmez).
   - `Loop112StrategyPivot` (commit 2) → tek migration, çekirdek tamamlandığında uygulanır.

3. **Aile-spesifik migration (varsa, §27.13'e göre):**
   - Aile A (Swing): 0 migration.
   - Aile B (Grid): `AddStrategyGridState` (`Strategies.GridStateJson NVARCHAR(MAX) NULL`).
   - Aile C (Breakout): 0 migration.
   - Aile D (Arbitrage): `AddFundingRateSnapshots` table + `AddStrategySecondarySymbol` (`Strategies.SecondarySymbol NVARCHAR(20) NULL`).

4. **Bot restart sonrası `POST /api/risk/circuit-breaker/reset` (X-Admin-Key)** — `reference_circuit_breaker_reset.md`. Strategy seed Active flip CB durumunu etkilemez ama deploy sonrası standart prosedür.

5. **Re-aktivasyon (gelecek):** Pattern composite re-aktive olmak istendiğinde:
   ```sql
   UPDATE Strategies SET Status = 3 WHERE Type = 3;
   UPDATE Strategies SET Status = 2 WHERE Type = <NewType>;
   ```
   + bot restart + CB reset. **Migration gerekmez**, sadece data flip.

6. **Loop 130 audit:** 20 loop boyunca PatternComposite Paused kaldıysa silmek için yeni ADR (örn. ADR-0030). Bu ADR-0027'yi superseded etmez — sadece pattern altyapısını siler.

---

## Kaynak

- ADR-0014 (pattern-based-scalping-reform) — superseded by ADR-0024.
- ADR-0023 (risk-first-tp-sl-asymmetry) — superseded by ADR-0026.
- ADR-0024 (pattern-based-scalping) — Pattern composite altyapı kararı; bu ADR ile **paused** (supersede edilmiyor).
- ADR-0025 (futures-short-pivot) — TradeDirection + IExchangeClient + Position.Direction korunur.
- ADR-0026 (entry-timing-fix) — R:R 1:1 simetri + Limit pullback (Option A) korunur.
- Memory `loop_discipline.md` — kâr olunaya kadar 4h loop disiplini (pivot doğru zamanda).
- Memory `trading_vision.md` — pattern detector vizyonu **askıya alma** değil **vazgeçme** anlamına gelmemeli.
- Memory `feedback_frekans_kartopu.md` — 30+/h kuralı yeni aile seçiminin ana sınır şartı.
- Memory `feedback_no_dead_code.md` — paused ≠ deprecated; çoğul evaluator paradigmasında pattern altyapısı meşru yaşam alanı bulur.
- Memory `feedback_no_session_split.md` — yeni aile 24/7 uniform.
- Loop 80-110 raporları — pattern composite 0 pozitif kanıtı.
- Loop 111 boot.md — lifecycle bug-fix sonrası strateji edge sorununun ayrıştırılması.
- DDD reference: Vaughn Vernon — *Implementing DDD* §10 — Strategy aggregate identity (Type + Symbol) çoğul evaluator'la uyumlu, aggregate boundary değişmez.
- Clean Architecture dependency rule: yeni evaluator Application'da port'a (IStrategyEvaluator), Infrastructure'da impl'e bağlanır; Domain saf.
- Plug-in pattern: [Microsoft Learn — Eventing Composition](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/) — Strategy pattern + Registry deseni.
