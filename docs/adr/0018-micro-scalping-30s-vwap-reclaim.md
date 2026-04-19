# 0018. Micro-Scalping 30s VWAP Reclaim — BNB-Discount Fee Sim + $5.10 Floor

Date: 2026-04-19
Status: Accepted
Supersedes strategy layer of: [ADR-0016 VWAP-EMA Hybrid V2 Tuned](./0016-vwap-ema-hybrid-v2-tuned.md)
Supersedes sizing floor of: [ADR-0017 TimeStop/Duplicate/Sizing](./0017-timestop-mapping-duplicate-protection-sizing.md)
Relates to: [ADR-0011 Equity-Aware Sizing](./0011-equity-aware-sizing-and-risk-tracking.md), [ADR-0014 Pattern-Based Scalping Reform](./0014-pattern-based-scalping-reform.md), [ADR-0015 VWAP-EMA Hybrid Strategy](./0015-vwap-ema-hybrid-strategy.md)

> Bu ADR üç eksende karar verir: (A) **stratejik reform** — 1m `VwapEmaHybrid`'den 30sn kline bazlı `MicroScalperVwapEma30s` tipi türetilir; BNB discount varsayımıyla %0.60 TP / %0.35 SL / 2dk TimeStop; 4 sembol (BTC/ETH/BNB/XRP). (B) **sizing floor düzeltme** — `max(equity × 0.01, 5.10)` USD; Binance canlı API `minNotional = 5.00` zorlamasına + precision buffer 0.10'a uyum. (C) **paper-mode fee simülasyonu** — testnet `fee = 0` gerçekliğinin mainnet'e öğretici olmaması için Infrastructure katmanında sabit `0.00075` round-trip internal deduction. Binance WS `@kline_30s` stream, `ISystemEventPublisher` + aggregate sınırları dokunulmaz.

## Context

### 18.1 Loop 22 + 22c Halt Kanıtı — İki Boyutlu Başarısızlık

Loop 22 (ADR-0016 tuned VwapEma, 45 dk canlı paper):

| Gözlem | Sayısal Kanıt | Beklenen | Root Cause |
|---|---|---|---|
| 7 signal emit, 0 TP hit | 45 dk | 4-8 sinyal/saat, WR ≥36% breakeven | Sinyal sıklığı OK, ama duplicate + sizing + TimeStop bug'ları |
| Cash drift `−$18.75` | 2 açık BNB poz ~$78 notional | $20-40 | ADR-0017 hotfix |
| BNB pos 31.5 dk açık | `MaxHoldMinutes=10` seed'te | TimeStop 10 dk'da close | ContextJson key mismatch |

Loop 22c hotfix-2 (ADR-0017 uygulandı, 85 dk canlı paper):

| Gözlem | Sayısal Kanıt |
|---|---|
| 4 signal emit, 4 sizing skipped | `effectiveMinNotional` floor formülü reject |
| BTC entry cap clamp $20 → tp `$20 × 1.007 ≈ $20.14` | Hedef notional user iradesine uymuyor — kullanıcı $1 istiyordu |
| XRP/BNB precision ceiling 5 qty → $25+ notional | LOT_SIZE yukarı yuvarlama, hedef aşıldı |

### 18.2 Kullanıcı Komple Reform Emri (Loop 23 boot)

Birebir (`loops/loop_23/reform-brief.md`):

> "biz bu algoritmayı komple değiştirelim böyle olmayacak hiç işlem yok... dakikada 2-3 işlem açık olsun... saatte en az 180 işlem gibi bişeyler... 30sn 50sn 1dk gibi süreçlerde karlardan yararlanarak kartopu etkisi ile parayı büyütmeliyiz... Loop 24'te saate en az 150 işlem yapılıyor ve kar ediyor olmamız gerek"

Hedefler:
- **Saatte ≥150 trade** (dakikada 2.5-3)
- **Net kar** (fee drag sonrası)
- **Sizing alt:** $1 USD (kullanıcı: "mantıksızsa değiştirebilirsin, binance-expert araştırsın")
- **Timeframe:** 30sn-1dk
- **MaxOpenPositions:** 3-4 paralel (her symbol 1)
- **Semboller:** BTC/ETH/BNB/XRP — spekülasyondan etkilenmeyen major coin'ler

### 18.3 binance-expert Canlı API AR-GE (Loop 23)

Ayrıntı: [`loops/loop_23/research-micro-scalping.md`](../../loops/loop_23/research-micro-scalping.md) (499 satır, 2026-04-19 canlı).

Kritik bulgular:

**minNotional KATI:**

