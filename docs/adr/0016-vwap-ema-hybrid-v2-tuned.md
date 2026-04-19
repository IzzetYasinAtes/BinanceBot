# 0016. VWAP-EMA Hybrid V2 — High-Frequency Scalping Tuning + SystemEvents Publisher

Date: 2026-04-19
Status: Accepted
Supersedes: [ADR-0015 VWAP-EMA Hybrid Strategy](./0015-vwap-ema-hybrid-strategy.md)

> Bu ADR iki eksende karar verir: (A) `VwapEmaHybrid` parametre tuning'i ile sık işlem + sıkı SL hedefine uyum, (B) boş kalan `SystemEvents` tablosunu dolduran `ISystemEventPublisher` servisinin Clean Architecture katmanlarına yerleştirilmesi. Operasyonel detay (commit sırası, test matrisi) backend-dev tarafından `loops/loop_21/decision-*.md` içinde üretilecek.

## Context

### 1. Loop 20 Deneysel Bulgu — Matematiksel Uyumsuzluk

`loops/loop_20/summary.md` + `loops/loop_20/diagnosis-no-trade.md`:

- 150 dk canlı `Paper` run, **0 sinyal / 0 order / 0 trade**.
- 125 `KlineClosedEvent` işlendi, 125 `VwapEmaStrategyEvaluator.EvaluateAsync` çağrıldı — sistem bozuk değil, **strateji tasarımı gereği** sessiz.
- Root cause: `directionGate` (`nowEma > prevEma` on EMA21 1h) 21 saatlik pencerede BTC −%0.69, BNB −%1.66, XRP −%0.43 → gate her iterasyonda `false`.
- XRP'de **11 kez** `directionGate=true + vwapContext=true + volume=3x` ama `reclaim=false` (last barClose VWAP altı kaldı, kıl payı kaçtı) — ADR-0015'in `vwapReclaim` keskin eşiği gevşetilebilir.

### 2. Piyasa-Hedef Matematik Çakışması

`loops/loop_20/summary.md` canlı ölçüm:

| Sembol | 1m ort | 1m max | 15m ort | 15m max |
|--------|--------|--------|---------|---------|
| BTC    | %0.032 | %0.25  | %0.21   | %0.90   |
| BNB    | %0.011 | %0.14  | %0.14   | %0.42   |
| XRP    | %0.029 | %0.48  | %0.30   | %1.14   |

ADR-0015 TP hedefi (`swingHigh × 0.95`) tipik olarak %1-2 gross gerektiriyor. 15m ortalama hareket %0.14-0.30 → hedef ortalamanın **5-10 katı**. Hedef piyasanın boyu ile uyumsuz.

### 3. Kullanıcı Kararı (Loop 20 halt sonrası)

> "%1-2 → %0.3-0.5 net/trade. Fee düşülüp net %0.1-0.3. Saatte 4-6 trade... sık işlem yapıp stoploss koy kardan faydalan"

Yeni operasyonel hedef:
- Net **%0.3-0.5** / trade (gross %0.5-0.7, fee %0.2 round-trip dahil)
- **4-8 sinyal/saat** toplam (3 sembol × 1-2 saat/sembol konsolidasyon dönemlerinde)
- **Sıkı SL** — whipsaw kabul; erken zarar kesme, sık işlem devir hızı
- Time-stop devam — fiyat yataylaşırsa pozisyon erken çıksın

### 4. binance-expert Araştırması (`loops/loop_21/research-b-high-frequency.md`)

Ayrıntılı BKZ. rapor. Nihai öneri: **C + E Kombinasyonu** (VWAP zone toleransı + slope threshold gevşemesi). Ret edilen alternatifler:

| Alternatif | Özet | Red |
|---|---|---|
| A — Weighted 3/4 scoring | `directionGate=false` iken skor 0.70 → emit | Bear-gün güvencesi bozulur |
| B — EMA12(15m) gate | Daha kısa pencere | Loop 16-19 %100 time-stop, 15m gate faydasız kanıtlı |
| C — VWAP zone (±%0.15) | `last > VWAP` OR `abs(last-VWAP)/VWAP ≤ 0.0015` | **Kabul** — Loop 20'nin 11 missed signal'ı yakalanırdı |
| D — Volume 1.0 | `SMA20 × 1.0` | Filtre etkinliği kaybolur, fee drag artar |
| E — Slope tolerance (≥−%0.05) | `nowEma ≥ prevEma × 0.9995` | **Kabul** — net bear pasif kalır, yatay konsolidasyonda gate açılır |

