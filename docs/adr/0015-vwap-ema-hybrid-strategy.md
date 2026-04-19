# 0015. VWAP-EMA Hybrid Strategy — Pattern-Scalping Surface Replacement

Date: 2026-04-19
Status: Accepted
Supersedes: [ADR-0014 Pattern-Based Scalping Reform](./0014-pattern-based-scalping-reform.md)

> Operasyonel detay (commit sırası, NuGet, test) backend-dev tarafından `loops/loop_20/decision-vwap-ema.md`'de üretilecek. Bu doküman normatif karardır.

## Context

### 1. Loop 16-19 Pattern-Scalping Sonucu — Edge Kanıtlanamadı

ADR-0014 ile kurulan `PatternScalpingEvaluator` + 14 `IPatternDetector` ailesi Loop 16-19 boyunca `Paper` modda canlı kline stream ile çalıştırıldı. Loop 19 sonu DB snapshot (`loops/loop_19/db-snapshot.md`):

| Metrik | Değer | Hedef |
|---|---|---|
| Closed positions | 16 | ≥ 20 |
| Win rate | %43.75 (7W/9L) | ≥ %55 |
| Net realized PnL | **-$0.39** (14 gün) | ≥ +%1 / trade |
| Avg hold | **9.82 dk** | — |
| Time-stop exit oranı | **%100** (16/16) | ≤ %20 (fallback) |
| TP hit oranı | **%0** | ≥ %50 |
| Fee drag | ~$0.78 (39 fill × ~$20 × %0.1) | — |

**Kritik tespit:** 16 pozisyonun tamamı 622s (10.37dk) time-stop ile kapandı — `Position.MaxHoldDuration` primary exit olarak görev yaptı, pattern detector'ların üzerinde anlaştığı `PatternResult.TakeProfit` hedefleri hiç gerçekleşmedi. Bu, pattern-based giriş sinyalinin **TP'ye ulaşacak momentum predict edemediğinin** deneysel kanıtıdır.

Ek olarak:
- R:R asimetrisi ters (avg win +$0.05, avg loss -$0.09 civarı — +$0.11 best vs -$0.27 worst);
- Pattern confidence skorları `0.55` threshold'u zor aşıyor, sinyal sıklığı düşük (16 pos / 14 gün ≈ 1.1/gün);
- Fee drag net PnL'in yarısı — brut hedef %0.2 taker fee altında pozitif değil.

### 2. binance-expert Araştırma Kararı

`loops/loop_19/research-reform.md` 267 satırda 8 strateji ailesi canlı Binance exchangeInfo + akademik kaynak taraması ile skorlandı:

| Strateji | Skor | Karar |
|---|---|---|
| **VWAP-Bounce + EMA21 Trend Filter** | **7/10** | Seçildi |
| EMA Pullback | 6/10 | Yedek |
| ORB / Donchian / BB Squeeze | 5/10 | Reddedildi |
| Candlestick Combo | 3/10 | Kanıtlanmış yetersiz (Loop 16-19) |
| RSI Divergence | 3/10 | PMC-NIH 2023 etkisiz |
| Order-Book Imbalance | 1/10 | 10sn horizon, fee ile karsız |

Seçilen hibritin kanıtı:
- VWAP bounce trend-günde WR %65-70 belgelenmis (Tradewink, LuxAlgo, medium/redsword_23261 çalışması);
- R:R 1:2 (stop %0.5 / TP %1.0-1.2) → fee %0.20 round-trip dahil break-even WR **%33.3**;
- Gerçek WR %65-70 → EV **+%0.406 net/trade** (brut %0.606 - fee %0.20);
- Binance mainnet + testnet exchangeInfo canlı doğrulandı (2026-04-19): BTCUSDT `stepSize 0.00001`, BNBUSDT `0.001`, XRPUSDT `0.1`, tümünde `minNotional = 5 USDT`. $20 nominal pozisyon üç sembolde de LOT_SIZE ve NOTIONAL filtrelerini geçer.

### 3. Kullanıcı Talepleri (Loop 19 PM handoff)

