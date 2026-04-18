# Loop 16 — Pattern-Based Scalping Reform: Operasyonel Karar (backend-dev sablonu)

**Tarih:** 2026-04-17 | **Yazar:** architect | **Normatif kaynak:** [docs/adr/0014-pattern-based-scalping-reform.md](../../docs/adr/0014-pattern-based-scalping-reform.md) | **AR-GE:** [research-pattern-scalping.md](./research-pattern-scalping.md)

> Bu dokuman backend-dev'in **tek seferde** Loop 16 reformunu commit'lemesi icin commit-by-commit sablondur. Karar gerekceleri ADR-0014'tedir; burada sadece "ne, nereye, nasil" var.

---

## Commit Sirasi (10 commit)

```
C1  chore: add OHLC_Candlestick_Patterns + Skender.Stock.Indicators NuGet refs
C2  feat(domain): Position.MaxHoldDuration field + Open() optional param
C3  feat(domain): StrategyType enum reset → PatternScalping = 1 only
C4  migration: Loop16PatternScalpingReset (DELETE Strategies + Position.MaxHoldDuration column)
C5  feat(application): IPatternDetector + PatternResult + PatternType + PatternDirection
C6  feat(infra): 8 sprint-1 detectors (DoubleBottom/Top, ThreeSoldiers/Crows, Morning/EveningStar, Bull/BearFlag)
C7  feat(infra): 6 sprint-2 detectors (Asc/DescTriangle, Hammer/ShootingStar, Bull/BearishEngulfing)
C8  feat(infra): PatternScalpingEvaluator + DI registration + delete 3 old evaluators
C9  feat(infra): StopLossMonitorService time-stop branch + OrderFilledPositionHandler maxHoldBars wiring
C10 chore(config): appsettings.json strategy seed reset + tests (detector unit + evaluator integration)
```

---

## C1 — NuGet Dependencies

**Dosya:** `src/Infrastructure/BinanceBot.Infrastructure.csproj`

```xml
<ItemGroup>
  <!-- Loop 16 ADR-0014: pattern detection -->
  <PackageReference Include="OHLC_Candlestick_Patterns" Version="*" />
  <PackageReference Include="Skender.Stock.Indicators" Version="*" />
</ItemGroup>
```

**Komut:**
```bash
dotnet add src/Infrastructure package OHLC_Candlestick_Patterns
dotnet add src/Infrastructure package Skender.Stock.Indicators
dotnet build
```

**Acceptance:** `dotnet build` clean; iki paket `bin/Debug/net10.0` icine `cp` edilmis.

---

## C2 — Position.MaxHoldDuration

**Dosya:** `src/Domain/Positions/Position.cs`

Yeni property (StopPrice/TakeProfit yaninda, line ~33):

```csharp
/// <summary>
/// Optional pattern-based time stop (ADR-0014 §14.5). When set,
/// <see cref="Infrastructure.Trading.StopLossMonitorService"/> dispatches a
/// <c>CloseSignalPositionCommand</c> when <c>Now - OpenedAt &gt; MaxHoldDuration</c>.
/// Persisted as seconds (long?) in DB; null = no time stop.
/// </summary>
public TimeSpan? MaxHoldDuration { get; private set; }
```

`Open(...)` factory imza guncellenir (mevcut sırali parametreleri bozmamak icin sona ekle):

```csharp
public static Position Open(
    Symbol symbol,
    PositionSide side,
    decimal quantity,
    decimal entryPrice,
    decimal? stopPrice,
    long? strategyId,
    TradingMode mode,
    DateTimeOffset now,
    decimal? takeProfit = null,
    TimeSpan? maxHoldDuration = null)              // YENI
{
    // ... mevcut validasyon ...
    if (maxHoldDuration is TimeSpan d && d <= TimeSpan.Zero)
    {
        throw new DomainException("Max hold duration must be positive when set.");
    }

    var position = new Position
    {
        // ... mevcut field set ...
        MaxHoldDuration = maxHoldDuration,
    };
    // ... mevcut sonu ...
}
```

**Configuration:** `src/Infrastructure/Persistence/Configurations/PositionConfiguration.cs` (asagi yukari `StrategyConfiguration` ile ayni dosya yapisi):