### 5. Backlog — `SystemEvents` Tablosu Boş

`loops/loop_20/summary.md` son maddeler + UI `logs.html` Sistem Olayları sekmesi `/api/logs/tail` 200 dönüyor, data yok. `SystemEvents` tablosuna hiç insert yapılmamış; event publishing altyapısı yok. Loop 21 reform ADR'sine eklenmesi karar verildi (tek ADR, tek migration pencere).

## Decision

### 16.1 VwapEmaHybrid V2 — Parametre Tuning (Strateji Sınıfı Yeniden Yazılmaz)

`StrategyType.VwapEmaHybrid = 1` **enum değeri aynen korunur** (breaking enum değişikliği yok). `VwapEmaStrategyEvaluator` sınıfı yaşamaya devam eder; yalnızca `Parameters` record'una dört yeni alan, dört koşul formülüne aritmetik güncelleme, `appsettings.json` üç seed `ParametersJson`'ına yeni alan setleri eklenir. Indicator service, aggregate, DDD sınırları, event/command/query yüzeyi **dokunulmaz**.

### 16.2 Giriş Kuralları — Kapalı 1m Bar Bazında (Tuned)

```
1. directionGate  : nowEma >= prevEma * (1 + SlopeTolerance)
                    // SlopeTolerance = -0.0005  (slope >= -%0.05)
2. vwapContext    : prevBarClose < vwap                               (değişmez)
3. vwapReclaim    : lastBarClose > vwap
                    OR Math.Abs(lastBarClose - vwap) / vwap <= VwapTolerancePct
                    // VwapTolerancePct = 0.0015  (±%0.15 zone)
4. volumeConfirm  : lastBarVolume >= volumeSma20 * VolumeMultiplier
                    // VolumeMultiplier = 1.05   (eski 1.2'den düşürüldü)
```

Dört koşul da true → `StrategySignalDirection.Long` emit. Short tarafı üretilmez (spot long-only — ADR-0006 + kullanıcı kuralı korunur).

### 16.3 Çıkış Kuralları — Sabit TP + Sembol-Bazlı SL

| Exit | ADR-0015 | ADR-0016 |
|---|---|---|
| Take-Profit | `swingHigh(last 20 bars) × 0.95` (≈%1-2 gross, piyasaya uyumsuz) | **`entryPrice × (1 + TpGrossPct)`, TpGrossPct = 0.007** (%0.7 gross → %0.5 net) |
| Stop-Loss (BTC/BNB) | `entryPrice × (1 - 0.008)` (%0.8) | **`entryPrice × (1 - 0.003)` (%0.3)** |
| Stop-Loss (XRP) | `entryPrice × (1 - 0.008)` (%0.8) | **`entryPrice × (1 - 0.004)` (%0.4)** — per-symbol override |
| Time-Stop | `MaxHoldDuration = 15 min` | **`MaxHoldDuration = 12 min`** (devir hızı) |

Sabit TP (yüzde-bazlı) tercih edildi çünkü `swingHigh × 0.95` her bar'da dalgalanan kararsız hedef; sabit yüzde öngörülebilir ve canlı EV hesabı doğrulanabilir (research §3.4 son madde).

Monitor servisleri (`StopLossMonitorService`, `TakeProfitMonitorService`, Loop 16 time-stop kolu) **olduğu gibi korunur**. Evaluator `StrategyEvaluation.SuggestedStopPrice` / `SuggestedTakeProfit` / `ContextJson.maxHoldMinutes` doldurur.

### 16.4 Per-Symbol StopPct — ParametersJson Çözümü

Semboller tek strateji seed'i paylaşmaz (ADR-0015 §15.1 üç seed per sembol: `BTC-VwapEma-Scalper`, `BNB-VwapEma-Scalper`, `XRP-VwapEma-Scalper`). **Dolayısıyla her seed'in `ParametersJson`'u kendi `StopPct` değerini taşır** — `StopPctPerSymbol` sözlüğüne gerek yok, daha basit. Aggregate sınırı temiz kalır.