1. Saatte 4-6 trade (10-15 dk bir işlem);
2. Net **%1-2 kar / trade** (fee düşülmüş);
3. Sizing: `max(currentBalance × 0.20, 20.0)` USD (kartopu — kullanıcı keyfiyle yeniden hesaplanan %20 oransal büyüme);
4. Stop-loss zorunlu, take-profit "formasyon hedefinin biraz altında" (güvenli realize);
5. Spot long-only, `testnet` öncelik (ADR-0006 mainnet yasak);
6. Semboller: `BTCUSDT`, `BNBUSDT`, `XRPUSDT` (ETH ilerde).

## Decision

**Strateji yüzeyi ikinci kez reset edilir.** `StrategyType.PatternScalping` silinir, yerine `StrategyType.VwapEmaHybrid = 1` gelir. 14 `IPatternDetector` + `PatternScalpingEvaluator` dosya bazında silinir. Yeni `VwapEmaStrategyEvaluator` + `IMarketIndicatorService` çifti Clean Architecture katmanlarında aşağıdaki şekilde yerleşir.

### 15.1 Strateji Adı & Sembol Seed

Üç aktif seed (long-only, spot), `appsettings.json` `Strategies.Seed[]`:

| Name | Type | Symbols | Mode |
|---|---|---|---|
| `BTC-VwapEma-Scalper` | `VwapEmaHybrid` | `BTCUSDT` | Long-only |
| `BNB-VwapEma-Scalper` | `VwapEmaHybrid` | `BNBUSDT` | Long-only |
| `XRP-VwapEma-Scalper` | `VwapEmaHybrid` | `XRPUSDT` | Long-only |

### 15.2 Giriş Kuralları — Kapalı 1m Bar Bazında

Aşağıdaki **dört koşul da** aynı 1m bar kapanışında sağlanmalıdır. Evaluator `EvaluateAsync` içinden:

```
1. directionGate  : EMA21(1h) yukarı yönde (son iki 1h-EMA21 değeri artan)
2. vwapContext    : önceki bar (^2) kapanışı VWAP altında  (pullback)
3. vwapReclaim    : son kapalı bar (^1) kapanışı VWAP üstünde  (bullish reclaim)
4. volumeConfirm  : bar(^1).Volume >= SMA(Volume, 20) * VolumeMultiplier  (default 1.2)
```

Dört koşul true → `StrategySignalDirection.Long` emit edilir. Short tarafı üretilmez (spot long-only; ADR-0006 + kullanıcı kuralı).

**Neden 1h EMA21 (higher-timeframe gate):**
- 1m EMA whipsaw gürültüsünü eleyen yapısal filtre;
- Bear gününde sistem pasif kalır (WR'yi %65+ üstünde tutan ana koşul);
- 1h EMA21 bar hesabı için 21 saatlik warmup — REST backfill (ADR-0009) 1000 bar 1m + ek 21 bar 1h çekerek başlangıçta sağlanır (`§15.7`).

**Neden rolling 24h VWAP (UTC 00:00 reset değil):**
- Crypto 24/7 piyasa; "gün kapanışı" kavramı Binance'te kurumsal anlam taşımaz;
- Rolling 24h → `1440 bar × 1m` veya `288 bar × 5m` üzerinde tipik-fiyat ağırlıklı ortalama;
- UTC reset Asian session açılışında VWAP'ı sıfırdan kurar → ilk 2-3 saat sinyal üretemez. Rolling reddedilmez.

### 15.3 Çıkış Kuralları

| Exit | Mekanizma | Hedef |
|---|---|---|
| **Take-Profit (primary)** | `Position.TakeProfit = swingHigh(last 20 bars of 1m) × 0.95` | "Formasyon hedefinin biraz altında" — kullanıcı talep |
| **Stop-Loss (primary)** | `Position.StopPrice = entryPrice × (1 - stopPct)` — `stopPct` config, default `0.008` (%0.8) | VWAP altına yapışma invalidation |
| **Time-Stop (fallback)** | `Position.MaxHoldDuration = TimeSpan.FromMinutes(15)` | Sinyal yanıltıcı çıktı, fiyat yatay seyrediyor |

Mevcut `StopLossMonitorService` + `TakeProfitMonitorService` + Loop 16'da eklenen time-stop kolu **olduğu gibi korunur** — yeni monitor service yok. Evaluator sadece `StrategyEvaluation.SuggestedStopPrice` / `SuggestedTakeProfit` / `ContextJson.maxHoldMinutes` doldurur.

### 15.4 Position Sizing — Kullanıcı Kuralı

`IPositionSizingService` kontratı **değişmez** (ADR-0011 §11.1). `StrategySignalToOrderHandler` fan-out'u, yeni strateji için aşağıdaki `PositionSizingInput`'u kurar:

```
minNotionalOverride = max(equity * 0.20m, 20.0m)   // "kartopu min $20" kuralı
```

Bu override **strateji layer'ında** (handler'daki sizing input hazırlığı) uygulanır — `PositionSizingService` aritmetiği değişmez, `MinNotional` alanına `max(equityPct * 0.20, 20.0, exchangeMinNotional)` değerinin `max`'ı verilir. Exchange filtreleri canlı doğrulandı; $20 üç sembolde de LOT_SIZE + NOTIONAL geçer (research §1).