```csharp
builder.Property(p => p.MaxHoldDuration)
    .HasColumnName("MaxHoldDurationSeconds")
    .HasConversion(
        v => v.HasValue ? (long?)(long)v.Value.TotalSeconds : null,
        v => v.HasValue ? (TimeSpan?)TimeSpan.FromSeconds(v.Value) : null);
```

**Acceptance:** Domain testleri pass; `Position.Open(... maxHoldDuration: TimeSpan.FromMinutes(10))` dogrulanmis.

---

## C3 — StrategyType Enum Reset

**Dosya:** `src/Domain/Strategies/StrategyEnums.cs`

```csharp
namespace BinanceBot.Domain.Strategies;

public enum StrategyStatus
{
    Draft = 1,
    Paused = 2,
    Active = 3,
}

public enum StrategyType
{
    PatternScalping = 1,
}

public enum StrategySignalDirection
{
    Long = 1,
    Short = 2,
    Exit = 3,
}
```

Eski `Grid=1, TrendFollowing=2, MeanReversion=3` **silinir.** `PatternScalping` numara `1`'i tekrar kullanir (DB row'lari migration ile temizleniyor; conflict yok).

**Etki:** Solution'da `StrategyType.Grid` / `.TrendFollowing` / `.MeanReversion` referanslari compile error verir → C8'de evaluator class'lari silindiginde temizlenir. Test class'lari (`TrendFollowingEvaluatorTests`, `EvaluatorTakeProfitTests` vb.) C8'de silinir.

---

## C4 — Migration

**Komut:**
```bash
dotnet ef migrations add Loop16PatternScalpingReset --project src/Infrastructure --startup-project src/Api
```

**Manuel duzeltme — `Up()` icine ekle:**

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    // 1. EF auto-generated kolon ekleme:
    migrationBuilder.AddColumn<long>(
        name: "MaxHoldDurationSeconds",
        table: "Positions",
        type: "bigint",
        nullable: true);

    // 2. Strateji wipe (ADR-0014 §14.1) — eski enum degerleri uyumsuz, seeder yeni 3 row basacak.
    migrationBuilder.Sql("DELETE FROM StrategySignals;");
    migrationBuilder.Sql("UPDATE Positions SET StrategyId = NULL;");
    migrationBuilder.Sql("DELETE FROM Strategies;");
}

protected override void Down(MigrationBuilder migrationBuilder)
{
    migrationBuilder.DropColumn(name: "MaxHoldDurationSeconds", table: "Positions");
    // Stratejiler restore edilemez — Down() sirasinda manuel reseed gerekir.
}
```

**Acceptance:** `dotnet ef database update --project src/Infrastructure --startup-project src/Api` clean; `Strategies` tablosu bos; `Positions.MaxHoldDurationSeconds` kolonu mevcut.

---

## C5 — IPatternDetector + Domain Tipler

**Dosya:** `src/Application/Strategies/Patterns/IPatternDetector.cs`

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

public enum PatternDirection { Long = 1, Short = 2 }

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

---

## C6 — Sprint 1 Detectors (8 adet)

**Konum:** `src/Infrastructure/Strategies/Patterns/Detectors/`

### Ortak helper — `src/Infrastructure/Strategies/Patterns/PatternFilters.cs`

```csharp
namespace BinanceBot.Infrastructure.Strategies.Patterns;

using BinanceBot.Domain.MarketData;
using BinanceBot.Infrastructure.Strategies.Evaluators;

internal static class PatternFilters
{
    public static decimal AverageVolume(IReadOnlyList<Kline> bars, int lookback)
    {
        if (bars.Count < lookback) return 0m;
        decimal sum = 0m;
        for (int i = bars.Count - lookback; i < bars.Count; i++) sum += bars[i].Volume;
        return sum / lookback;
    }

    public static bool VolumeConfirmed(Kline bar, decimal avg, decimal multiplier)
        => avg > 0m && bar.Volume >= avg * multiplier;

    public static bool RsiInBand(IReadOnlyList<Kline> bars, int period, decimal min, decimal max)
    {
        if (bars.Count < period + 2) return false;
        var rsi = Indicators.Rsi(bars, period);
        return rsi >= min && rsi <= max;
    }

    public static bool TrendUp(IReadOnlyList<Kline> bars, int emaPeriod)
        => bars[^1].ClosePrice > Indicators.Ema(bars, emaPeriod, bars.Count - 1);

