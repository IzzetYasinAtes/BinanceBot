# Loop 5 — Decision: Trade Frequency + Realism Reform (Operasyonel)

> Karar: [ADR-0012](../docs/adr/0012-trade-frequency-realism-reform.md). Bu dosya backend-dev icin commit-by-commit kod sablonu + dosya/satir referansi.

## Backend-dev Komut Zinciri (sirayla, her commit ayri PR ya da tek branch ardisik commit)

### Commit 1 — Ticker24h DTO + IBinanceMarketData genislemesi (P0-1a)

**Dosya:** `src/Application/Abstractions/Binance/Ticker24hDto.cs` (yeni)

```csharp
namespace BinanceBot.Application.Abstractions.Binance;

public sealed record Ticker24hDto(
    string Symbol,
    decimal LastPrice,
    decimal PriceChangePct,   // -2.34 = -2.34%
    decimal HighPrice,
    decimal LowPrice,
    decimal QuoteVolume,
    DateTimeOffset CloseTime);
```

**Dosya:** `src/Application/Abstractions/Binance/IBinanceMarketData.cs` (genislet — yeni metod)

```csharp
Task<IReadOnlyList<Ticker24hDto>> GetTicker24hAsync(
    IReadOnlyCollection<string> symbols,
    CancellationToken cancellationToken);
```

**Dosya:** `src/Infrastructure/Binance/BinanceMarketDataClient.cs` (genislet — `GetExchangeInfoAsync` `:45` paterninden kopya)

```csharp
public async Task<IReadOnlyList<Ticker24hDto>> GetTicker24hAsync(
    IReadOnlyCollection<string> symbols,
    CancellationToken cancellationToken)
{
    if (symbols.Count == 0) return Array.Empty<Ticker24hDto>();

    var joined = "[" + string.Join(",", symbols.Select(s => $"\"{s.ToUpperInvariant()}\"")) + "]";
    var url = "/api/v3/ticker/24hr?symbols=" + Uri.EscapeDataString(joined);

    using var response = await _http.GetAsync(url, cancellationToken);
    response.EnsureSuccessStatusCode();
    using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
    using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

    var list = new List<Ticker24hDto>(symbols.Count);
    foreach (var item in doc.RootElement.EnumerateArray())
    {
        list.Add(new Ticker24hDto(
            Symbol: item.GetProperty("symbol").GetString()!,
            LastPrice: ParseDecimal(item.GetProperty("lastPrice").GetString()),
            PriceChangePct: ParseDecimal(item.GetProperty("priceChangePercent").GetString()),
            HighPrice: ParseDecimal(item.GetProperty("highPrice").GetString()),
            LowPrice: ParseDecimal(item.GetProperty("lowPrice").GetString()),
            QuoteVolume: ParseDecimal(item.GetProperty("quoteVolume").GetString()),
            CloseTime: DateTimeOffset.FromUnixTimeMilliseconds(item.GetProperty("closeTime").GetInt64())));
    }

    _logger.LogInformation("Ticker24h fetched for {Count} symbols", list.Count);
    return list;
}
```

**Test:** `tests/Tests/Infrastructure/Binance/BinanceMarketDataClient_Ticker24hTests.cs` — `WireMock` ile fake `/ticker/24hr` mocku, 3 sembol array dön → `[BTCUSDT, BNBUSDT, XRPUSDT]` parse dogrulanir.

### Commit 2 — GetMarketSummaryQueryHandler reform (P0-1b)

**Dosya:** `src/Application/MarketData/Queries/GetMarketSummary/GetMarketSummaryQuery.cs:23-92`

Mevcut handler'i tamamen rewrite:

```csharp
public sealed class GetMarketSummaryQueryHandler
    : IRequestHandler<GetMarketSummaryQuery, Result<IReadOnlyList<MarketSummaryDto>>>
{
    private readonly IApplicationDbContext _db;
    private readonly IBinanceMarketData _binance;

    public GetMarketSummaryQueryHandler(IApplicationDbContext db, IBinanceMarketData binance)
    {
        _db = db;
        _binance = binance;
    }

    public async Task<Result<IReadOnlyList<MarketSummaryDto>>> Handle(
        GetMarketSummaryQuery request, CancellationToken ct)
    {
        var ticker24h = await _binance.GetTicker24hAsync(request.Symbols, ct);
        var symbolValues = request.Symbols.Select(s => s.ToUpperInvariant()).ToHashSet();

        var bookTickers = await _db.BookTickers.AsNoTracking()
            .ToListAsync(ct);
        var bookByName = bookTickers
            .Where(b => symbolValues.Contains(b.Symbol.Value))
            .ToDictionary(b => b.Symbol.Value, b => b);

        var results = ticker24h.Select(t =>
        {
            var markPrice = bookByName.TryGetValue(t.Symbol, out var bt)
                ? (bt.BidPrice + bt.AskPrice) / 2m
                : t.LastPrice;
            return new MarketSummaryDto(
                t.Symbol,
                t.LastPrice,
                markPrice,
                t.PriceChangePct,
                t.QuoteVolume,
                t.CloseTime);
        }).ToList();

        return Result.Success<IReadOnlyList<MarketSummaryDto>>(results);
    }
}
```

**Sebep `bookTickers.ToListAsync` + filter:** EF Core `Symbol.Value` HasConversion'i `Where`'da Contains'i translate edemez (StrategySignalToOrderHandler:74 yorumu ile ayni problem). In-memory filter ucuz (3 row).

**Test:** `tests/Tests/Application/MarketData/GetMarketSummaryQueryHandlerTests.cs` — `IBinanceMarketData` mock (`Moq`); 3 sembol icin `Ticker24hDto` doner; result'in `ChangePct` = mock'tan geleni test eder.

### Commit 3 — XRP-Grid bandi reseed (P0-2)

**Dosya:** `src/Api/appsettings.json:81`

Eski:
```json
"ParametersJson": "{\"LowerPrice\":0.50,\"UpperPrice\":0.80,\"GridCount\":10,\"OrderSize\":5}"
```

Yeni:
```json
"ParametersJson": "{\"LowerPrice\":1.30,\"UpperPrice\":1.65,\"GridCount\":20,\"OrderSize\":5}"
```