**Stop distance hesabı:** `stopDistance = entryPrice × stopPct`. Sizing servisi bu mesafeyi risk-pct ile `qtyByRisk`'a çevirir; `MaxPositionPct` cap'i yine aktif (`RiskProfile`).

### 15.5 DDD Aggregate Sınırı

Dependency kuralı (`src/CLAUDE.md`):

```
Domain ──▶ Application ──▶ Infrastructure ──▶ Api
```

| Aggregate / Type | Katman | Değişiklik |
|---|---|---|
| `Strategy` aggregate | Domain | Davranış aynı; `StrategyType` enum değeri yeniden isimlendirilir. Parametre validation yine `ParametersJson` opak alanında (JSON schema Application tarafında ele alınır). |
| `Position` aggregate | Domain | **Değişiklik yok.** `MaxHoldDuration`, `StopPrice`, `TakeProfit` alanları ADR-0014 §14.5 / ADR-0012 §12.4 / Loop 10'dan intact. |
| `Order` / `RiskProfile` aggregate'ları | Domain | Değişiklik yok. |
| `MarketIndicatorSnapshot` (value object, **yeni**) | Application | `record` — `Vwap`, `Ema1h21`, `VolumeSma20`, `SwingHigh20`, `AsOf` |
| `IMarketIndicatorService` (abstraction, **yeni**) | Application | `GetSnapshotAsync(string symbol, CancellationToken)` → `MarketIndicatorSnapshot?` |
| `IStrategyEvaluator` | Application | Kontrat değişmez |
| `VwapEmaStrategyEvaluator` (**yeni**) | Infrastructure | `IStrategyEvaluator.Type = VwapEmaHybrid`; 4 koşulu değerlendirir, leader snapshot ile `StrategyEvaluation` emit eder |
| `MarketIndicatorService` (**yeni**) | Infrastructure | `IMarketIndicatorService` impl; rolling buffer + 1m / 1h `Channel<Kline>` subscriber |
| `StrategySignalToOrderHandler` | Infrastructure | Sizing override eklenir (§15.4); fan-out mantığı korunur |

**Neden `IMarketIndicatorService` Application'da, impl Infrastructure'da:** Domain saf C# + BCL kuralına uymak için rolling buffer, Channel<T>, Skender.Stock.Indicators bağımlılığı Infrastructure'da kalır; Application kontrat sahibi.

**Neden yeni aggregate yok:** VWAP/EMA/SMA/swingHigh hiçbiri iş kuralı taşımaz — iterable kline stream'den **türetilen** indikatörler. Domain invariant'ı yok. `MarketIndicatorSnapshot` saf veri taşıyıcı (value object / record). Yeni aggregate uydurmak DDD'de anemic model üretir — reddedilir.

### 15.6 Contract İmzaları

**Application / `IMarketIndicatorService.cs`:**