    public static decimal Clamp01(decimal v) => v < 0m ? 0m : (v > 1m ? 1m : v);
}
```

### Ornek — `BullishEngulfingDetector.cs`

```csharp
namespace BinanceBot.Infrastructure.Strategies.Patterns.Detectors;

using BinanceBot.Application.Strategies.Patterns;
using BinanceBot.Domain.MarketData;
using BinanceBot.Infrastructure.Strategies.Evaluators;
using System.Text.Json;

public sealed class BullishEngulfingDetector : IPatternDetector
{
    public PatternType Type => PatternType.BullishEngulfing;
    public decimal Weight => 0.55m;

    public PatternResult? Detect(IReadOnlyList<Kline> bars)
    {
        if (bars.Count < 22) return null;
        var b1 = bars[^2];
        var b2 = bars[^1];

        var bearishB1 = b1.ClosePrice < b1.OpenPrice;
        var bullishB2 = b2.ClosePrice > b2.OpenPrice;
        var engulfs = b2.OpenPrice <= b1.ClosePrice && b2.ClosePrice >= b1.OpenPrice;
        if (!bearishB1 || !bullishB2 || !engulfs) return null;

        // Volume mecburi (Bulkowski 10gun follow-through negative — ADR-0014 §14.3 red-flag).
        var avgVol = PatternFilters.AverageVolume(bars, 20);
        if (!PatternFilters.VolumeConfirmed(b2, avgVol, 1.5m)) return null;

        // RSI 40-60 reversal'a optimal (whipsaw eler).
        if (!PatternFilters.RsiInBand(bars, 14, 40m, 60m)) return null;

        var trendUp = PatternFilters.TrendUp(bars, 20);
        var conf = PatternFilters.Clamp01(0.50m
            + (trendUp ? 0.15m : 0m)
            + 0.10m   // volume passed
            + 0.10m); // rsi band passed

        var entry = b2.ClosePrice;
        var stop = b1.LowPrice - 0.0001m;
        var risk = entry - stop;
        var tp = entry + risk * 1.5m;

        var ctx = JsonSerializer.Serialize(new
        {
            type = "bullish_engulfing",
            b1Close = b1.ClosePrice,
            b2Close = b2.ClosePrice,
            volume = b2.Volume,
            avgVolume = avgVol,
            trendUp,
            confidence = conf,
        });

        return new PatternResult(
            PatternType.BullishEngulfing,
            PatternDirection.Long,
            conf,
            entry,
            stop,
            tp,
            MaxHoldBars: 5,
            ctx);
    }
}
```

### Diger 7 detector — sablonun ozeti

| Detector | Algorithm Hint | Weight | MaxHoldBars |
|---|---|---|---|
| `DoubleBottomDetector` | son 15 bar icinde 2 pivot-low (`bars[^k].LowPrice` `k=±2 swing`), ikisi `±%0.5` esit, neckline = aralarinin highest, breakout = `bars[^1].Close > neckline`, vol spike | 0.85 | 10 |
| `DoubleTopDetector` | yukarinin mirror'u | 0.85 | 10 |
| `ThreeWhiteSoldiersDetector` | OHLC_Candlestick_Patterns NuGet `IsThreeWhiteSoldiers` true; 3 ardisik bullish, her biri oncekinin govdesi icinde aciliyor; vol >= avg | 0.78 | 5 |
| `ThreeBlackCrowsDetector` | mirror | 0.78 | 5 |
| `MorningStarDetector` | OHLC NuGet `IsMorningStar`, **gap kontrolu kapali** (cyrpto 24/7) — manuel: b1 bearish, b2 small body (`<%30`), b3 bullish; b3.Close > b1 mid | 0.67 | 7 |
| `EveningStarDetector` | mirror | 0.67 | 7 |
| `BullFlagDetector` | flagpole = `bars[^9..^5]` net %1+ artis; konsolidasyon = `bars[^4..^1]` slope dusuk + retracement `<%50`; breakout = `bars[^1].Close > bars[^4..^1].Max(High)`; vol spike | 0.64 | 10 |
| `BearFlagDetector` | mirror | 0.64 | 10 |

**OHLC NuGet kullanim ornegi** (ThreeWhiteSoldiers):

```csharp
using OHLC_Candlestick_Patterns;
// ...
var ohlcList = bars.TakeLast(5).Select(b => new Ohlcv {
    Open = (double)b.OpenPrice, High = (double)b.HighPrice,
    Low = (double)b.LowPrice, Close = (double)b.ClosePrice,
    Volume = (double)b.Volume, Date = b.OpenTime.UtcDateTime
}).ToList();