Yine de başka bir strateji gelecekte tek seed altında çoklu sembol servis ederse diye, fallback kuralı: `StopPct` alanı zorunlu; `StopPctPerSymbol` (opsiyonel `Dictionary<string, decimal>`) varsa sembol eşleşmesinde önceliklenir, yoksa `StopPct` kullanılır. Loop 21 üç seed için `StopPctPerSymbol` **alanı yok**, her seed kendi `StopPct`'sini taşır.

### 16.5 Parameters Contract — Yeni Alanlar

`VwapEmaStrategyEvaluator.Parameters` record/class:

```csharp
private sealed class Parameters
{
    // ADR-0015 alanları (yeni default'larla)
    public decimal StopPct            { get; set; } = 0.003m;  // eski 0.008
    public decimal VolumeMultiplier   { get; set; } = 1.05m;   // eski 1.2
    public int     SwingLookback      { get; set; } = 20;      // swingHigh tracking backlog
    public decimal TpSafetyFactor     { get; set; } = 0.95m;   // deprecated — TpGrossPct geldi, ileride silinecek
    public int     MaxHoldMinutes     { get; set; } = 12;      // eski 15
    public int     VwapRollingBars1m  { get; set; } = 1440;
    public int     EmaPeriod1h        { get; set; } = 21;

    // ADR-0016 yeni alanlar
    public decimal VwapTolerancePct   { get; set; } = 0.0015m; // ±%0.15 zone
    public decimal SlopeTolerance     { get; set; } = -0.0005m; // slope >= -%0.05
    public decimal TpGrossPct         { get; set; } = 0.007m;   // entry × 1.007

    // Opsiyonel per-symbol override (Loop 21'de kullanılmıyor; ileride tek-seed multi-symbol için)
    public Dictionary<string, decimal>? StopPctPerSymbol { get; set; } = null;
}
```