```csharp
namespace BinanceBot.Application.Strategies.Indicators;

public interface IMarketIndicatorService
{
    /// <summary>
    /// Sembol için son kapalı 1m bar üzerinden türetilen indikatör snapshot'ı.
    /// Warmup (VWAP 1440 bar veya EMA21 1h başlangıcı) tamamlanmamışsa null.
    /// </summary>
    MarketIndicatorSnapshot? TryGetSnapshot(string symbol);
}

public sealed record MarketIndicatorSnapshot(
    decimal Vwap,
    decimal PrevBarClose,       // bar[^2].Close — vwap-below check icin
    decimal LastBarClose,       // bar[^1].Close — vwap-reclaim check icin
    decimal LastBarVolume,
    decimal VolumeSma20,
    decimal Ema1h21Now,
    decimal Ema1h21Prev,        // yon gate: Now > Prev -> uptrend
    decimal SwingHigh20,
    DateTimeOffset AsOf);
```

**Infrastructure / `VwapEmaStrategyEvaluator.cs`:**

```csharp
namespace BinanceBot.Infrastructure.Strategies.Evaluators;

public sealed class VwapEmaStrategyEvaluator : IStrategyEvaluator
{
    public StrategyType Type => StrategyType.VwapEmaHybrid;

    private readonly IMarketIndicatorService _indicators;
    private readonly ILogger<VwapEmaStrategyEvaluator> _logger;

    public VwapEmaStrategyEvaluator(
        IMarketIndicatorService indicators,
        ILogger<VwapEmaStrategyEvaluator> logger) { ... }

    public Task<StrategyEvaluation?> EvaluateAsync(
        long strategyId, string parametersJson, string symbol,
        IReadOnlyList<Kline> closedBars, CancellationToken ct);

    private sealed class Parameters
    {
        public decimal StopPct            { get; set; } = 0.008m;  // %0.8
        public decimal VolumeMultiplier   { get; set; } = 1.2m;
        public int     SwingLookback      { get; set; } = 20;
        public decimal TpSafetyFactor     { get; set; } = 0.95m;  // swingHigh × 0.95
        public int     MaxHoldMinutes     { get; set; } = 15;
        public int     VwapRollingBars1m  { get; set; } = 1440;   // 24h
        public int     EmaPeriod1h        { get; set; } = 21;
    }
}
```

**Infrastructure / `MarketIndicatorService.cs`:**

```csharp
public sealed class MarketIndicatorService
    : IMarketIndicatorService, IHostedService
{
    // Per-symbol rolling buffers:
    //   - 1m bars   : ConcurrentDictionary<string, CircularBuffer<Kline>> (cap = 1440)
    //   - 1h bars   : ConcurrentDictionary<string, CircularBuffer<Kline>> (cap = 30)
    //
    // Kline stream subscription via IBinanceMarketStream (1m live) + REST backfill
    // (ADR-0009) on boot. 1h buckets aggregated locally from 1m bars (veya REST
    // klines endpoint).
    //
    // Calculations pure-decimal; helpers Indicators.cs sanitize edilir (VWAP + SwingHigh ekle).
    public MarketIndicatorSnapshot? TryGetSnapshot(string symbol) { ... }
}
```

### 15.7 Warmup Stratejisi

| Indicator | Gereken tarih | Nereden |
|---|---|---|
| VWAP rolling 24h | 1440 × 1m kapalı bar | REST `/api/v3/klines` `interval=1m limit=1000` iki çağrı (ilk bar backfill zaten ADR-0009) |
| EMA21 1h | 21 × 1h kapalı bar (21 saat) | REST `/api/v3/klines` `interval=1h limit=50` **tek çağrı boot sırasında** |
| SwingHigh 20 | 20 × 1m kapalı bar | Aynı VWAP backfill buffer'ından |
| VolumeSma 20 | 20 × 1m kapalı bar | Aynı buffer |