var pattern = new CandlestickPatternRecognizer(ohlcList);
if (!pattern.IsThreeWhiteSoldiers()) return null;
// ... filter + confidence + entry/stop/tp ...
```

> **NOT**: `OHLC_Candlestick_Patterns` API ismi guncel olabilir; backend-dev kontrol edip sınıf adini guncellesin.

---

## C7 — Sprint 2 Detectors (6 adet)

| Detector | Algorithm Hint | Weight | MaxHoldBars |
|---|---|---|---|
| `AscendingTriangleDetector` | `bars[^15..]` icinde flat resistance (3+ touch `±%0.3`), yukseklen support trendline; breakout `bars[^1].Close > resistance` + vol spike | 0.62 | 10 |
| `DescendingTriangleDetector` | mirror | 0.62 | 10 |
| `HammerDetector` | OHLC NuGet `IsHammer` veya manuel: alt wick body'den 2x+, ust wick `<%10`, body altta, RSI<40 (oversold) ve S/R yakinligi (`±%0.5`) | 0.58 | 5 |
| `ShootingStarDetector` | mirror (RSI>60 + ust wick) | 0.58 | 5 |
| `BullishEngulfingDetector` | (C6'da ornek) | 0.55 | 5 |
| `BearishEngulfingDetector` | mirror | 0.55 | 5 |

---

## C8 — PatternScalpingEvaluator + DI + Eski Sil

### Yeni evaluator

**Dosya:** `src/Infrastructure/Strategies/Evaluators/PatternScalpingEvaluator.cs`

(ADR-0014 §14.4'teki tam class — kopyala.)

### DI registration

**Dosya:** `src/Infrastructure/DependencyInjection.cs` (mevcut satir ~146-149)

Sil:
```csharp
services.AddSingleton<IStrategyEvaluator, GridEvaluator>();
services.AddSingleton<IStrategyEvaluator, TrendFollowingEvaluator>();
services.AddSingleton<IStrategyEvaluator, MeanReversionEvaluator>();
```

Yerine ekle:
```csharp
// Loop 16 ADR-0014: 14 pattern detector — weight inisli sira (debug log).
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

### Eski dosyalari sil

```
src/Infrastructure/Strategies/Evaluators/GridEvaluator.cs
src/Infrastructure/Strategies/Evaluators/TrendFollowingEvaluator.cs
src/Infrastructure/Strategies/Evaluators/MeanReversionEvaluator.cs

tests/Tests/Infrastructure/Strategies/TrendFollowingEvaluatorTests.cs
tests/Tests/Infrastructure/Strategies/MeanReversionEvaluatorTests.cs    (varsa)
tests/Tests/Infrastructure/Strategies/GridEvaluatorTests.cs              (varsa)
tests/Tests/Infrastructure/Strategies/EvaluatorTakeProfitTests.cs        (eski 3 evaluator referansi tasiyabilir; reform et veya sil)
```

`Indicators.cs` (`src/Infrastructure/Strategies/Evaluators/Indicators.cs`) **KORU** — pattern filters bunu kullaniyor.

---

## C9 — StopLossMonitorService Genislemesi + OrderFilledPositionHandler

### `StopLossMonitorService` time-stop branch

**Dosya:** `src/Infrastructure/Trading/StopLossMonitorService.cs`

Mevcut `foreach (pos in openPositions)` icine, **stop check'inden ONCE** ekle:

