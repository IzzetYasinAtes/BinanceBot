# 0014. Pattern-Based Scalping Reform — Strategy Surface Reset

Date: 2026-04-17
Status: Accepted

> Operasyonel detay (NuGet komutları, dosya/satır listesi, commit-by-commit kod sablonu, agent zinciri) icin bkz. [`loops/loop_16/decision-pattern-reform.md`](../../loops/loop_16/decision-pattern-reform.md). Bu dokuman normatif karardir.

## Context

Loop 4 → Loop 15 boyunca uygulanan `Grid` / `TrendFollowing` / `MeanReversion` strateji uclusu bir kac mimari ve istatistiki sorun ile tıkandi:

1. **Edge yok.** Loop 4 (`loops/loop_4/summary.md`) Paper portfoy `$100 → $95.30` (-%4.7); Loop 12-15 frequency-tuning + take-profit reformu sonrasi bile WR `<%50`, R:R `<1.0`. Round-trip taker fee `%0.20` (binance-expert AR-GE `loops/loop_16/research-pattern-scalping.md` Bolum 0) en az `%50 WR` + `R:R 1.0` istiyor; ucumuz da bu esiği aşamadi.
2. **Stratejik tasarim duplicasyon.** Ucu de "tek indikator + tek kosul" mantigi (EMA cross / RSI+BB / fiyat band) — `IStrategyEvaluator` arayuzu pure-function olmasina ragmen evaluator ailesi homojen degil, parametre alanlari her seed'de farkli, fan-out handler bunu opaque `parametersJson` ile tasiyor; backend-dev her parametre eklemesinde 3 seed + 3 evaluator class touch ediyor. **Acik-kapali ihlali** (yeni strateji = yeni evaluator class + yeni `StrategyType` enum value + yeni seed + yeni test sınıfı).
3. **Akademik bulgu — tek pattern edge'siz.** Tobi Lux (2024) DAX backtest (`loops/loop_16/research-pattern-scalping.md` §A1): TA-Lib 61 candlestick pattern, KS+Mann-Whitney null hypothesis reddedilemedi → tek-pattern girisi `random` ile istatistiksel ozdes. Pattern edge ancak **(a) cogul pattern weighted vote, (b) trend/volume/RSI filtre, (c) ozenli stop+TP+time stop** ile cikar.
4. **Crypto reality.** 1m bar noise daily chart'a gore `~10x` agir; Binance spot 24/7 (gap yok) → `Morning/Evening Star`'ın gap kosulu uygulanmaz; testnet XRP/USDT likiditesi sınırlı → pattern WR'sini `%5-10` asagi revize etmek lazim.
5. **C# library ekosistemi olgun.** `OHLC_Candlestick_Patterns` NuGet (37+37 candle + 9+9 chart pattern) `.NET 8 Standard`, native dependency yok, .NET 10 uyumlu. `Skender.Stock.Indicators` aktif bakimli RSI/EMA/ATR; iki paket ile pure-C# scalping stack.

binance-expert AR-GE (`loops/loop_16/research-pattern-scalping.md` 401 satir) 7 pattern grubu (14 detector — bullish + bearish), Bulkowski + chartpatternspro + altFINS + Luxalgo WR'lerine dayali statik agirliklar (`0.55-0.85`), confidence threshold `≥0.55 entry / ≥0.75 strong`, time stop `10 dk` (max 7 bar), R:R `1.5-2.0`, hedef gunde 15 trade × `%55+` WR ile portfoyde `~%3` net onerdi.

PM bu AR-GE'yi mimari karara cevirmemi istedi: **strateji yuzeyini sifirla, pattern detector ailesi + weighted scalping evaluator ekle, mevcut Position/Order/RiskProfile/StopLoss/TakeProfit aggregate semasini koru.**

## Decision

`StrategyType` enum'u **breaking change** ile yeniden duzenlenir; `Grid`/`TrendFollowing`/`MeanReversion` **silinir**, tek deger `PatternScalping = 1` kalir. Tum pattern algoritmalari `IPatternDetector` arayuzu altında 14 ayri Infrastructure sinifi olur. Tek `PatternScalpingEvaluator` (`IStrategyEvaluator` impl) tum detector'lari toplar, weighted vote ile yon karar verir, en yuksek-confidence pattern'in stop+TP'sini kullanir. Domain davranisi minimaldir: yeni `Position.MaxHoldDuration` field'i (nullable) + mevcut `StopLossMonitorService`'in icine zaman stop kolu eklenir. Yeni aggregate yok.

### 14.1 StrategyType Enum Reform — Breaking Change Kabul

**Karar:** `src/Domain/Strategies/StrategyEnums.cs` `StrategyType`:

```csharp
public enum StrategyType
{
    PatternScalping = 1,
}
```

**Eski degerler (`Grid=1, TrendFollowing=2, MeanReversion=3`) tamamen silinir.** Numara geri kullanilir (`PatternScalping=1`) cunku:

- DB migration sirasinda `Strategies` tablosundaki tum mevcut row'lar yeni seed ile **silinip yeniden yazilacak** (yeni isimler, yeni parametre semasi, eski parametrejson uyumsuz).
- `StrategySignals` tablosundaki gecmis sinyaller `StrategyId` foreign key uzerinden cascade siliniyor (mevcut FK `OnDelete: Cascade`).
- `Positions` tablosunun `StrategyId` kolonu nullable; eski strateji row'u silinen acik pozisyonlarin `StrategyId`'si NULL'a duser → operasyonel olarak kabul edilir cunku Loop 16 oncesi pozisyon kapatilmis olmali (test/paper modda tek pozisyon snapshot'i kayda deger degil).

**Migration:** `dotnet ef migrations add Loop16PatternScalpingReset --project Infrastructure --startup-project Api`

Migration ek olarak `Up()` icinde:

```csharp
migrationBuilder.Sql("DELETE FROM StrategySignals;");
migrationBuilder.Sql("UPDATE Positions SET StrategyId = NULL;");
migrationBuilder.Sql("DELETE FROM Strategies;");
```

Boot'ta `StrategyConfigSeeder` yeni seed'i basar (`§14.6`). Idempotent.

**Reddedilen Alternatif A — yeni numara (`PatternScalping = 4`).** Eski 1/2/3'u DB'de tutmak. Reddedildi cunku evaluator yok (`StrategyEvaluatorRegistry.Resolve` null doner) → `StrategyEvaluationHandler` her bar log spam atar; UI'da olu strateji gosterilir. Temizlik elzem.

**Reddedilen Alternatif B — `[Obsolete]` markaj + transition donemi.** Reddedildi — paper/testnet modunda gercek user/data yok, breaking change risksiz; transition complexity YAGNI.

**Cakisma kontrolu:** `StrategyConfiguration.cs:19` `HasConversion<int>()` → degisiklik yok (enum `int` map'leniyor). `StrategyConfigSeeder` (Loop 5+) parametrejson'u opaque string okuyor, semasi degisse de upsert calisir.

### 14.2 IPatternDetector Arayuzu (Application Katmani)

**Konum:** `src/Application/Strategies/Patterns/IPatternDetector.cs`

```csharp
namespace BinanceBot.Application.Strategies.Patterns;

using BinanceBot.Domain.MarketData;

public interface IPatternDetector
{
    PatternType Type { get; }
    decimal Weight { get; }
    PatternResult? Detect(IReadOnlyList<Kline> closedBars);
}

public sealed record PatternResult(
    PatternType Type,
    PatternDirection Direction,
    decimal Confidence,
    decimal EntryPrice,
    decimal StopPrice,
    decimal TakeProfit,
    int MaxHoldBars,
    string ContextJson);

public enum PatternDirection
{
    Long = 1,
    Short = 2,
}

public enum PatternType
{
    DoubleBottom = 1,
    DoubleTop = 2,
    ThreeWhiteSoldiers = 3,
    ThreeBlackCrows = 4,
    MorningStar = 5,
    EveningStar = 6,
    BullFlag = 7,
    BearFlag = 8,
    AscendingTriangle = 9,
    DescendingTriangle = 10,
    Hammer = 11,
    ShootingStar = 12,
    BullishEngulfing = 13,
    BearishEngulfing = 14,
}
```

**Tasarim ozellikleri:**

- `Detect` `Task` donmez — pure synchronous calculation; her detector saliseden az calisir, async overhead israf. `IStrategyEvaluator.EvaluateAsync` async kalir (DB I/O bagimliligi).
- `Weight` interface property — `decimal` (sabit) — Loop 16'da statik (Bulkowski/Luxalgo WR), Loop 17+ adaptive (`§14.11`).
- `Confidence` per-detection: filtre kombinasyonuna gore `0-1` (volume confirm `+0.10`, RSI bant ici `+0.10`, trend context `+0.15`, gibi).
- `EntryPrice` evaluator tarafindan `latest.ClosePrice`'a override edilebilir, ama detector kendi onerisini sunar (orn. Pinbar wick uzerine LIMIT_MAKER icin).
- `MaxHoldBars` per-pattern (research D4): Engulfing 5, Pinbar 5, Star 7, Flag/Triangle/Double 10. Evaluator pattern'in onerisini Position'a tasiyor; StopLossMonitor zamanli kapatma yapiyor (`§14.5`).
- `ContextJson` debug + UI tooltip + audit log icin (hangi pattern, hangi confidence, hangi filtreler tetikledi).

**Gerekce — Application'da degil Domain'de neden:** Pattern algoritmasi **infrastructure detayi** (3rd-party NuGet pattern detector'una sarmal, indicator calculation library'sine bagimli); Domain saf C# + BCL; pattern interface Application'da cunku `IStrategyEvaluator` ile ayni katmanda — handler injection kontratin ayni seviyede oldugunu belirtir.

### 14.3 14 Detector — Infrastructure Implementations

**Konum:** `src/Infrastructure/Strategies/Patterns/Detectors/<Name>Detector.cs`

Sprint 1 (sırasıyla, agirlik sirasi):

| # | Sinif | Type | Weight | Lib |
|---|---|---|---|---|
| 1 | `DoubleBottomDetector` | `DoubleBottom` | `0.85` | Custom (pivot-low pair) |
| 2 | `DoubleTopDetector` | `DoubleTop` | `0.85` | Custom (pivot-high pair) |
| 3 | `ThreeWhiteSoldiersDetector` | `ThreeWhiteSoldiers` | `0.78` | OHLC NuGet |
| 4 | `ThreeBlackCrowsDetector` | `ThreeBlackCrows` | `0.78` | OHLC NuGet |
| 5 | `MorningStarDetector` | `MorningStar` | `0.67` | OHLC NuGet (gap kontrolu kapali) |
| 6 | `EveningStarDetector` | `EveningStar` | `0.67` | OHLC NuGet (gap kontrolu kapali) |
| 7 | `BullFlagDetector` | `BullFlag` | `0.64` | Custom (flagpole + konsolidasyon + breakout) |
| 8 | `BearFlagDetector` | `BearFlag` | `0.64` | Custom |

Sprint 2 (filtre bagimli, daha noise):

| # | Sinif | Type | Weight | Lib |
|---|---|---|---|---|
| 9 | `AscendingTriangleDetector` | `AscendingTriangle` | `0.62` | Custom (trendline + flat resistance) |
| 10 | `DescendingTriangleDetector` | `DescendingTriangle` | `0.62` | Custom |
| 11 | `HammerDetector` | `Hammer` | `0.58` | OHLC NuGet |
| 12 | `ShootingStarDetector` | `ShootingStar` | `0.58` | OHLC NuGet |
| 13 | `BullishEngulfingDetector` | `BullishEngulfing` | `0.55` | OHLC NuGet (sadece filtre ile) |
| 14 | `BearishEngulfingDetector` | `BearishEngulfing` | `0.55` | OHLC NuGet (sadece filtre ile) |

**Confidence puanlama (her detector icindeki ortak kural):**

```
baseConfidence = pattern detected ? 0.50 : 0
+ trendContext (EMA20 yon ile uyumlu)        +0.15
+ volumeConfirm (last bar vol ≥ 1.5× avg(20)) +0.10
+ rsiBand (40 ≤ rsi ≤ 60 reversal'a iyi)     +0.10
+ srProximity (pivot S/R seviyesinden ±%0.5)  +0.10
total clamped [0, 1]
```

Her detector kendi pattern-spesifik filtresini de uygular (ornegin Engulfing icin `Volume` zorunlu, filtre yoksa pattern hic emit edilmez).

**Detay — orn. `BullishEngulfingDetector`:**

```
1. closedBars.Count >= 22 (RSI(14) + 2 bar + 5 bar EMA20 warmup)
2. b1 = closedBars[^2], b2 = closedBars[^1]
3. b1.Close < b1.Open  // bearish
4. b2.Close > b2.Open  // bullish
5. b2.Open <= b1.Close && b2.Close >= b1.Open  // engulf
6. rsi(closedBars, 14) ∈ [40, 60] dışıysa skip (whipsaw)
7. volume(b2) >= avg(volume, 20) * 1.5 değilse skip
8. ema20 = Ema(closedBars, 20)
9. trend = b2.Close > ema20 ? "up" : "down"
10. baseConf = 0.50
   + trend == "up" ? +0.15 : 0  // bullish engulf trend up'da daha güçlü
   + +0.10 (volume passed)
   + +0.10 (rsi band passed)
11. entry = b2.Close (next bar açılışı için confirmation kullanan handler'a hint)
12. stop = b1.Low - 0.0001 (engulfing bar uç altı)
13. tp = entry + (entry - stop) * 1.5  // R:R 1.5
14. maxHoldBars = 5
15. return PatternResult(BullishEngulfing, Long, totalConf, entry, stop, tp, 5, contextJson)
```

Detector kalıbı tum 14 sınıfta benzer; `OHLC_Candlestick_Patterns` paketi `Engulfing/MorningStar/Hammer/ThreeSoldiers/...` icin temel pattern bool'unu doner, bizim sınıf etrafa filtre + confidence + stop/TP ekler.

**Reddedilen Alternatif — tek mega-class (PatternScanner) icinde 14 method.** Reddedildi: SRP ihlali, unit test guc, weight/confidence yapisi reuse edilemez, yeni pattern ekleme-silme commit acisi acgozlu. **Detector-per-pattern** acik-kapali prensibi (yeni pattern = yeni sınıf + DI registration; mevcut hicbir kod değismez).

### 14.4 PatternScalpingEvaluator (Infrastructure)

**Konum:** `src/Infrastructure/Strategies/Evaluators/PatternScalpingEvaluator.cs`

```csharp
public sealed class PatternScalpingEvaluator : IStrategyEvaluator
{
    public StrategyType Type => StrategyType.PatternScalping;

    private readonly IEnumerable<IPatternDetector> _detectors;
    private readonly ILogger<PatternScalpingEvaluator> _logger;

    public PatternScalpingEvaluator(
        IEnumerable<IPatternDetector> detectors,
        ILogger<PatternScalpingEvaluator> logger)
    {
        _detectors = detectors;
        _logger = logger;
    }

    public Task<StrategyEvaluation?> EvaluateAsync(
        long strategyId, string parametersJson, string symbol,
        IReadOnlyList<Kline> closedBars, CancellationToken ct)
    {
        var p = EvaluatorParameterHelper.TryParse<Parameters>(parametersJson) ?? new Parameters();

        // 1. Tum detector'lari calistir; null-result'lari ele.
        var detections = _detectors
            .Select(d => d.Detect(closedBars))
            .Where(r => r is not null)
            .Select(r => r!)
            .ToList();

        if (detections.Count == 0)
        {
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        // 2. Yon-bazli weighted vote.
        decimal longScore = 0m, shortScore = 0m;
        foreach (var det in detections)
        {
            var weight = _detectors.First(d => d.Type == det.Type).Weight;
            var contribution = weight * det.Confidence;
            if (det.Direction == PatternDirection.Long)
                longScore += contribution;
            else
                shortScore += contribution;
        }

        // 3. Threshold check (parametre ile override edilebilir).
        var winnerScore = Math.Max(longScore, shortScore);
        if (winnerScore < p.EntryThreshold)
        {
            _logger.LogDebug("PatternScalping: weak signal score={Score} threshold={Threshold}",
                winnerScore, p.EntryThreshold);
            return Task.FromResult<StrategyEvaluation?>(null);
        }

        var winnerDir = longScore > shortScore ? PatternDirection.Long : PatternDirection.Short;

        // 4. En guclu (confidence×weight) detection'in stop/TP'sini kullan.
        var leader = detections
            .Where(d => d.Direction == winnerDir)
            .OrderByDescending(d => _detectors.First(x => x.Type == d.Type).Weight * d.Confidence)
            .First();

        // 5. Strategy parametre tarafindan symbol-specific tweaks.
        var orderSize = p.OrderSize;  // sizing handler tarafindan override (ADR-0011)

        var direction = winnerDir == PatternDirection.Long
            ? StrategySignalDirection.Long
            : StrategySignalDirection.Short;

        var ctx = EvaluatorParameterHelper.SerializeContext(new
        {
            type = "patternscalping",
            leaderPattern = leader.Type.ToString(),
            leaderConfidence = leader.Confidence,
            longScore,
            shortScore,
            detectionCount = detections.Count,
            allPatterns = detections.Select(d => new { d.Type, d.Direction, d.Confidence }),
            maxHoldBars = leader.MaxHoldBars,
        });

        return Task.FromResult<StrategyEvaluation?>(new StrategyEvaluation(
            direction,
            orderSize,
            leader.EntryPrice,
            leader.StopPrice,
            ctx,
            SuggestedTakeProfit: leader.TakeProfit));
    }

    private sealed class Parameters
    {
        public decimal EntryThreshold { get; set; } = 0.55m;
        public decimal StrongThreshold { get; set; } = 0.75m;
        public decimal OrderSize { get; set; } = 0.001m;
        // Sembol-spesifik filtre tunable'lari (research B):
        public decimal VolumeMultiplier { get; set; } = 1.5m;
        public int LookbackBars { get; set; } = 20;
    }
}
```

**Karar — `MaxHoldBars` evaluator'dan handler'a nasil gidiyor:** `StrategyEvaluation.ContextJson` icine `maxHoldBars` yazilir. `StrategySignalToOrderHandler` (Infrastructure, fan-out) bunu deserialize eder ve `PlaceOrderCommand`'a gecirir; `OrderFilledPositionHandler` `Position.Open(..., maxHoldBars: ...)` cagirir → `Position.MaxHoldDuration` set olur (`§14.5`).

**Reddedilen Alternatif — `StrategyEvaluation` record'a `MaxHoldBars` field'i ekle.** Reddedildi: record'un genel arayuzunu kirletir, sadece pattern stratejisi kullanir; `ContextJson` zaten opaque metadata kovasi (mevcut akiş `type=trend/meanrev/grid` zaten oradan okuyor) — Loop 16 icinde basit cozum. Loop 17 adaptive learning'de tum context structured tip alabilir, o zaman record'a tasimak isteğe bagli.

**Reddedilen Alternatif — sum-of-confidence (weight'siz) vote.** Reddedildi: tum pattern'lar esit kucuk olur (weight'siz Engulfing == DoubleBottom). Bulkowski WR tabanli weight ayrımi edge'in temeli.

### 14.5 Time Stop — `Position.MaxHoldDuration` + `StopLossMonitorService` Genislemesi

**Karar:** Yeni Domain field, ayri service degil. Mevcut `StopLossMonitorService` (30s tick, ADR-0012 §12.3) genisler.

**Domain degisikligi:**

```csharp
// Position.cs
public TimeSpan? MaxHoldDuration { get; private set; }   // yeni

public static Position Open(
    Symbol symbol, PositionSide side, decimal quantity,
    decimal entryPrice, decimal? stopPrice,
    long? strategyId, TradingMode mode, DateTimeOffset now,
    decimal? takeProfit = null,
    TimeSpan? maxHoldDuration = null)              // yeni nullable parametre
```

`Position.OpenedAt` zaten var; `now - OpenedAt > MaxHoldDuration` true ise time stop tetiklenir.

**`StopLossMonitorService` icine ek tick logic:**

```csharp
foreach (pos in openPositions):
    var ageElapsed = clock.UtcNow - pos.OpenedAt;
    if (pos.MaxHoldDuration is TimeSpan dur && ageElapsed > dur)
    {
        await mediator.Send(new CloseSignalPositionCommand(
            pos.Symbol.Value, pos.StrategyId, pos.Mode,
            $"time_stop_max_hold_{(int)ageElapsed.TotalMinutes}min",
            $"timestop-{pos.Id}-{clock.UtcNow.ToUnixTimeSeconds()}"), ct);
        continue;  // stop check'i bypass et — kapaniyor zaten
    }
    // ... mevcut StopPrice check
```

**Migration:** `Positions.MaxHoldDurationSeconds` (long? NULL) — `TimeSpan` direkt EF map'lenmiyor, saniye olarak yazilir, getter `TimeSpan.FromSeconds(...)` ile hesaplanir. Mevcut row'lar NULL → time stop calismaz (geriye donuk uyumlu). Pattern stratejisi pattern'in `MaxHoldBars`'ini sembolun bar suresine carpar (BinanceBot 1m bar → `MaxHoldBars * 60s`); evaluator emit anında handler `Position.Open(..., maxHoldDuration: TimeSpan.FromMinutes(maxHoldBars))` ile gecirir.

**Reddedilen Alternatif A — yeni `TimeStopMonitorService`.** Reddedildi: ayni 30s tick, ayni open-pozisyon-tarama, ayni `CloseSignalPositionCommand`; iki BackgroundService kod tekrarı. Tek service'in icine if-block eklemek 5 satir.

**Reddedilen Alternatif B — `Position.MaxHoldUntil` mutlak DateTimeOffset.** Reddedildi: time-zone testleri zorlasir, yeniden start sonra test verisi non-deterministik. `TimeSpan + OpenedAt` deterministik.

**Reddedilen Alternatif C — `Position.IsTimeStopBreached(now)` domain method.** Cazip ama domain'in clock dependency'si var; saf olusturmak icin `now` parametresi gecirilmeli — yapildi (zaten Position.Open `now` aliyor); method eklemek isteğe bagli, monitor service `now - OpenedAt > MaxHoldDuration` direkt yazabilir cunku iki field public okunuyor. **YAGNI'ye direnis ıcın method eklemiyoruz.**

### 14.6 Strategy Seed Reform

**Karar:** `appsettings.json` `Strategies.Seed` array'i tamamen reset edilir:

```json
"Strategies": {
  "Seed": [
    {
      "Name": "BTC-Pattern-Scalper",
      "Type": "PatternScalping",
      "Symbols": [ "BTCUSDT" ],
      "ParametersJson": "{\"EntryThreshold\":0.55,\"StrongThreshold\":0.75,\"OrderSize\":0.001,\"VolumeMultiplier\":1.5,\"LookbackBars\":20}",
      "Activate": true
    },
    {
      "Name": "BNB-Pattern-Scalper",
      "Type": "PatternScalping",
      "Symbols": [ "BNBUSDT" ],
      "ParametersJson": "{\"EntryThreshold\":0.55,\"StrongThreshold\":0.75,\"OrderSize\":0.01,\"VolumeMultiplier\":1.5,\"LookbackBars\":20}",
      "Activate": true
    },
    {
      "Name": "XRP-Pattern-Scalper",
      "Type": "PatternScalping",
      "Symbols": [ "XRPUSDT" ],
      "ParametersJson": "{\"EntryThreshold\":0.60,\"StrongThreshold\":0.80,\"OrderSize\":5,\"VolumeMultiplier\":1.8,\"LookbackBars\":20}",
      "Activate": true
    }
  ]
}
```

**Sembol-spesifik tuning:**
- BTC/BNB: `EntryThreshold=0.55` (yuksek likidite, pattern saygin).
- XRP: `EntryThreshold=0.60`, `VolumeMultiplier=1.8` (research §F: "WR `%5-10` asagi revize et"). Daha agresif filtre, daha az ama daha kaliteli sinyal.

`OrderSize` ADR-0011 sizing service ile override edildigi icin sembolik (yine de seed'de tutuluyor, fan-out fallback icin). `LookbackBars` detector'lara akmaz (her detector kendi history'sini bilir); pattern context filtreleme (volume avg, EMA20) icin reservasyon — Loop 17'de detector'lar bu parametreyi okuyabilir.

**Seeder davranisi:** `StrategyConfigSeeder` Loop 5'te "active strateji ParametersJson skip" politikasini koruyor (ADR-0012 §12.2 not). Loop 16'da migration `DELETE FROM Strategies` calistirdigi icin tum 3 row yeniden insert edilir — bypass garantili.

### 14.7 Detector DI Registration

**Konum:** `src/Infrastructure/DependencyInjection.cs`

Mevcut 3 satir (`GridEvaluator`/`TrendFollowingEvaluator`/`MeanReversionEvaluator`) silinir. Yeni:

```csharp
// 14 pattern detector — sira Weight inisli (debug log siralamasi)
services.AddSingleton<IPatternDetector, DoubleBottomDetector>();
services.AddSingleton<IPatternDetector, DoubleTopDetector>();
services.AddSingleton<IPatternDetector, ThreeWhiteSoldiersDetector>();
services.AddSingleton<IPatternDetector, ThreeBlackCrowsDetector>();
services.AddSingleton<IPatternDetector, MorningStarDetector>();
services.AddSingleton<IPatternDetector, EveningStarDetector>();
services.AddSingleton<IPatternDetector, BullFlagDetector>();
services.AddSingleton<IPatternDetector, BearFlagDetector>();
services.AddSingleton<IPatternDetector, AscendingTriangleDetector>();
services.AddSingleton<IPatternDetector, DescendingTriangleDetector>();
services.AddSingleton<IPatternDetector, HammerDetector>();
services.AddSingleton<IPatternDetector, ShootingStarDetector>();
services.AddSingleton<IPatternDetector, BullishEngulfingDetector>();
services.AddSingleton<IPatternDetector, BearishEngulfingDetector>();

services.AddSingleton<IStrategyEvaluator, PatternScalpingEvaluator>();
services.AddSingleton<StrategyEvaluatorRegistry>();
```

`IEnumerable<IPatternDetector>` `PatternScalpingEvaluator` ctor'a otomatik enjekte olur (DI default behavior).

### 14.8 NuGet Dependencies — Onayli

**Karar:** Iki paket eklenir, ikisi de `src/Infrastructure/BinanceBot.Infrastructure.csproj`:

```xml
<PackageReference Include="OHLC_Candlestick_Patterns" Version="<latest>" />
<PackageReference Include="Skender.Stock.Indicators" Version="<latest>" />
```

`Skender.Stock.Indicators` `Application` projesinde de gerekli olabilir (`Indicators.cs` helper'i Loop 12'de `Infrastructure/Strategies/Evaluators/Indicators.cs` icindeydi — saf C#, native dep yok). Karar: **Infrastructure'da kalsin** (helper class'in domain knowledge'i yok, yeni evaluator'lar da Infrastructure'da). Application icine sirkulasyon olmaz.

**Lisans kontrolu:** `OHLC_Candlestick_Patterns` MIT (`przemyslawbak/OHLC_Candlestick_Patterns` GitHub). `Skender.Stock.Indicators` Apache 2.0. Ikisi de ticari kullanima uygun.

**Reddedilen Alternatif — TA-Lib.NET wrapper.** Reddedildi: native DLL bagimliligi (Windows/Linux ayri build), CI/CD karmasi; saf .NET cozumu varken native gerekmez.

### 14.9 Test Stratejisi

**Karar:** Iki katmanli test:

#### 14.9.1 Detector Unit Tests — `tests/Tests/Infrastructure/Strategies/Patterns/<Name>DetectorTests.cs`

Her detector icin minimum 4 senaryo:

| Senaryo | Beklenti |
|---|---|
| Pattern dogru sekilde olusmus + tum filtreler gectı | `PatternResult != null`, `Confidence ≥ 0.75` |
| Pattern olusmus + volume filtresi gecmedi | `null` (Engulfing) veya `Confidence < 0.55` (digerleri) |
| Pattern olusmus + RSI ekstrem (35) | Engulfing/Pinbar `null`; reversal pattern (DoubleBottom) PASS |
| Bar sayisi yetersiz (`< minBars`) | `null` |

Test datasi: `Kline.Create(...)` ile synthetic bar serisi. Helper `PatternTestBars.cs`:

```csharp
public static IReadOnlyList<Kline> BuildBullishEngulfing(int prefixBars = 30) {
    var bars = TrendingDown(prefixBars - 2);
    bars.Add(Kline.Create(... open=100, close=98, high=101, low=97, volume=normal));     // b1 bearish
    bars.Add(Kline.Create(... open=97, close=102, high=103, low=96.5, volume=high));      // b2 engulfing
    return bars;
}
```

#### 14.9.2 Evaluator Integration Tests — `tests/Tests/Infrastructure/Strategies/PatternScalpingEvaluatorTests.cs`

Mock detector'larla weighted vote dogrulamasi:

| Test | Mock Setup | Beklenti |
|---|---|---|
| `NoDetections_ReturnsNull` | tum detector `null` doner | `evaluation == null` |
| `SingleLongDetection_AboveThreshold_ReturnsLong` | DoubleBottom `Confidence=0.9` doner (`weight=0.85` × `0.9 = 0.765 > 0.55`) | `Direction=Long`, leader=DoubleBottom |
| `MixedSignals_LongScoreWins` | DoubleBottom Long `0.9` + BearishEngulfing Short `0.7` | `Long` (`0.85×0.9=0.765 > 0.55×0.7=0.385`) |
| `WeakSignal_BelowThreshold_ReturnsNull` | Hammer `Confidence=0.4` (`0.58×0.4=0.232 < 0.55`) | `null` |
| `ContextJson_IncludesAllPatterns` | 3 long detection | JSON'da `allPatterns.Length == 3` |

Detector mock'lari: `Mock<IPatternDetector>` (Moq) veya minimal stub class — Detector basit interface, stub kolay.

**Reddedilen Alternatif — gercek 1m Binance backtest verisi ile end-to-end test.** Reddedildi: deterministik degil, test suite yavaslar, CI flaky. Backtest harness Loop 17+ scope; unit + integration burada yeter.

### 14.10 Backward Compatibility — Position/Order/RiskProfile/Sizing/Exit Korunur

**Karar:** Loop 4-15 boyunca yapilan reformlarin **hicbiri silinmez veya degismez**:

| Aggregate / Service | Loop 16 etkisi |
|---|---|
| `Position` aggregate | `MaxHoldDuration` field eklenir (`§14.5`); `Open` parametresi optional. **Geriye uyumlu.** |
| `Position.StopPrice` (ADR-0012 §12.4) | Korunur; pattern detector `StopPrice` doner, evaluator forward eder. |
| `Position.TakeProfit` (Loop 10) | Korunur; pattern detector `TakeProfit` doner, evaluator forward eder. |
| `Order` aggregate | Degisiklik yok. |
| `RiskProfile` aggregate | Degisiklik yok; `RecordTradeOutcome` zincir (ADR-0011 §11.7) intact. |
| `IPositionSizingService` (ADR-0011 §11.1) | Degisiklik yok; `StrategySignalToOrderHandler` evaluator'in `OrderSize`'ını ignore eder, sizing service hesaplar. |
| `StopLossMonitorService` | Genisler (`§14.5`); SL akisi degismez, time-stop kolu eklenir. |
| `TakeProfitMonitorService` (Loop 10) | Degisiklik yok; `Position.TakeProfit` ayni semantik. |
| `OrderFilledPositionHandler` | `Position.Open` cagrisina `maxHoldDuration` parametresi eklenir; `ContextJson`'dan `maxHoldBars` deserialize edilip `TimeSpan.FromMinutes(maxHoldBars)` olarak gecirilir. |
| `EquityPeakTrackerService` | Degisiklik yok. |
| `IBinanceMarketData.GetTicker24hAsync` (ADR-0012 §12.1) | Degisiklik yok. |
| `IStrategyEvaluator` arayuzu | Degisiklik yok; `PatternScalpingEvaluator` mevcut kontratin impl'i. |

**Sonuc:** Sizing/exit/risk reformu olduğu yerde kalir; sadece "sinyal nereden gelir" sorusunun cevabi degisir.

### 14.11 Volume + RSI Filter — Detector Icine, Evaluator'a Degil

**Karar:** Filtre **detector seviyesinde** uygulanir (`§14.3`'te aciklandigi gibi).

**Gerekce:**
- Her pattern'in **kendi filtre semasi** vardir: Engulfing volume zorunlu (research §B1 red flag); Hammer S/R proximity zorunlu; DoubleBottom volume breakout'ta zorunlu, dip oluşumunda gerekli degil. Ortak filtre evaluator level'de uygulanırsa pattern-spesifik nuance kaybolur.
- Detector pattern bilgisini iyi tanir (kendi algoritma sahibi); filtre ile algoritma arasinda kohezyon yuksek.
- Evaluator's responsibility: **toplama + voting**. Ek filtre evaluator'da SRP ihlali.
- Test edilebilirlik: detector kendi unit test'inde tum filtreleri sahip; evaluator integration test'i mock detector'larla votingı izole eder.

**Reddedilen Alternatif — evaluator level'de "pre-filter" (volume + trend EMA20 zorla)**. Reddedildi cunku DoubleBottom dip kismi cogu zaman volume'u dusuk; pre-filter onu eler ama dip pattern'inin core'u dusuk volume + breakout yuksek. Pattern-spesifik logic detector'da yasar.

### 14.12 Pattern Weights — Loop 16 Statik, Loop 17+ Adaptive

**Karar:** Loop 16'da `IPatternDetector.Weight` **sabit** (sınıf icinde literal); Bulkowski + chartpatternspro + altFINS + Luxalgo WR'lerine dayali (research §C1).

Loop 17+ adaptive learning backlog (`loops/loop_17/research-adaptive-weight.md` placeholder ileride):

- Thompson Sampling: her 50 trade sonrasi `wins/(wins+losses)` ile weight guncelle.
- Storage: yeni aggregate `PatternPerformance` (`Type, WinCount, LossCount, AvgConfidence, LastUpdated`) — DB persisted.
- `IPatternDetector.Weight` getter `IPatternStatsProvider`'dan okur (DI).
- A/B testing: `PatternScalpingEvaluator` `parametersJson` `WeightSource = "static" | "adaptive"`.

Loop 17 bunlari sahiplenir; **Loop 16 scope disinda**.

### 14.13 Cakisma Kontrolu — ADR-0006/0008/0009/0010/0011/0012/0013

| ADR | Cakisma | Karar |
|---|---|---|
| **ADR-0006 testnet-first** | `Binance.AllowMainnet=false`; pattern stratejisi mode-agnostik calisir, mainnet branch sizing oncesi skip | Uyumlu |
| **ADR-0008 §8.3 fan-out per mode** | `StrategySignalEmittedEvent` 3 mode icin fan-out; `PatternScalpingEvaluator` mode bilmiyor (signal layer) | Uyumlu |
| **ADR-0008 §8.6 mode-scoped RiskProfile** | Pattern signal her mode icin sizing service uzerinden ayri risk hesaplar | Uyumlu |
| **ADR-0009 REST kline backfill** | Detector minimum 22 bar gerektirir (RSI 14 + volume avg 20); 1000 bar backfill rahat karsilar | Uyumlu |
| **ADR-0010 backfill event suppression** | Backfill suresince `KlineClosedEvent` susuyor → `StrategyEvaluationHandler` cagrilmiyor → pattern degerlendirme tetiklenmiyor | Uyumlu (zaten korunmali) |
| **ADR-0011 §11.1-11.5 sizing + evaluator separation** | `PatternScalpingEvaluator.OrderSize` ignore edilir; sizing service stop distance ile equity-aware hesaplar | Uyumlu, design's korunmasi |
| **ADR-0011 §11.6 OrderFilledPositionUpdater** | `Position.Open(..., maxHoldDuration: ...)` parametresi eklenir; mevcut handler genisler | Uyumlu |
| **ADR-0011 §11.12 server-side stop deferred** | Pattern stratejisi de client-side soft-stop kullanir; `StopLossMonitorService` sahip | Uyumlu |
| **ADR-0012 §12.1 Ticker24h REST** | UI ve market summary etkilenmez | Uyumlu |
| **ADR-0012 §12.2 XRP-Grid bandi** | `XRP-Grid` strateji silinir (`§14.6`); XRP yerine `XRP-Pattern-Scalper` | Replaced — ayrica ADR-0012 §12.2 partially superseded |
| **ADR-0012 §12.3 StopLossMonitor** | Genisler (`§14.5`); davranisi degismez sadece time-stop kolu eklenir | Uyumlu |
| **ADR-0012 §12.4 Position.StopPrice** | Korunur; pattern detector emit eder | Uyumlu |
| **ADR-0012 §12.5/§12.6/§12.7 Trend/MeanRev/Grid parametre tuning** | Stratejiler silinir → bu sectionlar **superseded** | Replaced |
| **ADR-0012 §12.8/§12.9 Paper realism** | Degisiklik yok; pattern stratejisi de paper'da ayni fill engine kullanir | Uyumlu |
| **ADR-0013 (varsa) server-side OCO** | Loop 16 oncesi yazilmadi; pattern stratejisi ayni soft-stop yolundan gider | Uyumlu (ileride OCO eklenince pattern de faydalanir) |

## Consequences

### Pozitif

- **Edge artisi.** Multi-pattern weighted vote + filtre kombinasyonu Tobi Lux (2024) sonucunu gecer; backtest yok ama Bulkowski + altFINS WR'leri `%55-85` araliginda — tek-pattern `~%50` random'a gore acik artis.
- **Acik-kapali prensibi.** Yeni pattern ekleme = yeni detector class + 1 satir DI. Mevcut hicbir kod dokunmaz (evaluator `IEnumerable<IPatternDetector>` topluyor).
- **Test edilebilirlik artar.** 14 detector × 4 senaryo = 56 unit test; evaluator 5 integration test. Loop 12-15'te 3 evaluator × parametre permutation testleri kaotikti — pattern detector'da pure-function clarity.
- **Strateji yuzeyi temiz.** 3 evaluator + 3 farkli parametre semasi → 1 evaluator + 1 standart parametre seti. Backend-dev bilissel yuk azalir.
- **Library destegi.** `OHLC_Candlestick_Patterns` 37+37 candle pattern out-of-box; gelecek pattern eklemek (Doji, Spinning Top vs.) adapter detector ile 1 saatlik is.
- **Crypto-aware.** Morning/Evening Star gap kontrolu kapali (research §F); sembol-spesifik volume threshold (XRP `1.8x`).
- **Backward compat tam.** Position/Order/RiskProfile/Sizing/Exit reform akıslari aynen calisir.

### Negatif / Tradeoff

- **Breaking DB change.** `Strategies` tablosu wipe, mevcut acik pozisyonlarin `StrategyId` NULL'a duser. Operasyonel adim: migration oncesi tum acik pozisyon manuel kapatilir. Paper/testnet'te kabul; mainnet bloklu.
- **NuGet bagimliligi.** `OHLC_Candlestick_Patterns` aktif maintain durumu kontrol edilmeli (son commit ne zaman?); abandonware riski varsa Loop 17'de fork. `Skender.Stock.Indicators` aktif (microsoft.com/MS open source).
- **14 detector class boilerplate.** Sprint 1'de 8 class + Sprint 2'de 6 class; her biri ~80-150 satir (filtre + confidence + test). Yine de SRP + acik-kapali değer ile orantilidir.
- **Custom detector implementations** (Flag/Triangle/DoubleBottom): NuGet bunlari icermiyor (sadece candlestick + Fibonacci pattern). Pivot detection algoritmasi (3-bar swing low/high) custom kod; `Skender` `PivotPoints` indikatoru var ama farkli semantik. Tahmini 200-300 satir custom.
- **Confidence scoring deterministik degil 100%.** Filtre `+0.10/+0.15` artislar empirik (Bulkowski WR'lerinden tahmin); Loop 17 adaptive learning ile kalibrasyon — Loop 16'da kabul edilen risk.
- **Time stop deterministik ama agressif.** 5-bar Engulfing / 7-bar Star / 10-bar Flag bazi pattern'larin tam realize olmasini engelleyebilir. Pattern bazli farkli sure (research §D4), evaluator pass eder, monitor uygular — yine de bilincli kompromi: scalping 5-10 dk kuralı.
- **Pattern detector'lari `Detect(IReadOnlyList<Kline>)` sync.** Eger gelecekte detector'lar external API (sentiment service vs.) cagirirsa async signature gerekir. Loop 16 scope'unda gerek yok.
- **`StrategyEvaluation.ContextJson` `maxHoldBars` opaque.** Handler `JsonDocument.Parse` ile cikarmak zorunda; magic string. Loop 17'de `StrategyEvaluation` record'a typed field olarak terfi.

### Notr

- Domain event sayisi degismez (`StrategySignalEmittedEvent`, `PositionOpenedEvent`, `PositionClosedEvent`).
- Yeni MediatR command/query yok.
- Migration tek (`Loop16PatternScalpingReset`); ek complexity yok.
- Fan-out davranisi degismez (`StrategySignalToOrderHandler`).
- Logging contracts korunur (`agent-bus.append_decision` gerek karari icin).

## Alternatifler (uzun)

### Alt-1: Evaluator-per-pattern (14 evaluator)

Her pattern ayri `IStrategyEvaluator` impl. Reddedildi cunku `StrategyType` enum 14 deger olur, seed array sismeleri (`BTC-DoubleBottom` + `BTC-Engulfing` + ... = 42 seed); fan-out handler ayni sembolde 14 strateji icin tetiklenir; sizing/risk fan-out kac kat artar; UI strateji listesi okunmaz. Weighted vote tek evaluator icinde dogal — alternatif elenir.

### Alt-2: Pattern Detection Domain Service

`IPatternDetector` domain'a tasi (Domain Service). Reddedildi: NuGet (OHLC_Candlestick_Patterns + Skender) Infrastructure detayi; Domain saf C# + BCL kuralı (CLAUDE.md §src/CLAUDE.md). Adapter pattern ile sarmal yapip Application interface'i Domain'a tasimak mumkun ama yarari yok — pattern detection use-case'i (Application servisi) dogal yer.

### Alt-3: Kademeli geçiş — `StrategyType.PatternScalping = 4` ekle, eski 3'u tut

Reddedildi (`§14.1`'de ayrintili). Olu strateji DB'de durmaz, evaluator yok hatasi sansurlenmez.

### Alt-4: Tek pattern + sentiment AI hibrit

LLM destegi ile pattern + ozet ileri analiz. Reddedildi: scope patlama, kaynaklara dis bagimlilik, latency `200-500ms` per signal. Loop 20+ icin reservasyon.

### Alt-5: TA-Lib.NET wrapper kullanimi

Native DLL; CI/CD karmasi. Reddedildi (`§14.8`).

### Alt-6: Pattern weights detector property yerine config

`appsettings.json` `PatternWeights` section. Reddedildi: weight semantik, pattern algoritmasinin parcasi (Bulkowski WR-based); operatorel runtime change manipulasyon riski; Loop 17 adaptive learning DB'de tutar (config degil, learning state). Loop 16 statik C# literal en temiz.

### Alt-7: Pattern detection caching (1 dk TTL)

Reddedildi: `KlineClosedEvent` per-bar tetiklenir (1 dk = 1 evaluation), cache miss her zaman; YAGNI.

## Kaynak

- [ADR-0006 Testnet-First Policy](./0006-testnet-first-policy.md)
- [ADR-0008 Trading Modes Fan-Out](./0008-trading-modes.md) §8.3 §8.6
- [ADR-0009 REST Kline Backfill On Boot](./0009-rest-kline-backfill-on-boot.md)
- [ADR-0010 Backfill Event Suppression](./0010-backfill-event-suppression.md)
- [ADR-0011 Equity-Aware Sizing & Risk Tracking](./0011-equity-aware-sizing-and-risk-tracking.md) §11.1 §11.5 §11.6 §11.12
- [ADR-0012 Trade Frequency Reform + 24h Ticker + Stop-Loss Monitor](./0012-trade-frequency-realism-reform.md) §12.3 §12.4 (kismi superseded: §12.5/§12.6/§12.7)
- [loops/loop_16/research-pattern-scalping.md](../../loops/loop_16/research-pattern-scalping.md) — binance-expert AR-GE 401 satir
- [loops/loop_16/decision-pattern-reform.md](../../loops/loop_16/decision-pattern-reform.md) — operasyonel commit-by-commit (backend-dev sablonu)
- [Bulkowski thepatternsite — CandlePerformers](https://www.thepatternsite.com/CandlePerformers.html)
- [chartpatternspro — High Win Rate Chart Patterns](https://chartpatternspro.com/high-win-rate-chart-patterns/)
- [altFINS — Crypto Chart Patterns](https://altfins.com/knowledge-base/chart-patterns/)
- [Luxalgo — Candle Formations Every Scalper Should Know](https://www.luxalgo.com/blog/candle-formations-every-scalper-should-know/)
- [Tobi Lux 2024 — Predictive Power of Candlestick Patterns](https://medium.com/@Tobi_Lux/on-predictive-power-of-candlestick-patterns-in-stock-trading-an-experiment-d71dd92b4b27)
- [OHLC_Candlestick_Patterns NuGet](https://github.com/przemyslawbak/OHLC_Candlestick_Patterns) (MIT)
- [Skender.Stock.Indicators NuGet](https://github.com/DaveSkender/Stock.Indicators) (Apache 2.0)
- [Eric Evans — Domain-Driven Design ch. 5 Aggregate state, ch. 6 Domain Service](https://www.dddcommunity.org/learning-ddd/what_is_ddd/)
- [Microsoft Learn — DDD-CQRS application service pattern](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/)
- [jasontaylordev/CleanArchitecture — handler-per-action](https://github.com/jasontaylordev/CleanArchitecture)
- [ardalis/CleanArchitecture — Open/Closed for evaluator family](https://github.com/ardalis/CleanArchitecture)