**Operasyonel adim (StrategySeeder.cs:84-85 Active strateji parametre swap'i skip ediyor):**

1. UI veya `POST /api/strategies/{id}/deactivate` ile `XRP-Grid` deactivate.
2. App restart (StrategySeeder yeni `ParametersJson` ile mevcut row'u update eder — Status `!= Active` durumda).
3. `POST /api/strategies/{id}/activate` ile aktif et.

**Reviewer kontrolu:** boot log "Strategy params updated for XRP-Grid" gormeli; DB `Strategies.ParametersJson` yeni JSON'a eslesmeli; `GridEvaluator` artik bar'larda `null` donmemeli (XRP fiyati bantta kalirsa).

### Commit 4 — Indicators helper (DRY refactor for §12.5)

**Dosya:** `src/Infrastructure/Strategies/Evaluators/Indicators.cs` (yeni)

```csharp
namespace BinanceBot.Infrastructure.Strategies.Evaluators;

internal static class Indicators
{
    public static decimal Rsi(IReadOnlyList<BinanceBot.Domain.MarketData.Kline> bars, int period)
    {
        if (bars.Count < period + 1) return 50m;
        decimal gainSum = 0m, lossSum = 0m;
        var start = bars.Count - period;
        for (var i = start; i < bars.Count; i++)
        {
            var diff = bars[i].ClosePrice - bars[i - 1].ClosePrice;
            if (diff >= 0m) gainSum += diff;
            else lossSum -= diff;
        }
        var avgGain = gainSum / period;
        var avgLoss = lossSum / period;
        if (avgLoss == 0m) return 100m;
        var rs = avgGain / avgLoss;
        return 100m - (100m / (1m + rs));
    }

    public static decimal Ema(IReadOnlyList<BinanceBot.Domain.MarketData.Kline> bars, int period, int endIndex)
    {
        if (endIndex < period - 1) return bars[endIndex].ClosePrice;
        decimal alpha = 2m / (period + 1);
        decimal ema = bars[endIndex - period + 1].ClosePrice;
        for (var i = endIndex - period + 2; i <= endIndex; i++)
            ema = alpha * bars[i].ClosePrice + (1 - alpha) * ema;
        return ema;
    }

    public static decimal Atr(IReadOnlyList<BinanceBot.Domain.MarketData.Kline> bars, int period)
    {
        if (bars.Count < period + 1) return 0m;
        var start = bars.Count - period;
        decimal sum = 0m;
        for (var i = start; i < bars.Count; i++)
        {
            var high = bars[i].HighPrice;
            var low = bars[i].LowPrice;
            var prevClose = bars[i - 1].ClosePrice;
            var tr = Math.Max(high - low, Math.Max(Math.Abs(high - prevClose), Math.Abs(low - prevClose)));
            sum += tr;
        }
        return sum / period;
    }
}
```

`TrendFollowingEvaluator.cs:64-93` ve `MeanReversionEvaluator.cs:65-101` private static metodlari sil, `Indicators.Rsi/Ema/Atr` cagrilarina cevir. **Davranis identical** — sadece DRY.

### Commit 5 — TrendFollowing parametre + RSI filtresi (P1-4)

**Dosya:** `src/Infrastructure/Strategies/Evaluators/TrendFollowingEvaluator.cs:11-18`

```csharp
private sealed class Parameters
{
    public int FastEma { get; set; } = 3;
    public int SlowEma { get; set; } = 8;
    public int AtrPeriod { get; set; } = 14;
    public decimal AtrStopMultiplier { get; set; } = 2.0m;
    public decimal OrderSize { get; set; } = 0.001m;
    public int RsiPeriod { get; set; } = 14;
    public decimal RsiMin { get; set; } = 30m;
    public decimal RsiMax { get; set; } = 70m;
}
```

`EvaluateAsync` icine cross check sonrasi (`if (!crossedUp && !crossedDown) return null;` ardindan):

```csharp
var rsi = Indicators.Rsi(closedBars, p.RsiPeriod);
if (rsi < p.RsiMin || rsi > p.RsiMax)
{
    // Cross + RSI ekstrem → whipsaw riski. Sinyal eler.
    return Task.FromResult<StrategyEvaluation?>(null);
}
```

**`appsettings.json:67`:**
```json
"ParametersJson": "{\"FastEma\":3,\"SlowEma\":8,\"AtrPeriod\":14,\"AtrStopMultiplier\":2.0,\"OrderSize\":0.001,\"RsiPeriod\":14,\"RsiMin\":30,\"RsiMax\":70}"
```

**Aktif strateji icin reseed adimi:** Commit 3 ile ayni sira (deactivate → restart → activate) — `BTC-Trend-Fast`.

### Commit 6 — MeanReversion BB stdDev gevsetme (P1-5)

**Dosya:** `src/Api/appsettings.json:74`

```json
"ParametersJson": "{\"RsiPeriod\":14,\"RsiOversold\":30,\"RsiOverbought\":70,\"BbPeriod\":20,\"BbStdDev\":1.5,\"OrderSize\":0.01}"
```

Evaluator kod degisikligi yok — `BbStdDev` zaten parametrik (`MeanReversionEvaluator.cs:17`). `BNB-MeanRev` reseed adimi: commit 3 ile ayni.

### Commit 7 — Position.StopPrice + migration (P0-3 alt-yapi)

**Dosya:** `src/Domain/Positions/Position.cs`

```csharp
public decimal? StopPrice { get; private set; }   // line ~14 ekle

public static Position Open(
    Symbol symbol,
    PositionSide side,
    decimal quantity,
    decimal entryPrice,
    decimal? stopPrice,                           // YENI parametre
    long? strategyId,
    TradingMode mode,
    DateTimeOffset now)
{
    if (quantity <= 0m) throw new DomainException("Position quantity must be positive.");
    if (entryPrice <= 0m) throw new DomainException("Entry price must be positive.");
    if (stopPrice is decimal s && s <= 0m) throw new DomainException("Stop price must be positive when set.");

    var position = new Position
    {
        Symbol = symbol,
        Side = side,
        Status = PositionStatus.Open,
        Quantity = quantity,
        AverageEntryPrice = entryPrice,
        MarkPrice = entryPrice,
        StopPrice = stopPrice,                    // YENI
        StrategyId = strategyId,
        Mode = mode,
        OpenedAt = now,
        UpdatedAt = now,
    };
    position.RaiseDomainEvent(new PositionOpenedEvent(
        position.Id, symbol.Value, side, entryPrice, quantity, mode));
    return position;
}
```

**Cagri yeri (tek):** `src/Infrastructure/Orders/OrderFilledPositionHandler.cs` — `Position.Open(...)` cagrisina `stopPrice: order.StopPrice` parametresi eklenir. (Order zaten `StopPrice` tutuyor; `PlaceOrderCommand` `StopPrice` parametresi mevcut mu — degil ise ekle.)

**`PlaceOrderCommand` kontrolu:** `src/Application/Orders/Commands/PlaceOrder/PlaceOrderCommand.cs` — `StopPrice` field'i yoksa ekle (record genislet); `StrategySignalToOrderHandler.cs:153-163` `cmd` constructor'ina `stopPrice: notification.SuggestedStopPrice` gec.

**Migration:** `dotnet ef migrations add AddPositionStopPrice --project src/Infrastructure --startup-project src/Api`. Generated SQL: `ALTER TABLE Positions ADD StopPrice decimal(18,8) NULL;`.

**EF config:** `src/Infrastructure/Persistence/Configurations/PositionConfiguration.cs` — `builder.Property(p => p.StopPrice).HasColumnType("decimal(18,8)");` ekle.

### Commit 8 — StopLossMonitorService (P0-3 ana akis)

**Dosya:** `src/Infrastructure/Trading/StopLossMonitorService.cs` (yeni)

```csharp
using BinanceBot.Application.Abstractions;
using BinanceBot.Application.Positions.Commands.ClosePosition;
using BinanceBot.Domain.Positions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BinanceBot.Infrastructure.Trading;

public sealed class StopLossMonitorService : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(30);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StopLossMonitorService> _logger;

    public StopLossMonitorService(
        IServiceScopeFactory scopeFactory,
        ILogger<StopLossMonitorService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("StopLossMonitor started, tick={Sec}s", TickInterval.TotalSeconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "StopLossMonitor tick failed");
            }
            try { await Task.Delay(TickInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task TickOnceAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var openPositions = await db.Positions.AsNoTracking()
            .Where(p => p.Status == PositionStatus.Open && p.StopPrice != null)
            .ToListAsync(ct);

        if (openPositions.Count == 0) return;

        var bookTickers = (await db.BookTickers.AsNoTracking().ToListAsync(ct))
            .ToDictionary(b => b.Symbol, b => b);

        foreach (var pos in openPositions)
        {
            if (pos.StopPrice is not decimal stop) continue;
            if (!bookTickers.TryGetValue(pos.Symbol, out var bt)) continue;

            var markPrice = pos.Side == PositionSide.Long ? bt.BidPrice : bt.AskPrice;
            if (markPrice <= 0m) continue;

            var triggered = pos.Side == PositionSide.Long
                ? markPrice <= stop
                : markPrice >= stop;
            if (!triggered) continue;

            var cidPrefix = $"stop-{pos.Id}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
            var reason = $"stop_loss_triggered@{markPrice:F4}_stop={stop:F4}";

            var result = await mediator.Send(new CloseSignalPositionCommand(
                pos.Symbol.Value, pos.StrategyId, pos.Mode, reason, cidPrefix), ct);

            if (result.IsSuccess)
            {
                _logger.LogWarning(
                    "STOP-LOSS triggered pos={PosId} mode={Mode} mark={Mark} stop={Stop} cid={Cid}",
                    pos.Id, pos.Mode, markPrice, stop, result.Value.CloseClientOrderId);
            }
            else if (result.Status != Ardalis.Result.ResultStatus.NotFound)
            {
                _logger.LogError(
                    "STOP-LOSS close failed pos={PosId} mode={Mode}: {Errors}",
                    pos.Id, pos.Mode, string.Join(";", result.Errors));
            }
        }
    }
}
```

**DI:** `src/Api/Program.cs` — `services.AddHostedService<StopLossMonitorService>();` (StrategySeeder `AddHostedService` cagrisinin yanina).

**Test:** `tests/Tests/Infrastructure/Trading/StopLossMonitorServiceTests.cs` — InMemory DbContext + Position.Open(stopPrice=100) + BookTicker(BidPrice=99) → tek tick sonrasi `CloseSignalPositionCommand` Mediator'a gonderildi (mock IMediator).

### Commit 9 — PaperFillSimulator latency (P2-8)

**Dosya:** `src/Infrastructure/Trading/Paper/PaperFillOptions.cs`

```csharp
public sealed record PaperFillOptions
{
    public decimal FixedSlippagePct { get; init; } = 0.0005m;
    public int SimulatedLatencyMs { get; init; } = 100;   // YENI
}
```

**Dosya:** `src/Application/Abstractions/Trading/IPaperFillSimulator.cs` (imza degisikligi)

```csharp
Task<PaperFillOutcome> SimulateAsync(
    Order order, Instrument instrument, BookTicker bookTicker,
    OrderBookSnapshot? depthSnapshot, DateTimeOffset now, CancellationToken ct);
```

**Dosya:** `src/Infrastructure/Trading/Paper/PaperFillSimulator.cs:33-92`

```csharp
public async Task<PaperFillOutcome> SimulateAsync(
    Order order, Instrument instrument, BookTicker bookTicker,
    OrderBookSnapshot? depthSnapshot, DateTimeOffset now, CancellationToken ct)
{
    if (_options.SimulatedLatencyMs > 0)
    {
        await Task.Delay(_options.SimulatedLatencyMs, ct);
    }
    // ... mevcut Simulate body identical, sync return olarak kalır
    var filterFailure = ValidateFilters(order, instrument);
    // ... vs.
}
```

**Cagri yeri:** Order placement pipeline (`PaperFillOrderExecutor` veya benzer Infrastructure servisi). `Simulate` cagrilari `await SimulateAsync(... ,ct)` olarak update.

**`appsettings.json:50`:**
```json
"PaperFill": {
  "FixedSlippagePct": 0.0005,
  "SimulatedLatencyMs": 100
}
```

**Test guncellemesi:** `tests/Tests/Infrastructure/Trading/PaperFillSimulator*Tests.cs` — `Simulate(...)` → `await SimulateAsync(..., CancellationToken.None)`. Test `PaperFillOptions` `SimulatedLatencyMs=0` ile inject (test hizli kalsin).

### Commit 10 — CB Audit Log (P3-9)

**Dosya:** `src/Application/RiskProfiles/Commands/RecordTradeOutcome/RecordTradeOutcomeCommand.cs:32`

`profile.RecordTradeOutcome(...)` cagrisinin oncesi/sonrasi audit log:

```csharp
_logger.LogInformation(
    "CB-AUDIT mode={Mode} pnl={Pnl} consecBefore={Before} statusBefore={StatusBefore} ddBefore={DDBefore}",
    request.Mode, request.RealizedPnl, profile.ConsecutiveLosses,
    profile.CircuitBreakerStatus, profile.CurrentDrawdownPct);

profile.RecordTradeOutcome(request.RealizedPnl, request.EquityAfter, _clock.UtcNow);

_logger.LogInformation(
    "CB-AUDIT mode={Mode} consecAfter={After} statusAfter={StatusAfter} ddAfter={DDAfter}",
    request.Mode, profile.ConsecutiveLosses, profile.CircuitBreakerStatus, profile.CurrentDrawdownPct);
```

**`RecordTradeOutcomeCommandHandler` constructor:** `ILogger<RecordTradeOutcomeCommandHandler>` injection ekle.

**Dosya:** `src/Infrastructure/Positions/PositionClosedRiskHandler.cs:30`

`await using var scope = ...` sonrasi:
```csharp
_logger.LogInformation(
    "PositionClosed handler entered pos={PosId} mode={Mode} pnl={Pnl} reason={Reason}",
    notification.PositionId, notification.Mode, notification.RealizedPnl, notification.Reason);
```

Loop 5 t30 raporunda `CB-AUDIT` ve `PositionClosed handler entered` log'larini grep ile cikar; root cause:
- `CB-AUDIT consecBefore` artmiyorsa → handler hic cagrilmiyor (PositionClosedEvent dispatch sorunu);
- `consecAfter` artiyor ama `statusAfter=Healthy` → CB tetikleyici kosul kayma var (kod review);
- `RealizedPnl=0` → `RecordTradeOutcome` `< 0` branch'ine girmiyor → CB hic tetiklenmez (zero-pnl bug, ayri fix).

## Commit Sirasi Zorlamasi

```
[1] Ticker24h DTO + IBinanceMarketData       (P0-1a)  bagimsiz
[2] GetMarketSummaryQueryHandler reform      (P0-1b)  [1] gerek
[3] XRP-Grid bandi reseed                    (P0-2)   bagimsiz (config)
[4] Indicators helper refactor               (DRY)    bagimsiz
[5] TrendFollowing RSI filtresi              (P1-4)   [4] gerek
[6] MeanReversion BbStdDev=1.5               (P1-5)   bagimsiz (config) [4 sonrasi recommend]
[7] Position.StopPrice + migration           (P0-3a)  bagimsiz
[8] StopLossMonitorService                   (P0-3b)  [7] gerek
[9] PaperFill latency + async                (P2-8)   bagimsiz
[10] CB Audit Log                            (P3-9)   bagimsiz
```

Backend-dev birlestirme onerisi: **iki PR** — PR-A: [1][2][3][6] config + market summary (kucuk, hizli merge); PR-B: [4][5][7][8] indicators + trend RSI + stop monitor (buyuk, careful review). PR-C ileri: [9][10] paper async + audit (post-test).

## Tester Senaryolari

1. **Market Summary 24h dogrulugu:** `GET /api/market/summary?symbols=BTCUSDT,BNBUSDT,XRPUSDT` → her sembol icin `changePct` Binance testnet `/api/v3/ticker/24hr` ile %0.5 tolerans icinde eslesmeli. Playwright UI'da yuzdelik degerlerin `0.00%` olmadigini kontrol etmeli.
2. **XRP-Grid sinyal:** Strateji aktive edildikten sonra 30 dakika icinde XRP fiyat 1.30-1.65 araliginda kalir kalmaz `StrategySignals` tablosunda XRP-Grid kaynakli en az 1 satir olmali (testnet aktivite/hareket on koşulu).
3. **Stop-loss tetikleme:** Manual test — DB'ye `Position.Open(symbol=BTCUSDT, side=Long, entry=110000, stopPrice=109500, status=Open)` insert; `BookTicker.BidPrice=109400`; 30s icinde StopLossMonitorService log "STOP-LOSS triggered" ve `CloseSignalPositionCommand` mediator'a dustugu test edilir; pozisyon `Status=Closed` olur.
4. **TrendFollowing frekans:** 60 dakika gozlem; trend stratejisinden gelen `StrategySignals` sayisi en az 3 olmali (Loop 4'te 0-1 idi).
5. **Paper latency:** `PlaceOrderCommand` (Paper modu) gonderiminden `OrderFilledEvent`'e kadar geçen sure 80ms+; assertion `>= 90ms` (100ms ± jitter).

## Reviewer Checklist

- [ ] `IBinanceMarketData` arayuzune yeni metod eklenmesi mevcut implementasyon (`BinanceMarketDataClient`) ve mock'larda update edildi.
- [ ] `GetMarketSummaryQueryHandler` artik DB Klines okumaz; eski `dayAgoClose`/`volume24h` kodu silindi.
- [ ] `Position.StopPrice` migration generated; reverse migration testi (`dotnet ef migrations remove` + re-add idempotent).
- [ ] `StopLossMonitorService` `OperationCanceledException` saygi (graceful shutdown), exception logging structured.
- [ ] `PaperFillSimulator.SimulateAsync` her cagri yeri `await` ile cagrilmis; sync `Simulate` artik mevcut degil.
- [ ] `Indicators` helper public/internal scope dogru — sadece evaluators namespace'inden cagrilmali.
- [ ] `appsettings.json` JSON formati gecerli (yeni alanlar, virgul/brace).
- [ ] CB audit log structured; `mode`/`pnl`/`consec` field'lari production log search icin kullanılabilir.

## ADR Cakisma Onayi

ADR-0012 §12.11'de detayli; ozet: ADR-0009 (backfill) etkilenmez, ADR-0011 (sizing/exit) etkilenmez (`Position.StopPrice` ek alan, mevcut `OrderFilledPositionHandler` genisletilir), ADR-0008 (fan-out per mode) korunur, ADR-0006 (mainnet bloklu) ihlali yok.

## Sonra

- Loop 5 sonu t30 raporunda CB-AUDIT log'larindan root cause cikar → P3 fix tek-satir commit.
- Loop 6 ADR-0013 candidate: server-side OCO STOP_LOSS_LIMIT (mainnet pre-flight).
- Loop 6 ADR-0014 candidate: backtester (parametre gozlem-driven yerine veri-driven).