```csharp
foreach (var pos in openPositions)
{
    // ADR-0014 §14.5: time-stop dali (pattern-based scalping max hold).
    if (pos.MaxHoldDuration is TimeSpan dur)
    {
        var ageElapsed = clock.UtcNow - pos.OpenedAt;
        if (ageElapsed > dur)
        {
            var cidPrefix = $"timestop-{pos.Id}-{clock.UtcNow.ToUnixTimeSeconds()}";
            await mediator.Send(new CloseSignalPositionCommand(
                pos.Symbol.Value, pos.StrategyId, pos.Mode,
                $"time_stop_max_hold_{(int)ageElapsed.TotalMinutes}min",
                cidPrefix), ct);
            _logger.LogInformation("TimeStop triggered pos={PosId} age={AgeMin}min max={MaxMin}min",
                pos.Id, (int)ageElapsed.TotalMinutes, (int)dur.TotalMinutes);
            continue;
        }
    }

    // ... mevcut StopPrice check (degismez) ...
}
```

`IClock` injection zaten varsa kullan; yoksa `DateTimeOffset.UtcNow` (test'i izole etmek icin `IClock` tercih).

### `OrderFilledPositionHandler` — maxHoldBars wiring

**Dosya:** `src/Infrastructure/Orders/OrderFilledPositionHandler.cs`

`Position.Open(...)` cagrisindan ONCE, `ContextJson`'dan `maxHoldBars` cikar:

```csharp
TimeSpan? maxHoldDuration = null;
if (!string.IsNullOrWhiteSpace(notification.ContextJson))
{
    try
    {
        using var doc = JsonDocument.Parse(notification.ContextJson);
        if (doc.RootElement.TryGetProperty("maxHoldBars", out var mhb)
            && mhb.TryGetInt32(out var bars) && bars > 0)
        {
            // BinanceBot 1m bar — 1 bar = 1 dakika.
            maxHoldDuration = TimeSpan.FromMinutes(bars);
        }
    }
    catch (JsonException)
    {
        // sessiz: pattern olmayan strateji ContextJson'larinda yok normal.
    }
}

var position = Position.Open(
    symbol, side, qty, fillPrice,
    stopPrice: order.StopPrice,
    strategyId: order.StrategyId,
    mode: order.Mode,
    now: clock.UtcNow,
    takeProfit: order.TakeProfit,
    maxHoldDuration: maxHoldDuration);
```

> Notification akisi: `OrderFilledEvent` → `Order` → ama `ContextJson` `Order`'da yok. Cozum: ya (a) `Order.ContextJson` field'i ekle ve `PlaceOrderCommand`'a tasi (Loop 11 paterni — sizing bilgisini Order'da tutuyoruz), ya da (b) handler `db.StrategySignals.OrderByDescending(s => s.CreatedAt).Where(s => s.Symbol == symbol && s.StrategyId == order.StrategyId).FirstOrDefaultAsync()` ile son sinyali okur. **Tercih: (b)** (daha az touch, schema degisikligi yok); ContextJson `StrategySignal.ContextJson` zaten persisted (Loop 5 sonrasi). Fallback: signal bulunmazsa `maxHoldDuration = null`.

```csharp
// (b) yontemi — son StrategySignal'i oku
var lastSignal = await db.StrategySignals.AsNoTracking()
    .Where(s => s.Symbol == order.Symbol
                && s.StrategyId == order.StrategyId
                && s.CreatedAt >= clock.UtcNow.AddMinutes(-5))   // taze sinyal kuralı
    .OrderByDescending(s => s.CreatedAt)
    .FirstOrDefaultAsync(ct);

TimeSpan? maxHoldDuration = null;
if (lastSignal is not null && !string.IsNullOrWhiteSpace(lastSignal.ContextJson))
{
    // ... yukaridaki JSON parse blogu ...
}
```

---

## C10 — Config Reset + Tests

### `src/Api/appsettings.json` — `Strategies.Seed`

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

### Detector unit tests — `tests/Tests/Infrastructure/Strategies/Patterns/Detectors/<Name>DetectorTests.cs`

Her detector icin minimum 4 test (ADR §14.9.1):