`IMarketIndicatorService.TryGetSnapshot` warmup tamamlanmamışsa `null` döner. `StrategyEvaluationHandler` null snapshot'ta evaluator'ı çağırmaz (veya evaluator null snapshot gördüğünde early-return eder — tercih #2 net log için daha iyi). Backfill ADR-0010 event suppression kuralıyla uyumlu — backfill bar'ları `KlineClosedEvent` emit etmez, sadece rolling buffer'ı doldurur.

### 15.8 SİLİNECEK / DEĞİŞECEK / YENİ Dosya Listesi

**SİLİNİR** (toplam 17+ dosya):

```
src/Infrastructure/Strategies/Evaluators/PatternScalpingEvaluator.cs
src/Infrastructure/Strategies/Patterns/PatternFilters.cs
src/Infrastructure/Strategies/Patterns/Detectors/
  ├─ AscendingTriangleDetector.cs
  ├─ BearFlagDetector.cs
  ├─ BearishEngulfingDetector.cs
  ├─ BullFlagDetector.cs
  ├─ BullishEngulfingDetector.cs
  ├─ DescendingTriangleDetector.cs
  ├─ DoubleBottomDetector.cs
  ├─ DoubleTopDetector.cs
  ├─ EveningStarDetector.cs
  ├─ HammerDetector.cs
  ├─ MorningStarDetector.cs
  ├─ ShootingStarDetector.cs
  ├─ ThreeBlackCrowsDetector.cs
  └─ ThreeWhiteSoldiersDetector.cs
src/Application/Strategies/Patterns/IPatternDetector.cs
tests/Tests/Infrastructure/Strategies/Patterns/**    (varsa tüm detector test dosyaları)
tests/Tests/Infrastructure/Strategies/PatternScalpingEvaluatorTests.cs  (varsa)
```

NuGet: `OHLC_Candlestick_Patterns` **kaldırılır** (Infrastructure.csproj'ten). `Skender.Stock.Indicators` **korunur** (VWAP + EMA + SMA için faydalı; alternatif: `Indicators.cs` helper'a manuel eklenir).

**DEĞİŞİR:**

| Dosya | Değişiklik |
|---|---|
| `src/Domain/Strategies/StrategyEnums.cs` | `PatternScalping = 1` → `VwapEmaHybrid = 1` (enum int değer **yeniden kullanılır**; migration DB wipe içinde §15.9) |
| `src/Infrastructure/Strategies/Evaluators/Indicators.cs` | `Vwap`, `VolumeSma`, `SwingHigh` helper'ları eklenir |
| `src/Infrastructure/DependencyInjection.cs` | 14 detector + `PatternScalpingEvaluator` registrationları silinir; `VwapEmaStrategyEvaluator` + `MarketIndicatorService` (singleton + HostedService) eklenir |
| `src/Infrastructure/Strategies/StrategySignalToOrderHandler.cs` | Sizing input'a `MinNotional = max(equity × 0.20, 20, exchangeMin)` override eklenir |
| `src/Api/appsettings.json` (+ `appsettings.Development.json`) | `Strategies.Seed[]` üç seed: `BTC/BNB/XRP-VwapEma-Scalper`, `Type=VwapEmaHybrid` |

**YENİ:**

```
src/Application/Strategies/Indicators/IMarketIndicatorService.cs
src/Application/Strategies/Indicators/MarketIndicatorSnapshot.cs    (partial record, tek dosyada da olabilir)
src/Infrastructure/Strategies/Indicators/MarketIndicatorService.cs
src/Infrastructure/Strategies/Indicators/IndicatorRollingBuffer.cs   (internal circular buffer)
src/Infrastructure/Strategies/Evaluators/VwapEmaStrategyEvaluator.cs
tests/Tests/Application/Strategies/VwapEmaStrategyEvaluatorTests.cs
tests/Tests/Infrastructure/Strategies/MarketIndicatorServiceTests.cs
```

### 15.9 Migration & DB Reset

Enum değeri `1`'i yeniden kullanmak için `Strategies` ve `StrategySignals` tabloları **wipe edilir** (ADR-0014 §14.1'deki aynı strateji):

Migration `Loop19VwapEmaHybridReset` `Up()`:

```
migrationBuilder.Sql("DELETE FROM StrategySignals;");
migrationBuilder.Sql("UPDATE Positions SET StrategyId = NULL;");
migrationBuilder.Sql("DELETE FROM Strategies;");
```