Evaluator sabit TP yolunu tercih eder; `TpSafetyFactor` + `swingHigh × 0.95` yolu silinmez ama artık bypass edilir (geri dönüş switch'i için bırakıldı). Loop 22+ tek yol kalınca temizlik.

### 16.6 appsettings.json Seed Güncellemesi

Üç seed `ParametersJson` alanı (BNB + BTC için aynı, XRP farklı `StopPct`):

```json
{
  "Strategies": {
    "Seed": [
      {
        "Name": "BTC-VwapEma-Scalper",
        "Type": "VwapEmaHybrid",
        "Symbols": ["BTCUSDT"],
        "IsEnabled": true,
        "ParametersJson": "{\"StopPct\":0.003,\"VolumeMultiplier\":1.05,\"SwingLookback\":20,\"TpSafetyFactor\":0.95,\"MaxHoldMinutes\":12,\"VwapRollingBars1m\":1440,\"EmaPeriod1h\":21,\"VwapTolerancePct\":0.0015,\"SlopeTolerance\":-0.0005,\"TpGrossPct\":0.007}"
      },
      {
        "Name": "BNB-VwapEma-Scalper",
        "Type": "VwapEmaHybrid",
        "Symbols": ["BNBUSDT"],
        "IsEnabled": true,
        "ParametersJson": "{\"StopPct\":0.003,\"VolumeMultiplier\":1.05,\"SwingLookback\":20,\"TpSafetyFactor\":0.95,\"MaxHoldMinutes\":12,\"VwapRollingBars1m\":1440,\"EmaPeriod1h\":21,\"VwapTolerancePct\":0.0015,\"SlopeTolerance\":-0.0005,\"TpGrossPct\":0.007}"
      },
      {
        "Name": "XRP-VwapEma-Scalper",
        "Type": "VwapEmaHybrid",
        "Symbols": ["XRPUSDT"],
        "IsEnabled": true,
        "ParametersJson": "{\"StopPct\":0.004,\"VolumeMultiplier\":1.05,\"SwingLookback\":20,\"TpSafetyFactor\":0.95,\"MaxHoldMinutes\":12,\"VwapRollingBars1m\":1440,\"EmaPeriod1h\":21,\"VwapTolerancePct\":0.0015,\"SlopeTolerance\":-0.0005,\"TpGrossPct\":0.007}"
      }
    ]
  }
}
```

### 16.7 Re-Seed Stratejisi — Migration Yok, Manuel Reseed

**Şema değişikliği yok** (`ParametersJson` opak `nvarchar(max)`). EF Core migration **gerekmez**. Ancak DB'de zaten ADR-0015 `ParametersJson` değerleri var; `StrategyConfigSeeder` mevcut davranışı `Upsert` değil **idempotent insert** ise eski değerler kalır.

İki kabul edilebilir yol:

1. **Seeder'ı Upsert'e çevir (TERCİH EDİLEN)** — `StrategyConfigSeeder` her boot'ta `Name` match ile `ParametersJson`, `Symbols`, `IsEnabled` alanlarını override eder. Yeni seed değerleri otomatik uygulanır. Kod değişikliği küçük, migration yok, runtime güvenli.

2. **Data-migration `Loop21VwapEmaHybridReseed`** — Seeder'ı dokunmadan tek seferlik:
   ```
   DELETE FROM StrategySignals;
   UPDATE Positions SET StrategyId = NULL;
   DELETE FROM Strategies;
   ```
   Boot'ta seeder 3 seed'i taze insert eder. ADR-0015 §15.9'un aynı pattern'i. Loop 21 ilk başlatmada open pozisyon yok (Loop 20 0 trade); `Positions` NULL update no-op.

**Bu ADR yol (2)'yi seçer** — migration + DB snapshot izlenebilirliği mimari disipline uyar; seeder davranışı ileriki loop'larda tartışılır (backlog). Migration adı: **`Loop21VwapEmaHybridReseed`**.

`dotnet ef migrations add Loop21VwapEmaHybridReseed --project Infrastructure --startup-project Api` backend-dev tarafından.

### 16.8 Beklenen Performans (binance-expert EV Hesabı)

| Metrik | Değer |
|---|---|
| Trade frekansı (3 sembol) | 4-8 sinyal/saat, konsolidasyon+bull piyasa |
| Net TP | %0.5 (gross %0.7 - fee %0.2 round-trip) |
| Net SL BTC/BNB | %0.1 (gross %0.3 + fee %0.2) |
| Net SL XRP | %0.2 (gross %0.4 + fee %0.2) |
| Break-even WR | %36.4 (TP %0.7 / SL %0.4 net formül) |
| EV @ WR=%65 | +%0.345 / trade |
| EV @ WR=%58 (muhafazakar) | +%0.18 / trade |
| Günlük EV muhafazakar | +%3-4 / gün (22 trade × %0.18) |

Break-even WR %36.4 — EMA+VWAP literatürü (Tadonomics, PickMyTrade) %55-68 bildiriyor. Güvenlik marjı geniş. Gerçek WR Loop 21 paper run sonu ölçülecek.

### 16.9 SystemEvents Publisher — Clean Architecture Şablonu

`SystemEvents` tablosu var ama boş. Şimdiye kadar domain olayları sadece Serilog'a gitti; DB persistence yok. UI `logs.html` Sistem Olayları sekmesini doldurmak için **paralel pipe** kurulur: Serilog dokunulmaz, MediatR `INotificationHandler` pattern'i ile domain event'ler `ISystemEventPublisher`'e de forward edilir.

#### 16.9.1 Contract Yerleşimi

| Tip | Katman | Rol |
|---|---|---|
| `SystemEventType` enum | Domain (`BinanceBot.Domain.Events`) | UL terim, app genelinde okunan sabit |
| `ISystemEventPublisher` | **Application** (`BinanceBot.Application.SystemEvents`) | Abstraction; handler'lar inject eder |
| `SystemEvent` persistence entity | Infrastructure.Persistence (`BinanceBot.Infrastructure.Persistence.Entities`) | EF Core entity, `SystemEvents` tablo map'i — zaten var |
| `SystemEventPublisher` impl | Infrastructure (`BinanceBot.Infrastructure.SystemEvents`) | `AppDbContext` bağımlılığı, transactional insert |
| Notification handler'lar | Infrastructure (`BinanceBot.Infrastructure.SystemEvents.Handlers`) | Her domain event için tek handler, `ISystemEventPublisher` ile row yazar |

**Neden Domain'e yalnızca enum:** `SystemEventType` UL terim; handler'lar ve infrastructure ikisi birden buna bağlı, fakat Domain'e kod bağımlılığı yok (Domain aggregate'leri bu enum'ı import etmez). Saf veri. Dependency rule ihlali yok: Application → Domain (enum read), Infrastructure → Application (interface impl) + Domain (enum read).

#### 16.9.2 SystemEventType Enum

```csharp
namespace BinanceBot.Domain.Events;

public enum SystemEventType
{
    // 1x lifecycle
    Startup              = 1,
    Shutdown             = 2,
    // 1x strategy
    StrategyActivated    = 10,
    StrategyDeactivated  = 11,
    // 2x ws
    WsStateChanged       = 20,    // connected / disconnected / reconnecting
    // 3x warmup
    WarmupCompleted      = 30,    // indicator service warmup done per symbol
    // 4x signal
    SignalEmitted        = 40,
    SignalSkipped        = 41,    // throttle / cooldown / gate-false
    // 5x order
    OrderPlaced          = 50,
    OrderFilled          = 51,
    OrderCanceled        = 52,
    // 6x position
    PositionOpened       = 60,
    PositionClosed       = 61,
    // 7x risk
    RiskAlert            = 70     // circuit-breaker trip, dd-limit hit
}
// Values are category-banded (1x/2x/3x/4x/5x/6x/7x) so new types append
// within a band without renumbering. UI filters by int category.
```

#### 16.9.3 ISystemEventPublisher Kontratı

```csharp
namespace BinanceBot.Application.SystemEvents;

public interface ISystemEventPublisher
{
    /// <summary>
    /// Domain olayının DB'ye paralel persistence'ı. Serilog ile eşzamanlı çağrılır.
    /// Idempotent değil — caller tekrar çağırırsa aynı row birden fazla insert olur.
    /// </summary>
    Task PublishAsync(
        SystemEventType type,
        string message,                       // TR, UI'da gözükecek
        string? detailsJson = null,           // opak context (symbol, price, ratio vs.)
        CancellationToken ct = default);
}
```

#### 16.9.4 Infrastructure Impl (Kontrat İmzası)

```csharp
namespace BinanceBot.Infrastructure.SystemEvents;

public sealed class SystemEventPublisher : ISystemEventPublisher
{
    private readonly AppDbContext _db;
    private readonly IClock _clock;              // ADR-X (ortak clock abstraction)
    private readonly ILogger<SystemEventPublisher> _logger;

    public SystemEventPublisher(AppDbContext db, IClock clock,
        ILogger<SystemEventPublisher> logger) { ... }

    public async Task PublishAsync(
        SystemEventType type, string message,
        string? detailsJson = null, CancellationToken ct = default)
    {
        // SystemEvent entity oluştur, _db.SystemEvents.Add, SaveChangesAsync
        // Exception → ILogger.LogWarning, fakat throw etme (telemetry yolu caller'ı etkilemesin)
    }
}
```

**Neden swallow-exception:** Telemetry bir tali yoldur; DB insert başarısız olursa trade pipeline durmaz. Serilog zaten birincil kayıt. `LogWarning` ile görünürlük korunur.

#### 16.9.5 Notification Handler Örneği

MediatR mevcut `StrategySignalEmittedEvent` için (ADR-0015'te yaşıyor):

```csharp
namespace BinanceBot.Infrastructure.SystemEvents.Handlers;

public sealed class StrategySignalEmittedSystemEventHandler
    : INotificationHandler<StrategySignalEmittedEvent>
{
    private readonly ISystemEventPublisher _publisher;

    public StrategySignalEmittedSystemEventHandler(ISystemEventPublisher p) => _publisher = p;

    public Task Handle(StrategySignalEmittedEvent n, CancellationToken ct)
        => _publisher.PublishAsync(
            SystemEventType.SignalEmitted,
            $"Signal emit: {n.Symbol} {n.Direction} @ {n.Price:F2}",
            JsonSerializer.Serialize(new { n.StrategyId, n.Symbol, n.Price, n.ContextJson }),
            ct);
}
```

Her `SystemEventType` için ayrı handler (toplam ~13 küçük class). SRP uygundur; each handler tek olayı tek cümleye çevirir.

#### 16.9.6 Mevcut Domain/Application Event'leri Mapping

| Domain Event | SystemEventType |
|---|---|
| `AppStartedEvent` (yeni — boot'ta publish edilir) | Startup |
| `AppStoppingEvent` (yeni) | Shutdown |
| `StrategyActivatedEvent` (varsa; yoksa boot seed'inde publish) | StrategyActivated |
| `WsConnectionStateChangedEvent` (binance ws layer) | WsStateChanged |
| `IndicatorWarmupCompletedEvent` (MarketIndicatorService'te yeni emit) | WarmupCompleted |
| `StrategySignalEmittedEvent` | SignalEmitted |
| `StrategySignalSkippedEvent` (yeni — throttle/cooldown/gate-false) | SignalSkipped |
| `OrderPlacedEvent`, `OrderFilledEvent`, `OrderCanceledEvent` | OrderPlaced / OrderFilled / OrderCanceled |
| `PositionOpenedEvent`, `PositionClosedEvent` | PositionOpened / PositionClosed |
| `CircuitBreakerTrippedEvent`, `RiskLimitHitEvent` | RiskAlert |

**Yeni eklenecek event'ler:** `AppStartedEvent`, `AppStoppingEvent`, `IndicatorWarmupCompletedEvent`, `StrategySignalSkippedEvent`. Diğerleri mevcut (ADR-0005/0011/0012/0015). Backend-dev yeni event class'larını Application/Domain'e uygun konuma yerleştirir — pure record, payload alanları minimal.

#### 16.9.7 Registration

`src/Infrastructure/DependencyInjection.cs`:

```
services.AddScoped<ISystemEventPublisher, SystemEventPublisher>();
// MediatR INotificationHandler tarama zaten assembly scan ile aktif; ek explicit
// kayıt gerekmiyor.
```

### 16.10 DDD Aggregate Sınırı — ADR-0015'ten Değişiklik Yok

| Aggregate / Type | Katman | ADR-0016 Etki |
|---|---|---|
| `Strategy` aggregate | Domain | Değişiklik yok — `ParametersJson` yeni alan taşıyor ama domain opak string olarak okur |
| `Position` aggregate | Domain | Değişiklik yok — `StopPrice`/`TakeProfit`/`MaxHoldDuration` mevcut |
| `Order` / `RiskProfile` | Domain | Değişiklik yok |
| `MarketIndicatorSnapshot` VO | Application | Değişiklik yok |
| `IMarketIndicatorService` | Application | Değişiklik yok |
| `VwapEmaStrategyEvaluator` | Infrastructure | `Parameters` record 4 yeni alan + `EvaluateAsync` formül güncellemesi |
| `StrategySignalToOrderHandler` | Infrastructure | Değişiklik yok — sizing, fan-out, monitor yüzeyi aynı |
| `ISystemEventPublisher` (yeni) | Application | `PublishAsync(type, message, detailsJson, ct)` kontratı |
| `SystemEventPublisher` (yeni) | Infrastructure | `AppDbContext` + `IClock` ile DB insert |
| `SystemEvent` entity | Infrastructure.Persistence | Zaten var, dokunulmaz |
| `SystemEventType` enum (yeni) | Domain.Events | Saf enum, domain behavior yok |

Yeni aggregate **yok**. `SystemEvent` tablosu persistence tarafında rapor/log sink; aggregate root değil, invariant taşımıyor. Anemic model üretmemek için aggregate yapılmadı — DDD kuralı (`src/CLAUDE.md`).

### 16.11 Logging Kontratı — Yeni Log Satırı

`VwapEmaStrategyEvaluator.EvaluateAsync` `LogDebug` satırı genişler:

```
VwapEma V2: symbol={Symbol} slope={Slope:F6} slopeTol={SlopeTol}
           vwapCtx={Below} reclaim={Reclaim} vwapZoneOk={ZoneOk}
           volRatio={Ratio:F3} decision={Emit|Skip}
```

`ContextJson` signal emit'te şu alanları taşır:

```json
{
  "type": "vwap-ema-hybrid-v2",
  "vwap": 95123.45,
  "ema1h21Now": 94800.00,
  "ema1h21Prev": 94720.00,
  "slope": 0.00084,
  "prevBarClose": 95110.00,
  "lastBarClose": 95150.00,
  "vwapDistance": 0.00028,
  "volumeRatio": 1.08,
  "stopPct": 0.003,
  "tpGrossPct": 0.007,
  "maxHoldMinutes": 12
}
```

## Consequences

### Pozitif

- **Piyasa gerçekliği ile uyumlu hedef.** %0.7 gross TP 7-12 barda ulaşılabilir (research §1.2); ADR-0015'in %1-2 hedefinin aksine bar sayısı Binance 1m volatilitesi ile uyumlu.
- **Sinyal üretim olasılığı %0 → 4-8/saat tahmini.** Slope tolerance ve VWAP zone gevşemesi Loop 20'nin 11 kaçan sinyalini yakalar; yatay konsolidasyon günlerinde gate açılır.
- **Strateji kod yüzeyi minimal değişir.** Parametre tuning + seed update + formül aritmetiği. Yeni aggregate, yeni evaluator, yeni indicator service yok. Kod diff ≤ 100 satır (evaluator + appsettings + migration).
- **SystemEvents tablosu dolar — UI Sistem Olayları sayfası çalışır.** Loop 20 backlog kapanır. Clean Architecture disiplini korunur (abstraction Application, impl Infrastructure).
- **Break-even WR %36.4 — literatür WR %55-68 aralığından çok uzak.** Güvenlik marjı geniş; fee drag dahil pozitif EV.
- **Per-symbol risk ayarı.** XRP'nin yüksek volatilitesi (1m max %0.48) daha geniş SL (%0.4) ile karşılanır; BTC/BNB sıkı SL (%0.3) whipsaw riskini minimize eder.

### Negatif / Tradeoff

- **Whipsaw riski orta-yüksek.** BTC 15-bar std. sapma %0.12 — %0.3 SL false-trigger tahmini %8-12 (research §3.1). XRP bar max %0.48 — tek bar gürültü SL tetikleyebilir.
- **Fee drag ciddi.** 22 trade/gün × %0.2 round-trip = günde %4.4 fee minimum break-even. WR < %50 ise matematik çalışmaz (break-even WR %36.4 ama gerçek WR ölçülene kadar varsayım).
- **WR kanıtsız.** Loop 16-19 farklı strateji altında WR %43.75. VWAP+EMA literatürü %55-68 öneriyor ama Binance BTC/BNB/XRP 1m data'da lokal backtest yok. Loop 21 paper run canlı kanıt.
- **`TpSafetyFactor` ve `swingHigh` yolu deprecated.** İki parametre setinin yaşaması kısa vadeli karmaşa; Loop 22'de cleanup backlog.
- **13 yeni notification handler + 4 yeni domain event class.** Küçük dosyalar, SRP uyumlu ama dosya sayısı artışı review yükü getirir.
- **Data-migration DB wipe.** `DELETE FROM Strategies` + `UPDATE Positions SET StrategyId = NULL`. Loop 20'de açık pozisyon yok (0 trade) → güvenli; yine de migration'ın etkisi dokümante edilmeli.

### Nötr

- Aggregate sayısı değişmez.
- MediatR command/query yüzeyi değişmez.
- `IMarketIndicatorService` / `MarketIndicatorSnapshot` değişmez.
- Warmup stratejisi değişmez (1440 × 1m + 21 × 1h).
- Fan-out (per-mode) davranışı değişmez.
- ADR-0006 (testnet-first), ADR-0009 (REST backfill), ADR-0010 (backfill event suppression), ADR-0011 (sizing), ADR-0012 (monitor) uyumlu.

## Alternatives (Reddedilen)

### Alt-A — Weighted 3-of-4 Scoring

Her koşul 0.25 ağırlık, eşik ≥0.65 → `directionGate=false` iken skor 0.70 ile emit. Bear-gün `directionGate` güvencesi bozulur. Debug opak (hangi koşul kaç ağırlık?). **Red.**

### Alt-B — EMA12(15m) Daha Kısa Pencere Gate

21 saat → 3 saat window. Loop 16-19 deneyi (ADR-0014 §14.1 sonuç) 15m-only stratejide %100 time-stop, %0 TP hit — 15m trend gate faydasız kanıtlı. **Red.**

### Alt-C Standalone — Yalnızca VWAP Zone

Slope dokunulmaz, sadece VWAP zone ±%0.15. Net bear günlerde `directionGate=false` sabit → yine pasif. Tek başına Loop 20 sorununu çözmez. Slope E ile kombine edildiğinde çözer. **Standalone red.**

### Alt-D — Volume Multiplier 1.0

SMA20 ile eşit. Volume filtresi fiilen kalkar. Sinyal kalitesi düşer, fee drag artar. **Red.**

### Alt-E Standalone — Yalnızca Slope Tolerance

VWAP reclaim sıkı kalır. 11 kaçan sinyal yine kaçar. Tek başına eksik. **Standalone red.**

### Alt-F — ATR-Based Dinamik SL

`SL = 2.5 × ATR(14, 1m)` her sinyal için hesaplanır. Pragmatik ama implement maliyeti yüksek (ATR buffer, evaluator hesap). Research §3.4 sabit sembol-bazlı `StopPct` yeterli diyor. **Red — Loop 22+ backlog.**

### Alt-G — `StopPctPerSymbol` Sözlüğü Tek Seed'de

Üç sembol tek strateji seed'i paylaşsın, `ParametersJson` içinde `{"StopPctPerSymbol": {"BTCUSDT":0.003, ...}}`. ADR-0015 kararı üç seed olması (§15.1). Her seed kendi `StopPct`'sini taşıyınca sözlüğe gerek yok; tek seed tasarımı aggregate sınırı ile uyumsuz (fan-out per-symbol zaten ayrı). **Red.**

### Alt-H — SystemEvents Publisher Domain Service

Domain'e `ISystemEventPublisher` koy. `AppDbContext` bağımlılığı Infrastructure detayı; Domain saf C# + BCL kuralı. Dependency yönü bozulur. **Red.**

### Alt-I — SystemEvents Publisher Serilog Sink

Serilog'a custom sink yaz, `SystemEvents` tablosuna yazsın. Log-level filtering ve structured property extraction ile event-type map'lemek kırılgan; yeni event eklendiğinde sink'te filter güncellemek gerekir. MediatR notification handler pattern daha net, domain event → SystemEvent mapping dışsal ve typed. **Red.**

### Alt-J — Migration Yok, Seeder Upsert

Seeder her boot'ta match+update. Basit ama DB snapshot izlenebilirliği zayıflar (ADR-0015 §15.9 pattern'inden saparız). Tercih edilen yol migration. **Red kısmi — backlog revisit.**

## Source

- [ADR-0005 Risk Profile and Circuit Breaker](./0005-risk-profile-and-circuit-breaker.md)
- [ADR-0006 Testnet-First Policy](./0006-testnet-first-policy.md)
- [ADR-0009 REST Kline Backfill On Boot](./0009-rest-kline-backfill-on-boot.md)
- [ADR-0010 Backfill Event Suppression](./0010-backfill-event-suppression.md)
- [ADR-0011 Equity-Aware Sizing & Risk Tracking](./0011-equity-aware-sizing-and-risk-tracking.md)
- [ADR-0012 Trade Frequency Realism Reform](./0012-trade-frequency-realism-reform.md)
- [ADR-0015 VWAP-EMA Hybrid Strategy](./0015-vwap-ema-hybrid-strategy.md) — **supersedes**
- [`loops/loop_21/research-b-high-frequency.md`](../../loops/loop_21/research-b-high-frequency.md) — binance-expert volatilite + gevşeme analizi
- [`loops/loop_20/summary.md`](../../loops/loop_20/summary.md) — t150 halt, 0 sinyal kanıtı
- [`loops/loop_20/diagnosis-no-trade.md`](../../loops/loop_20/diagnosis-no-trade.md) — directionGate root cause
- [Tadonomics — EMA+VWAP %68 WR](https://tadonomics.com/best-indicators-for-scalping/)
- [PickMyTrade — EMA-VWAP Otomasyonlu 20+ Daily Trades](https://blog.pickmytrade.trade/ema-vwap-strategy-automated-scalping-tradovate/)
- [Cryptowisser — VWAP+Fibonacci+EMA R:R Önerisi](https://www.cryptowisser.com/guides/fibonacci-vwap-ema-crypto-scalping/)
- [Medium mintonfin — VWAP Scalp 0.25-0.5% Target](https://medium.com/@mintonfin/how-to-scalp-crypto-like-a-pro-the-best-scalping-strategies-that-actually-work-in-2025-717d0acd0872)
- [Flipster — ATR Stop-Loss](https://flipster.io/blog/atr-stop-loss-strategy)
- [Statista — BTC 14-day ATR %2.75](https://www.statista.com/statistics/1306877/bitcoin-price-swings/)
- Carver R., *Systematic Trading* (2015) — break-even WR formülü
- Chan E., *Algorithmic Trading* (2013) — EV hesabı çerçevesi
- [Microsoft Learn — DDD + CQRS](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/)
- [jasontaylordev/CleanArchitecture](https://github.com/jasontaylordev/CleanArchitecture)
- [ardalis/CleanArchitecture](https://github.com/ardalis/CleanArchitecture)
- [joelparkerhenderson/architecture-decision-record (MADR)](https://github.com/joelparkerhenderson/architecture-decision-record)