```csharp
public class BullishEngulfingDetectorTests
{
    private readonly BullishEngulfingDetector _sut = new();

    [Fact]
    public void HappyPath_VolumeAndRsiPass_ReturnsHighConfidence()
    {
        var bars = PatternTestBars.BuildBullishEngulfing(prefixBars: 30);
        var r = _sut.Detect(bars);
        Assert.NotNull(r);
        Assert.Equal(PatternType.BullishEngulfing, r!.Type);
        Assert.Equal(PatternDirection.Long, r.Direction);
        Assert.True(r.Confidence >= 0.75m);
    }

    [Fact]
    public void NoVolumeSpike_ReturnsNull()
    {
        var bars = PatternTestBars.BuildBullishEngulfing(volumeSpike: false);
        Assert.Null(_sut.Detect(bars));
    }

    [Fact]
    public void RsiOversold_ReturnsNull()
    {
        var bars = PatternTestBars.BuildBullishEngulfing(rsiTarget: 25m);
        Assert.Null(_sut.Detect(bars));
    }

    [Fact]
    public void InsufficientBars_ReturnsNull()
    {
        var bars = PatternTestBars.BuildBullishEngulfing(prefixBars: 5);
        Assert.Null(_sut.Detect(bars));
    }
}
```

**Helper — `tests/Tests/Infrastructure/Strategies/Patterns/PatternTestBars.cs`** (ortak bar synthesizer; backend-dev impl).

### Evaluator integration tests — `tests/Tests/Infrastructure/Strategies/PatternScalpingEvaluatorTests.cs`

(ADR §14.9.2 tablosu — 5 test).

---

## Acceptance / Done Definition

- [ ] `dotnet build` clean (`src/` + `tests/`).
- [ ] `dotnet test` tum yeni testler pass (>50 test eklenmis olmali).
- [ ] `dotnet ef database update` clean; `Strategies` tablosu yeni 3 row icerir; `Positions.MaxHoldDurationSeconds` kolonu mevcut.
- [ ] `dotnet run --project src/Api` boot clean; UI'da 3 strateji `Active` gorunur (`BTC-Pattern-Scalper`, `BNB-Pattern-Scalper`, `XRP-Pattern-Scalper`).
- [ ] Boot 2 dakika sonra log'da `PatternScalping: weak signal score=...` veya `StrategySignalEmittedEvent type=patternscalping leaderPattern=...` goruntulenmesi (filtre calisiyor).
- [ ] tester Playwright ile `/dashboard` gezer, 3 strateji listesini dogrular, herhangi bir pattern emit'i UI'a yansir mi (sinyal tablosu).
- [ ] reviewer ADR-0014 + bu dokumana karsi PR check (Clean Architecture dependency rule, SOLID, DRY, KISS).

---

## Riskler / Notlar

- **OHLC_Candlestick_Patterns API'sı kontrol edilmeli.** GitHub son commit + sinif/method imzasi guncel olmayabilir; gerekirse `IsHammer/IsMorningStar` cagrilarini manuel pattern check ile degistir.
- **Custom pattern detector'lar (Flag/Triangle/DoubleBottom)** ~200-300 satir is; pivot-detection algoritmasi 3-bar swing low/high (`bars[i].Low < bars[i-1].Low && bars[i].Low < bars[i+1].Low && ...`).
- **MaxHoldDuration migration** mevcut acik pozisyonlarin `StopPrice`'ini bozmaz; sadece yeni kolon NULL.
- **`StrategySignal.ContextJson`** zaten persisted (Loop 5'ten sonra schema). Pattern stratejisinin context'i icindeki `maxHoldBars` field'i deserialize etmek icin `OrderFilledPositionHandler` `db.StrategySignals` lookup'i ekler — ekstra DB roundtrip (open pozisyon basina); kabul edilebilir performans (dakikada birkac fill).
- **Test sayisi artisi:** 14 detector × 4 = 56 + evaluator 5 = 61 yeni test; mevcut suite ~250 testin uzerine eklenir, CI suresi `<2 dk` artisi.
- **Confidence puanlari empirik.** Loop 16 sonu raporunda (`loops/loop_16/summary.md`) actual WR vs beklenen WR farki not edilir; Loop 17 adaptive learning bu veriyle calisir.

---

## Bagli Dokumanlar

- [ADR-0014 Pattern-Based Scalping Reform](../../docs/adr/0014-pattern-based-scalping-reform.md) — normatif karar
- [research-pattern-scalping.md](./research-pattern-scalping.md) — binance-expert AR-GE 401 satir
- [ADR-0011 §11.1-§11.6](../../docs/adr/0011-equity-aware-sizing-and-risk-tracking.md) — sizing service korunur
- [ADR-0012 §12.3 §12.4](../../docs/adr/0012-trade-frequency-realism-reform.md) — StopLossMonitor + Position.StopPrice korunur, time-stop kolu eklenir