Boot'ta `StrategyConfigSeeder` yeni `appsettings.Strategies.Seed[]`'i upsert eder → 3 yeni row. Loop 19 sonu DB zaten user tarafından reset ediliyor (user-notes Loop 19 decision); migration ekstra koruma.

Komut:
```
dotnet ef migrations add Loop19VwapEmaHybridReset --project Infrastructure --startup-project Api
```

### 15.10 Backward Compatibility

| ADR / Aggregate | Etkilenir mi? |
|---|---|
| ADR-0006 testnet-first | Uyumlu — mainnet branch sizing öncesi skip |
| ADR-0008 §8.3 fan-out per mode | Uyumlu — `StrategySignalEmittedEvent` aynı |
| ADR-0008 §8.6 mode-scoped RiskProfile | Uyumlu |
| ADR-0009 REST kline backfill | **Uyumlu, genişler** — 1h backfill eklenir (`§15.7`) |
| ADR-0010 backfill event suppression | Uyumlu — VWAP buffer backfill-skip modunda da dolar |
| ADR-0011 §11.4 sizing service | Uyumlu — sizing service aritmetiği değişmez; `MinNotional` input override handler'da |
| ADR-0011 §11.6 `Position.Open(..., maxHoldDuration)` | Uyumlu — evaluator `ContextJson.maxHoldMinutes` verir, handler `TimeSpan.FromMinutes` ile geçer |
| ADR-0012 §12.3 StopLossMonitor + time-stop kolu | Uyumlu |
| ADR-0012 §12.4 `Position.StopPrice` | Uyumlu — `SuggestedStopPrice` emit edilir |
| ADR-0014 (tüm §) | **Supersedes** — pattern detector ailesi ve evaluator silinir |

### 15.11 Logging Kontratı

`VwapEmaStrategyEvaluator` her `EvaluateAsync` çağrısında en az bir `_logger.LogDebug` atar:

```
VwapEma: symbol={Symbol} directionGate={Up} vwapContext={Below} 
         reclaim={Reclaim} volumeRatio={Ratio} decision={Emit|Skip}
```

Signal emit edildiğinde `StrategyEvaluation.ContextJson` şu alanları taşır (audit/UI tooltip için):

```json
{
  "type": "vwap-ema-hybrid",
  "vwap": 95123.45,
  "ema1h21Now": 94800.00,
  "ema1h21Prev": 94720.00,
  "prevBarClose": 95110.00,
  "lastBarClose": 95150.00,
  "volumeRatio": 1.35,
  "swingHigh20": 96200.00,
  "stopPct": 0.008,
  "maxHoldMinutes": 15
}
```

## Consequences

### Pozitif

- **TP realize edilebilir hedef.** `swingHigh × 0.95` fiyatın 1m lookback içinde test edilmiş seviyesi; pattern-detector'ların abstract "konfor hedefi" değil. Kullanıcı beklentisi "formasyon hedefinin biraz altında" birebir karşılanıyor.
- **EMA21 1h higher-timeframe gate whipsaw'ı çoğunlukla keser.** Sistem bear gününde pasif kalır (sinyal üretmez) — bu sessiz gün, zararlı gün değil. Loop 16-19'da yok olan koşul.
- **Sizing kullanıcı kuralını koruyor.** `max(equity × 0.20, 20.0)` → kartopu büyüdükçe pozisyon büyür; minimum tabanında Binance NOTIONAL filtresi rahat karşılanır.
- **Break-even WR %33 — %65 WR'de büyük güvenlik marjı.** Fee %0.20 round-trip dahil hesap net pozitif EV.
- **Clean Architecture bağımlılık yönü temiz.** `IMarketIndicatorService` Application'da, impl Infrastructure'da; evaluator rolling buffer bilmiyor, sadece snapshot okuyor. Unit test'te `IMarketIndicatorService` mock ile evaluator izole test.
- **Tek evaluator + tek indicator service.** 14 detector class + pattern filter hesap tekrarı yok. Kod yüzeyi %60+ küçülür.
- **Pattern `Indicators.cs` helper korunur ve genişler.** VWAP + SwingHigh + VolumeSma eklenir — DRY; Loop 20+ yeni stratejiler (EMA pullback yedek) aynı helper'ı kullanır.