| Sembol | minNotional | minQty | stepSize |
|---|---|---|---|
| BTCUSDT | 5.00 USD | 0.00001 BTC | 0.00001 BTC |
| ETHUSDT | 5.00 USD | 0.0001 ETH | 0.0001 ETH |
| BNBUSDT | 5.00 USD | 0.001 BNB | 0.001 BNB |
| XRPUSDT | 5.00 USD | 0.1 XRP | 0.1 XRP |

Testnet = mainnet aynı filtreler (`applyMinToMarket=true`). **$1 sizing imkansız — API -1013 MIN_NOTIONAL hatası.** Yeni floor: **$5.10** (5.00 min + 0.10 precision/slippage/fee buffer).

**Fee matematiği:**
- Spot (VIP 0): maker = taker = **%0.10** (reform-brief'teki maker=0 YANLIŞ; futures asimetrisi spot'ta yok)
- BNB discount: **%0.075** (hold BNB + enable)
- Round-trip: 0.20% (normal) / **0.15%** (BNB)
- Günlük fee farkı: $9.18 ($100 sermayede kritik) → **BNB discount zorunlu**
- Testnet fee = 0 → **internal fee sim zorunlu** (paper'da gerçekliğe öğretici)

**Break-even WR tablosu:**

| Senaryo | Fee RT | TP gross | SL gross | BE WR |
|---|---|---|---|---|
| Eski VwapEma (ADR-0016) | 0.20% | 0.50% | 0.30% | 62.5% |
| Yeni Micro (TP 0.60 / SL 0.35, BNB) | **0.15%** | **0.60%** | **0.35%** | **52.6%** |

**Strateji seçimi (8 pattern arasından):**

| Pattern | WR tahmin | Fee uyum | Skor |
|---|---|---|---|
| Spread Capture | N/A | KÖTÜ (spread << fee) | 1/10 — red |
| OFI (Order Flow Imbalance) | 52-58% | ORTA | 7/10 — v2 backlog |
| Tick RSI Divergence | 52-56% | KÖTÜ | 3/10 — red |
| **Micro VWAP Reclaim** | **58-65%** | **İYİ** | **7/10 — SEÇİLEN** |
| Quick Pullback | 55-60% | ORTA | 5/10 — red (tek başına) |
| Volume Spike Breakout | 55-65% | İYİ | 6/10 — confirm filtresi |
| BB Micro-Squeeze | 55-65% | ORTA | 4/10 — red |
| Wall Detection | 50-60% | ORTA | 4/10 — red (spoof riski) |

**Kombinasyon:** VWAP Reclaim + Volume 1.5× + EMA(20) trend filter (3 katman).

**Timeframe:** 30s kline — 1m ile hedef 150/saat tutmuyor:
- 1m: 4 sembol × 60/saat × 30% filtre = 72-96 trade/saat ❌
- **30s: 4 × 120/saat × 30% = 144-160 trade/saat ✓**

**Rate limit analizi:** 300 order/saat, limit 50/10s → %1.67 kullanım. Güvenli.

### 18.4 Kullanıcı ile Hedef Reconciliation

Kullanıcı $1 istedi, Binance API $5 zorluyor. `reform-brief.md` §Sizing: *"1 dolar alt limit mantıklı değilse bunu değiştirebilirsin"* — binance-expert araştırma sonucu bildiriyor. Auto-mode kararı: **$5.10 floor kabul**; kartopu ADR-0011 semantiği $510+ sermayede %1 > $5.10 olunca devreye girer.

### 18.5 Mevcut Altyapı Envanteri

Kontrol edildi:

| Kaynak | Durum |
|---|---|
| `StrategyType` enum | Tek değer: `VwapEmaHybrid = 1` (`src/Domain/Strategies/StrategyEnums.cs`) |
| `BinanceOptions.KlineIntervals` | `["1m", "1h"]` (`appsettings.json:37`) |
| `BinanceWsSupervisor.BuildStreamUrl` | Multi-interval desteği VAR (`{symbol}@kline_{interval}` — `BinanceWsSupervisor.cs:159-174`) — 30s eklemek sadece config |
| `MarketIndicatorService` rolling buffer | 1m + 1h buffer (ADR-0015 §15.6) — 30s için yeni buffer slot gerekli |
| `VwapEmaStrategyEvaluator.Parameters` | `EmaPeriod=21`, `EmaTimeframe="1h"`, `VwapWindowBars=1440` — 30s'e uyum için yeni parametre seti |
| `SnowballSizing.FloorUsd` | `20.0m` const (ADR-0017 hotfix-2) — **5.10'a düşecek** |
| `IInstrumentFilterService` / `instrument.MinNotional` | VAR — exchange floor zaten caller tarafında uygulanıyor |
| `ContextJson.maxHoldMinutes` + handler okuma | VAR (ADR-0017 §17.7) — 30s strateji için de uyumlu |
| `Orders.Commission` kolonu | VAR — fee persistence için hazır |
| `PaperFill.UseBnbFeeDiscount` | `appsettings.json:52` — `false` default, **true** yapılacak |

Yeni aggregate **yok**. Yeni domain event **yok**. Yeni infrastructure servis: `IPaperFeeSimulator` (opsiyonel — static helper da yeter; kararı §18.13).

## Decision

### 18.6 Stratejik Reform — `MicroScalperVwapEma30s` Yeni Enum Değeri

**Karar:** `StrategyType` enum'una **yeni değer** eklenir:

```csharp
public enum StrategyType
{
    VwapEmaHybrid           = 1,  // deprecated; DB reseed ile kaldırılır ama enum korunur (ADR-0015 Loop 19 pattern)
    MicroScalperVwapEma30s  = 2,  // ADR-0018 — 30sn kline, 4-sembol, BNB-discount micro-scalp
}
```

Eski değer `1` **silinmez** — `StrategyType.VwapEmaHybrid` enum ordinalini korur (geriye dönük migration karmaşası önlemek için ADR-0015 Loop 19 pattern'i tekrar edilir). Yeni evaluator sınıfı: `MicroScalperVwapEma30sEvaluator` (aşağıda §18.11). Mevcut `VwapEmaStrategyEvaluator` **kaldırılmaz** (deprecated olarak bırakılır) — registry `StrategyEvaluatorRegistry`'te her iki tip de kayıtlı kalır; DB reseed ile eski tip seed'leri silinir.

**Alternatif reddedildi (18.6.A):** `VwapEmaHybrid`'in parametre seti genişletilsin (ör. `KlineInterval` parametresi eklenip 30s path'i içeride branch'lensin). Red — 1m ile 30s arasında indicator buffer, warmup bar count, SlopeTolerance semantiği, VWAP window tanımı hepsi farklı; tek sınıf iki davranışı taşımak SRP bozar. İki evaluator daha net.

### 18.7 Parametre Kontratı — `MicroScalperVwapEma30sEvaluator.Parameters`

```csharp
private sealed class Parameters
{
    // Timeframe
    public string KlineInterval     { get; set; } = "30s";   // Binance WS @kline_30s
    public string EmaTimeframe      { get; set; } = "30s";   // EMA(20) on 30s bars (eski 1h değil)
    public int    EmaPeriod         { get; set; } = 20;

    // VWAP
    public int    VwapWindowBars    { get; set; } = 15;      // rolling 7.5 dk (15 × 30s)
    public decimal VwapTolerancePct { get; set; } = 0m;      // strict reclaim, zone yok

    // Volume
    public int    VolumeSmaBars     { get; set; } = 20;
    public decimal VolumeMultiplier { get; set; } = 1.5m;    // spam azaltma (eski 0.5 → 1.5)

    // Direction gate
    public decimal SlopeTolerance   { get; set; } = 0m;      // EMA slope pozitif olmalı, tolerance yok

    // Exits
    public decimal TpGrossPct       { get; set; } = 0.006m;  // +0.60% gross (+0.45% net BNB)
    public decimal StopPct          { get; set; } = 0.0035m; // -0.35% gross
    public int    MaxHoldMinutes    { get; set; } = 2;       // 4 bar × 30sn
}
```

**Break-even hesap (BNB discount):**
- Fee RT = 0.15%
- Net TP = 0.60 − 0.15 = **+0.45%**
- Net SL = 0.35 + 0.15 = **−0.50%**
- BE WR = 0.50 / (0.50 + 0.45) = **52.63%**

### 18.8 Giriş Kuralları — Kapalı 30sn Bar Bazında

```
1. directionGate  : nowEma(20, 30s) > prevEma(20, 30s) × (1 + SlopeTolerance=0)
                    // pure positive slope
2. vwapContext    : prevBarClose < vwap                    (pullback)
3. vwapReclaim    : lastBarClose > vwap                    (strict, zone yok)
4. volumeConfirm  : lastBarVolume >= volumeSma20 × 1.5     (spike)
```

Dört koşul da true → `StrategySignalDirection.Long` emit. Short path yok (ADR-0006 spot long-only).

### 18.9 Çıkış Kuralları

| Exit | Değer | Dayanak |
|---|---|---|
| Take-Profit | `entryPrice × (1 + 0.006)` | research §5.2 TP 0.60% |
| Stop-Loss | `entryPrice × (1 − 0.0035)` | research §5.2 SL 0.35% |
| Time-Stop | `MaxHoldDuration = TimeSpan.FromMinutes(2)` | research §5.2 (4 bar × 30s) |

TimeStop pipeline ADR-0017 §17.7 ile zaten `ContextJson.maxHoldMinutes` okuyor — yeni evaluator aynı key'i yazar, handler dokunulmaz.

**TimeStop tick hızı kontrolü:** `StopLossMonitorService`/`TakeProfitMonitorService` + `TimeStopMonitorService` (ADR-0014 §14.5) **30sn tick**'te çalışıyor. 2 dakika TimeStop için granularite yeterli (en geç 2:30'da tetiklenir, kabul edilir). Daha sıkı çözünürlük isterse ayrı ADR.

### 18.10 Sizing Floor Düzeltme (ADR-0017 §17.9 Supersedes)

`SnowballSizing` sabitleri değişir:

```csharp
internal static class SnowballSizing
{
    public const decimal FloorUsd      = 5.10m;   // ADR-0018 §18.10 — binance minNotional $5 + buffer $0.10
    public const decimal EquityFraction = 0.01m;  // ADR-0018 — kullanıcı %1 (eski %20 değil)
    // CalcMinNotional(equity) = max(equity × 0.01, 5.10)
}
```

**Kartopu örnekleri:**

| Equity | `equity × 0.01` | Floor | Target | Yorum |
|---|---|---|---|---|
| $100 | 1.00 | 5.10 | **5.10** | Floor devrede (kullanıcı $1 isteği → Binance $5 bağlayıcı) |
| $510 | 5.10 | 5.10 | **5.10** | Geçiş noktası — kartopu başlıyor |
| $1000 | 10.00 | 5.10 | **10.00** | Oransal büyüme |
| $5000 | 50.00 | 5.10 | **50.00** | Kartopu etkisi |

`StrategySignalToOrderHandler` ADR-0017 §17.9 formülünü korur:

```csharp
var targetNotional = SnowballSizing.CalcMinNotional(equity);           // max(eq*0.01, 5.10)
var effectiveMinNotional = Math.Max(targetNotional, instrument.MinNotional);  // exchange filter respect
var hardCap = equity * risk.MaxPositionSizePct;                        // sanity guard ADR-0017
var chosenNotional = Math.Min(targetNotional, hardCap);
// chosenNotional / entry → qty → Ceiling(qty / stepSize) × stepSize
```

**Precision yukarı yuvarlama zorunlu** (research §1.8): `qty * price` her zaman `minNotional`'ın üstünde kalsın. Mevcut caller LOT_SIZE filter ile ceil yapıyor — aynı kalır.

### 18.11 Architectural Impact — Dosya Bazında

| Dosya | Değişiklik | Katman |
|---|---|---|
| `src/Domain/Strategies/StrategyEnums.cs` | `MicroScalperVwapEma30s = 2` eklenir; `VwapEmaHybrid` deprecated comment | Domain |
| `src/Infrastructure/Strategies/Evaluators/MicroScalperVwapEma30sEvaluator.cs` | **Yeni dosya** — `IStrategyEvaluator` impl, `Type => MicroScalperVwapEma30s` | Infrastructure |
| `src/Infrastructure/Strategies/StrategyEvaluatorRegistry.cs` | Yeni evaluator registration (tek satır DI) | Infrastructure |
| `src/Infrastructure/Strategies/Indicators/MarketIndicatorService.cs` | 30s rolling buffer slot + warmup; `MarketIndicatorSnapshot`'a `Ema20_30sNow/Prev`, `Vwap30s`, `VolumeSma20_30s` alanları (veya ayrı snapshot tipi) | Infrastructure |
| `src/Application/Strategies/Indicators/MarketIndicatorSnapshot.cs` | Snapshot kontratı genişler (VO immutable) | Application |
| `src/Infrastructure/Strategies/SnowballSizing.cs` | `FloorUsd = 5.10m`, `EquityFraction = 0.01m` | Infrastructure |
| `src/Api/appsettings.json` → `Binance.KlineIntervals` | `["30s"]` (1m/1h kaldırılır — EMA20 30s bar kendi içinde hesaplanır) | Config |
| `src/Api/appsettings.json` → `Binance.Symbols` | `["BTCUSDT", "ETHUSDT", "BNBUSDT", "XRPUSDT"]` (zaten var) | Config |
| `src/Api/appsettings.json` → `Strategies.Seed` | 4 yeni seed: `BTC-MicroScalper`, `ETH-MicroScalper`, `BNB-MicroScalper`, `XRP-MicroScalper`. Eski 3 VwapEma seed **silinir** | Config |
| `src/Api/appsettings.json` → `PaperFill.UseBnbFeeDiscount` | `true` (BNB discount paper sim'de varsayılan aktif) | Config |
| `src/Api/appsettings.json` → `RiskProfile.Defaults.MaxOpenPositions` | `4` (her sembol 1 paralel) | Config |
| `src/Infrastructure/Paper/PaperFillService.cs` (veya eşdeğer) | Testnet fill sonrası `notional × 0.00075` commission düşümü + `Orders.Commission` update | Infrastructure |
| `src/Infrastructure/Persistence/Migrations/Loop23MicroScalperReseed.cs` | **Yeni migration** — `DELETE FROM StrategySignals; UPDATE Positions SET StrategyId = NULL; DELETE FROM Strategies;` — seeder boot'ta 4 yeni seed basacak | Infrastructure |

**WS URL builder değişmiyor** — `BinanceWsSupervisor.BuildStreamUrl` zaten `KlineIntervals` config'inden okuyor. `["30s"]` verildiğinde `wss://stream.testnet.binance.vision/stream?streams=btcusdt@kline_30s/ethusdt@kline_30s/...` formatını üretir. Binance dokümantasyonu `@kline_30s`'i destekliyor (research §3.11 + §4.4).

### 18.12 Paper-Mode Fee Simülasyonu — Infrastructure Static Helper

**Kontrat yerleşimi:**

| Tip | Katman | Rol |
|---|---|---|
| `PaperFeeSimulator` static class | Infrastructure (`BinanceBot.Infrastructure.Paper`) | `decimal CalculateCommission(decimal notional, bool bnbDiscount)` |

**Neden static helper (Alternatif H-B red):** Domain invariant değil; sadece paper-mode persistence yan etkisi. DI'ya abstraction eklemek (`IPaperFeeCalculator`) YAGNI — 2 satır saf fonksiyon, test edilebilir, mock'lanması gerekmeyen deterministik hesap. Gelecekte VIP tier per-user farklılaşırsa `IPaperFeeCalculator` abstraction'ı eklenir.

**İmzalar:**

```csharp
namespace BinanceBot.Infrastructure.Paper;

internal static class PaperFeeSimulator
{
    public const decimal NormalFeeRate = 0.001m;    // %0.10 spot VIP 0
    public const decimal BnbDiscountFeeRate = 0.00075m; // %0.075 BNB discount

    /// <summary>
    /// ADR-0018 §18.12. Paper mode commission simulation — testnet returns fee=0 but
    /// mainnet charges 0.075% (BNB) / 0.10% (normal). Internal deduction keeps the
    /// VirtualBalance honest for EV back-testing.
    /// </summary>
    public static decimal CalculateCommission(decimal notional, bool bnbDiscount)
        => notional * (bnbDiscount ? BnbDiscountFeeRate : NormalFeeRate);
}
```

**Nerede çağrılır:** `PaperFillService` (veya mevcut paper fill pipeline — backend-dev konumlandıracak) `OrderFilledEvent` publish etmeden **önce** `Orders.Commission` kolonunu doldurur ve `VirtualBalance.Debit(commission)` çağırır (açıkta yeni balance aggregate yok — mevcut `VirtualBalance` domain aggregate'ı ADR-0011'den). Commission close order fill sonrası **tekrar** düşer (round-trip).

**Kritik invariant:** Fee deduction `VirtualBalance` aggregate'ının kendi invariantı ile yönetilir — `Debit(amount)` zaten amount > 0 kontrolü yapıyor. Yeni domain method eklenmez.

### 18.13 Per Sembol Seed Tasarımı

Her sembol **tek seed** taşır — ADR-0015 §15.1 pattern'ine uyumlu. Tüm seed'ler aynı `ParametersJson`'u taşır (per-symbol StopPct override gerekmez — research §5.2 tek SL 0.35% tüm 4 sembol için uygun; XRP volatilitesi 30sn bar'da BTC/ETH benzeri — 1m'deki fark farklı ölçek).

`appsettings.json` Strategies.Seed:

```json
{
  "Name": "BTC-MicroScalper",
  "Type": "MicroScalperVwapEma30s",
  "Symbols": ["BTCUSDT"],
  "ParametersJson": "{\"KlineInterval\":\"30s\",\"EmaTimeframe\":\"30s\",\"EmaPeriod\":20,\"VwapWindowBars\":15,\"VwapTolerancePct\":0.0,\"VolumeSmaBars\":20,\"VolumeMultiplier\":1.5,\"SlopeTolerance\":0.0,\"TpGrossPct\":0.006,\"StopPct\":0.0035,\"MaxHoldMinutes\":2}",
  "Activate": true
}
```

ETH/BNB/XRP aynı ParametersJson, Symbols farklı. Toplam 4 seed.

### 18.14 Migration — `Loop23MicroScalperReseed`

ADR-0015 §15.9 + ADR-0016 §16.7 pattern'i:

```sql
-- Up
DELETE FROM StrategySignals;
UPDATE Positions SET StrategyId = NULL WHERE StrategyId IS NOT NULL;
DELETE FROM Strategies;
-- Down: no-op (data migration irreversible; seeder re-inserts on next boot)
```

Loop 22c açık pozisyon var mı? Yok (halt t=85 + Loop 23 DB reset). `UPDATE Positions SET StrategyId = NULL` fiilen no-op, code path güvenliği için korunur.

Seeder boot'ta 4 yeni seed'i idempotent insert eder (mevcut `StrategySeeder` davranışı). Reset sonrası taze seed.

**Migration adı:** `Loop23MicroScalperReseed`.

```bash
dotnet ef migrations add Loop23MicroScalperReseed --project Infrastructure --startup-project Api
```

### 18.15 DDD Aggregate Sınırı — Değişiklik Yok

| Aggregate / Type | Katman | ADR-0018 Etki |
|---|---|---|
| `Strategy` aggregate | Domain | Değişmez — `ParametersJson` opak string |
| `Position` aggregate | Domain | Değişmez — `MaxHoldDuration` / `StopPrice` / `TakeProfit` mevcut |
| `Order` aggregate | Domain | Değişmez — `Commission` kolon mevcut |
| `VirtualBalance` aggregate | Domain | Değişmez — `Debit(amount)` invariantı paper fee için kullanılır |
| `RiskProfile` aggregate | Domain | Değişmez |
| `StrategyType` enum | Domain | Yeni değer `MicroScalperVwapEma30s = 2` |
| `MarketIndicatorSnapshot` VO | Application | **Genişler** — 30s buffer alanları eklenir (ayrı snapshot tipi veya tek snapshot nullable field) |
| `IMarketIndicatorService` | Application | Değişmez (internal buffer genişler, arayüz aynı) |
| `IStrategyEvaluator` | Application | Değişmez |
| `MicroScalperVwapEma30sEvaluator` | Infrastructure | **Yeni sınıf** |
| `VwapEmaStrategyEvaluator` | Infrastructure | Deprecated, silinmez |
| `SnowballSizing` | Infrastructure | Const değerleri değişir |
| `PaperFeeSimulator` | Infrastructure | **Yeni static helper** |
| `BinanceWsSupervisor` | Infrastructure | Değişmez (config-driven) |

Yeni aggregate **yok**. Yeni domain event **yok**. Dependency rule ihlali yok.

### 18.16 Logging + ContextJson

Evaluator emit log satırı:

```
MicroScalper emit symbol={Symbol} entry={Entry} stop={Stop} tp={Tp} 
              slope={Slope:F6} vwap={Vwap} volRatio={Ratio:F3}
              decision=Emit
```

Skip log:

```
MicroScalper skip symbol={Symbol} slope={Slope:F6} vwapCtx={Below} 
              reclaim={Reclaim} volRatio={Ratio:F3} decision=Skip
```

ContextJson payload:

```json
{
  "type": "micro-scalper-vwap-ema-30s",
  "vwap": 85123.45,
  "ema20_30sNow": 85100.0,
  "ema20_30sPrev": 85080.0,
  "slope": 0.000235,
  "prevBarClose": 85100.0,
  "lastBarClose": 85150.0,
  "volumeRatio": 1.82,
  "stopPct": 0.0035,
  "tpGrossPct": 0.006,
  "maxHoldMinutes": 2
}
```

`maxHoldMinutes` key ADR-0017 §17.7 handler'ı ile tam uyumlu — TimeStop tetiklenir.

## Consequences

### Pozitif

- **Hedef frekans erişilir.** 30s kline × 4 sembol × ~30% filtre geçiş = 144-160 trade/saat (research §5.1) — kullanıcının "saatte ≥150" hedefi karşılanır.
- **Break-even WR düşer.** 62.5% (ADR-0016) → **52.63%** (BNB discount + asimetrik 1.71:1 R:R). Güvenlik marjı +10 puan.
- **Paper gerçekçiliği artar.** Testnet fee=0 aldatmacası internal sim ile kapatılır; EV hesabı mainnet'le tutarlı.
- **Sizing binance kuralına uyumlu.** $5.10 floor API-1013 error'dan koruma.
- **Kartopu kuralı korunur.** $510+ sermayede %1 oransal büyüme devreye girer.
- **Aggregate sınırı dokunulmaz.** Yeni domain event/aggregate yok, migration riski minimum.
- **4 sembol paralel.** BTC/ETH/BNB/XRP tek strateji tipi altında tutarlı.

### Negatif / Tradeoff

- **WR < 52.63% → sistematik zarar.** 150 trade/saat ile kötü WR hızla amplifiye eder. Loop 24 ilk 500 trade kritik izleme şart (research §5.6 UYARI 1).
- **BNB bakiyesi tükenirse BE WR 57%'e fırlar.** `UseBnbFeeDiscount=true` varsayımı BNB yan bakiyesi korumayı şart koşar. Alarm kuralı: BNB < $2 → log warning + fee mode switch (operasyonel plan, kod Loop 24 backlog).
- **Extreme volatilitede slippage TP'yi eritir.** Mainnet 0.5%+ slip → 0.60% gross TP etkisiz. Testnet'te gözükmez. ATR guard ADR-0018'de yok (v2 backlog).
- **30s bar gürültü riski.** 15-bar VWAP window = 7.5 dk — kısa pencere false reclaim üretebilir. Volume 1.5× filtresi bunu kısmen yakalar.
- **Dosya sayısı:** +1 evaluator, +1 migration, +1 fee helper, appsettings 4 seed. Mevcut VwapEmaStrategyEvaluator.cs deprecated kalıyor — cleanup Loop 24+ backlog.
- **`MarketIndicatorSnapshot` genişlemesi.** 30s için yeni alanlar snapshot VO'yu şişirir; tek snapshot'ta `Ema1h21*` (eski, artık null) ve `Ema20_30s*` (yeni) koexist eder. Alternatif: iki ayrı snapshot tipi (yeni tip: `MicroScalperIndicatorSnapshot`). Backend-dev kararı vermek üzere — tercih: **ayrı tip**, SRP + null-safety.
- **Eski 3 VwapEma seed + evaluator deprecated.** Silinmiyor (enum korunsun). DB reseed temiz ama kod yüzeyi 2 evaluator taşıyor → review yükü.
- **`KlineIntervals: ["30s"]`** — 1m/1h backfill kaldırılıyor. Eski backfill bar'ları hiç kullanılmayacak (yeni evaluator sadece 30s okur). Mevcut `Klines` tablosu eski bar'ları sakla; migration tarafı sadece Strategies wipe — Klines dokunulmaz.

### Nötr

- MediatR command/query yüzeyi değişmez.
- `IMarketIndicatorService` arayüzü değişmez (sadece buffer map'i genişler).
- `StrategySignalToOrderHandler` + `OrderFilledPositionHandler` (ADR-0017) dokunulmaz.
- `BinanceWsSupervisor` kod değişmez; sadece config güncellenir.
- ADR-0005 (risk), ADR-0006 (testnet), ADR-0009 (backfill), ADR-0010 (suppression), ADR-0011 (sizing formül), ADR-0012 (monitor), ADR-0017 (TimeStop/duplicate/sizing semantics) uyumlu.

## Alternatives (Reddedilen)

### Alt-A — 1m Kline İle Devam (Parametre Sıkılaştırma)

ADR-0016 VwapEma'yı 1m koru, sadece TP/SL/filter sıkılaştır. 1m × 4 sembol × 30% = 72-96 trade/saat — kullanıcı hedefinin altında. **Red** (research §5.1).

### Alt-B — Taker-Only Fee Modeli (BNB Discount Kullanmamak)

Round-trip 0.20%, BE WR 83% (senaryo 1, research §2.5). Pratik imkansız. **Red.**

### Alt-C — $1 Sizing Floor Zorlama

Binance API -1013 MIN_NOTIONAL hatası ile her order reject. **Red** (research §1.7).

### Alt-D — Yeni Enum Değeri Yerine Parametre Genişletmesi

`VwapEmaHybrid`'e `KlineInterval` parametresi eklenip tek evaluator'da 1m ve 30s branch'lensin. SRP bozar — indicator buffer, warmup, slope semantiği hepsi farklı. **Red** (§18.6.A).

### Alt-E — OFI (Order Flow Imbalance) Stratejisi

aggTrade stream + Hawkes process. WR 52-58% (bireysel bot), fee uyum ORTA. VWAP reclaim WR 58-65% + fee uyum İYİ. **Red — OFI v2 backlog** (research §3.3).

### Alt-F — Spread Capture

BTC spread 0.01 USD / 85000 = 0.0000118%. Fee 0.20% RT >> spread. Matematik tutmaz. **Red** (research §3.2).

### Alt-G — Paper Fee Sim Domain Servisi

Domain'de `IFeeCalculator` + Infrastructure impl. Over-engineering — 2 satırlık saf fonksiyon, domain invariant değil. Static helper yeter. **Red** (§18.12).

### Alt-H — MaxHoldMinutes Yerine MaxHoldBars (Bar Sayısı)

30s bar × 4 = 2dk. `maxHoldBars` key ADR-0017 handler'da VAR (§17.7 fallback), ama convention `maxHoldMinutes` primary. Primary key kullanılır — interop tutarlı. **Red kısmi.**

### Alt-I — EMA Timeframe 30m veya 1h

research §3.11 Katman 1: "EMA(20) 30m barlar". 30m EMA slope ADR-0016'daki 1h-EMA21 ile paralel filtre. **Red — 30s kendi içinde EMA yeterli** (direction gate + volume spike + VWAP zone üç bağımsız katman; 30m/1h EMA gereksiz karmaşa; 30s path tek-timeframe daha sade). Loop 24 gözlem sonrası gerekirse ayrı ADR ile eklenir (v2 backlog).

### Alt-J — MarketIndicatorSnapshot Tek Tip Genişlet

Mevcut snapshot tipine 30s alanları nullable ekle. Kısa vadede hızlı ama tip polimorfizmi bulanır — `Ema1h21Now == null && Ema20_30sNow != null` koşulu caller'ı mecbur bırakır. **Yarı-red — backend-dev ayrı tip tercih etsin** (§18.11 tablosu).

## Source

- [`loops/loop_23/research-micro-scalping.md`](../../loops/loop_23/research-micro-scalping.md) — binance-expert 499-satır canlı API AR-GE
- [`loops/loop_23/reform-brief.md`](../../loops/loop_23/reform-brief.md) — PM özet, kullanıcı hedefi
- Canlı API (2026-04-19): https://api.binance.com/api/v3/exchangeInfo
- Canlı API testnet: https://testnet.binance.vision/api/v3/exchangeInfo
- [ADR-0011 Equity-Aware Sizing](./0011-equity-aware-sizing-and-risk-tracking.md) — min-notional çerçevesi
- [ADR-0014 Pattern-Based Scalping Reform](./0014-pattern-based-scalping-reform.md) — TimeStop §14.5
- [ADR-0015 VWAP-EMA Hybrid Strategy](./0015-vwap-ema-hybrid-strategy.md) — seed-per-symbol pattern §15.1, DB reset §15.9
- [ADR-0016 VWAP-EMA Hybrid V2 Tuned](./0016-vwap-ema-hybrid-v2-tuned.md) — SystemEvents publisher, supersedes strategy layer
- [ADR-0017 TimeStop Mapping + Duplicate Protection + Sizing](./0017-timestop-mapping-duplicate-protection-sizing.md) — ContextJson key, duplicate guard, target-notional formula
- [`src/Domain/Strategies/StrategyEnums.cs`](../../src/Domain/Strategies/StrategyEnums.cs) — enum mevcut
- [`src/Infrastructure/Strategies/SnowballSizing.cs`](../../src/Infrastructure/Strategies/SnowballSizing.cs) — FloorUsd değişecek
- [`src/Infrastructure/Binance/Streams/BinanceWsSupervisor.cs`](../../src/Infrastructure/Binance/Streams/BinanceWsSupervisor.cs) — BuildStreamUrl §159-174 config-driven, değişmez
- [`src/Infrastructure/Strategies/Indicators/MarketIndicatorService.cs`](../../src/Infrastructure/Strategies/Indicators/MarketIndicatorService.cs) — buffer genişler
- [`src/Infrastructure/Strategies/Evaluators/VwapEmaStrategyEvaluator.cs`](../../src/Infrastructure/Strategies/Evaluators/VwapEmaStrategyEvaluator.cs) — deprecated
- [`src/Api/appsettings.json`](../../src/Api/appsettings.json) — config değişiklikleri
- [Binance Spot Filters](https://developers.binance.com/docs/binance-spot-api-docs/filters)
- [Binance Rate Limits](https://developers.binance.com/docs/binance-spot-api-docs/websocket-api/rate-limits)
- [Binance Testnet Fee Sim](https://dev.binance.vision/t/testnet-fee-simulation/16810)
- [Binance Fee Schedule](https://www.binance.com/en/fee)
- [Dean Markwick — Order Flow Imbalance](https://dm13450.github.io/2022/02/02/Order-Flow-Imbalance.html)
- [arxiv 2502.13722 — VWAP Execution Deep Learning (2025)](https://arxiv.org/html/2502.13722v1)
- [Microsoft Learn — DDD + CQRS](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/)
- [jasontaylordev/CleanArchitecture](https://github.com/jasontaylordev/CleanArchitecture)
- [ardalis/CleanArchitecture](https://github.com/ardalis/CleanArchitecture)
- [joelparkerhenderson/architecture-decision-record (MADR)](https://github.com/joelparkerhenderson/architecture-decision-record)