### Negatif / Tradeoff

- **Warmup gereksinimi 24h 1m + 21h 1h.** Boot'ta iki backfill çağrısı: 1440 1m bar + 21+ 1h bar × 3 sembol = 6 REST kline call. Binance rate-limit 1200 weight/min × `1440/1000 ≈ 2 call per symbol × 3 = 6 call @ weight 5 = 30 weight` + 3 × weight 5 = 45 toplam — trivial. Risk **yok**; yine de initial startup 2-3 saniye uzar.
- **Sinyal sıklığı tahmini 4-6/saat — kanıtlanmadı canlı data'da.** Research tahmin WR %65-70; gerçek Binance BTC/BNB/XRP 1m data'da lokal backtest yapılmadı. Loop 20 paper test canlı kanıt.
- **Short tarafı yok — bear trend'lerde sistem pasif.** Kullanıcı kuralı spot long-only; kabul. Bear süresince PnL değişmez (zarar etmez, kar da etmez).
- **VWAP reset stratejisi rolling 24h — memory footprint 1440 × 3 sembol × ~200 byte = ~870 KB per process.** İhmal edilebilir.
- **Breaking DB change.** `Strategies` tablosu wipe; açık pozisyonlar `StrategyId = NULL`'a düşer. Loop 19 sonu kullanıcı zaten reset ediyor; migration ekstra güvence.
- **`OHLC_Candlestick_Patterns` NuGet silinir.** Gelecek pattern ihtiyacında geri eklenir; ADR-0014 scope kapanır.
- **`StrategyEvaluation.ContextJson` `maxHoldMinutes` opak.** ADR-0014 §14.4'te raporlanan magic-string risk ADR-0015'te de devam eder — Loop 21+ `StrategyEvaluation` typed field terfi backlog.

### Nötr

- Domain event sayısı değişmez (`StrategySignalEmittedEvent`, `PositionOpenedEvent`, `PositionClosedEvent`);
- Yeni MediatR command/query yok;
- Migration tek (`Loop19VwapEmaHybridReset`);
- Fan-out davranışı değişmez;
- Logging kontratı (`agent-bus.append_decision`) korunur.

## Alternatives (Reddedilen)

### Alt-1: Opening Range Breakout (ORB)

**Red sebep:** Crypto 24/7 session open yok. Saatlik reset 24 potansiyel ORB aralığı üretir; double-break oranı %66.93 (Edgeful ES futures) crypto volatilitesinde daha yüksek beklenir → stop-hunt dominant. Volume filtresi zorunlu, implement karmaşıklığı VWAP-EMA üstünde.

### Alt-2: Donchian / Keltner Breakout

**Red sebep:** False breakout kripto'da yüksek; WR %30-45, R:R 3:1 — fee %0.20 sonrası pozitif EV'ye ATR-stop uzunluğu gerekiyor; XRPUSDT likiditesi test edilmedi. 10-15dk hedefle uyumlu ama sinyal kalitesi VWAP-EMA'nın altında.

### Alt-3: RSI Divergence + Candle Confirm

**Red sebep:** PMC/NIH 2023 (pmc.ncbi.nlm.nih.gov/articles/PMC9920669): standart RSI kripto'da etkisiz; divergence occurrence rate %0.8 — saatte 0-1 sinyal bile zor. Net %1 hedefle uyumsuz.

### Alt-4: Order-Book Imbalance

**Red sebep:** Towards Data Science (price-impact-of-order-book-imbalance): tahmin ufku 10 saniye, expected return < 10 bps; round-trip fee 20 bps. Alone karlı değil. Retail bot latency sub-second erişemez.

### Alt-5: Candlestick-Only (ADR-0014 devamı)

**Red sebep:** Loop 16-19 deneysel kanıt (16 pos / %43.75 WR / %0 TP hit / -$0.39 net). Tobi Lux 2024 DAX null hypothesis reddedilemedi; tek-pattern edge'siz. Weighted vote denendi — gerçek verilerde yine edge yok.

### Alt-6: Bollinger Squeeze Mean-Reversion

**Red sebep:** Spot long-only — aşağı breakout bekleme → boş gün; BB extreme → orta bant dönüşü ~%0.5-0.8, %1 net hedefine yetersiz.

### Alt-7: `StrategyType` yeni sayı (`VwapEmaHybrid = 2`), eski `PatternScalping` enum value tut

**Red sebep:** ADR-0014 §14.1 aynı gerekçe. Ölü strateji DB'de durmaz (evaluator yok → log spam); UI'da ölü seed gözükür. Enum value reuse güvenli çünkü migration DB wipe ediyor.

### Alt-8: VWAP reset UTC 00:00

**Red sebep:** Asia open'da sıfırdan başlar → ilk 2-3 saat sinyal yok; crypto 24/7 kurumsal session kavramı yok; rolling 24h literatürde de öneriliyor (LuxAlgo, Tradewink). Rolling seçildi.

### Alt-9: `MarketIndicatorService` Domain Service

**Red sebep:** Rolling buffer + Channel<Kline> + Skender.Stock.Indicators bağımlılığı Infrastructure detayı; Domain saf C# + BCL kuralı (`src/CLAUDE.md`). Application'da interface, Infrastructure'da impl → dependency rule ihlali yok.

### Alt-10: Her sembol için ayrı evaluator class

**Red sebep:** SRP ihlali; 3 class × 4 method aynı kod. Tek `VwapEmaStrategyEvaluator` + `parametersJson` ile sembol-spesifik `stopPct`/`VolumeMultiplier` override. Fan-out handler + sizing service zaten per-symbol çalışıyor.

## Source

- [ADR-0006 Testnet-First Policy](./0006-testnet-first-policy.md)
- [ADR-0008 Trading Modes](./0008-trading-modes.md) §8.2 §8.3 §8.6
- [ADR-0009 REST Kline Backfill On Boot](./0009-rest-kline-backfill-on-boot.md)
- [ADR-0010 Backfill Event Suppression](./0010-backfill-event-suppression.md)
- [ADR-0011 Equity-Aware Sizing & Risk Tracking](./0011-equity-aware-sizing-and-risk-tracking.md) §11.1 §11.4 §11.6
- [ADR-0012 Trade Frequency Realism Reform](./0012-trade-frequency-realism-reform.md) §12.3 §12.4
- [ADR-0014 Pattern-Based Scalping Reform](./0014-pattern-based-scalping-reform.md) — **supersedes**
- [`loops/loop_19/research-reform.md`](../../loops/loop_19/research-reform.md) — binance-expert strateji seçim raporu
- [`loops/loop_19/db-snapshot.md`](../../loops/loop_19/db-snapshot.md) — Loop 16-19 pattern-scalping gerçek sonuç
- Binance exchangeInfo canlı doğrulama (2026-04-19): `https://api.binance.com/api/v3/exchangeInfo` + `https://testnet.binance.vision/api/v3/exchangeInfo`
- [Tradewink — VWAP Bounce Strategy](https://www.tradewink.com/learn/vwap-bounce-trading-strategy)
- [Multi-Period EMA+VWAP High Win Rate Intraday Strategy](https://medium.com/@redsword_23261/multi-period-ema-crossover-with-vwap-high-win-rate-intraday-trading-strategy-54ca8955bb38)
- [ScienceDirect — Intraday Momentum in Crypto](https://www.sciencedirect.com/science/article/abs/pii/S1062940822000833)
- [PMC/NIH RSI Crypto 2023 (reddedilen alternatif)](https://pmc.ncbi.nlm.nih.gov/articles/PMC9920669/)
- [Skender.Stock.Indicators (NuGet, Apache 2.0)](https://github.com/DaveSkender/Stock.Indicators)
- [Microsoft Learn — DDD + CQRS Patterns](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/)
- [jasontaylordev/CleanArchitecture](https://github.com/jasontaylordev/CleanArchitecture)
- [ardalis/CleanArchitecture](https://github.com/ardalis/CleanArchitecture)
