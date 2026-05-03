# 0025. Spot to Futures Pivot — USDT-M Futures Testnet + Long & Short Symmetry

Date: 2026-05-03
Status: Proposed (Loop 92 — Spot Testnet 12 loop -$17.04 sonrası)
Relates to: ADR-0006 (Testnet-First, hâlâ aktif), ADR-0008 (Trading Modes — sadece Paper kalır), ADR-0023 (R:R asymmetry), ADR-0024 (Pattern-composite, korunur)
Memory ref: `trading_vision.md`, `feedback_frekans_kartopu.md`, `feedback_no_dead_code.md`, `feedback_no_session_split.md`

> Özet: Loop 80-91 boyunca Binance Spot Testnet üzerinde long-only pattern-composite scalper -$17.04 net (12 ardışık loop, hiçbir loop +karda kapanmadı). Long-only mimari kripto piyasasının iki yönlü doğasına uygunsuz; downtrend rejimlerde "0 emit + slow drift loss" oluyor. Loop 92 itibarıyla Binance USDT-M Futures Testnet (`testnet.binancefuture.com`) üzerine pivot edilir; hem Long hem Short emit'i destekleyen simetrik mimari, `IExchangeClient` Strategy Pattern portu, futures-spesifik domain (TradeDirection enum, FuturesAccount cüzdan modeli, leverage + margin), simetrik 10 pattern detector ekleme yapılır. Spot kodu CLAUDE.md §13 "deprecated kod yasak" gereği tamamen silinir; Trading:Mode=Futures sabit. ADR-0024 PatternComposer / BarSnapshot / IPatternDetector altyapısı tamamen korunur — sadece detector seti çiftlenir ve composer Long/Short ayrımı yapar.

---

## Context

### 25.1 Loop 80-91 Spot Long-Only Sonuçları

| Loop | Süre | Trade | Net PnL | Halt sebebi |
|---|---|---|---|---|
| 80 | 270dk | 7 | -$0.518 | 90dk üstüste 0 emit, ADR-0024 pivot tetiği |
| 81-85 | 5 loop | ~40 | -$3.21 | MTF gate sahte breakout filtreliyor + downtrend periodu yok-emit |
| 86-89 | 4 loop | ~30 | -$5.84 | "Pazar dönüşü +emit" patterni tek yönlü, geri dönüşler her zaman zarar |
| 90 | 4h | 18 | -$2.31 | MTF kapatma açtık, sahte breakout büyüdü |
| 91 | 4h | 12 | -$5.16 | MTF tekrar açıldı (slope < -%0.1 strict), carryover -$0.55 kötüleşti |

**Kümülatif:** 12 loop, ~107 trade, **net -$17.04** (Paper $500 → $482.96). Hiçbir loop +karda kapanmadı.

### 25.2 Kök Sorun — Long-Only Mimari Kripto'ya Uygunsuz

1. **Yön asimetrisi:** Pattern detector seti (BullishEngulfing, HammerReversal, BollingerLowerReversal, RsiOversoldRecovery, Ema9SlopeMomentum, DonchianBreakoutHigh) **hepsi long-bias**. Kripto 24/7 — uzun downtrend dilimleri var (Loop 90-91 BTC -2.5%, ETH -3.1%); long-only bot bu dilimde ya 0 emit ya yanlış-yönlü emit veriyor.
2. **MTF gate'in çıkmazı:** Long için "15m EMA21 slope > 0" filtresi açıkken downtrend dilimde 90+ dakika emit gelmiyor (frekans kuralı CLAUDE.md §12 ihlali). Filtre kapatınca sahte breakout long pozisyonları downtrend'de SL'e gidiyor. **Bu cebrî paradoks**: long-only sistemde downtrend = ya iddiacı zarar ya da kuralı ihlal eden frekans kaybı.
3. **Spot vs Futures kavramsal eşitsizlik:** Spot'ta sadece "buy with cash, sell to cash" var — short fiziksel olarak imkansız (margin lending dışında). Bot mainnet senaryosuna hazırlık için **Futures USDT-M** üzerine geçmek zorunda; çift yönlü emit + leverage + margin model.
4. **Frekans kuralı (§12):** "Saatte 30+ trade, ideal 150/h, 0 emit > 1h pivot". Loop 91'de 12 trade / 4h = 3 trade/h — kural hâlâ ezilmiş. Çift yönlü detector seti ortalama emit'i en az 2× artırır (long ve short pencereleri ayrı bar'larda açılır).
5. **Vision uyumu:** `trading_vision.md` "5-10 chart pattern detector, ağırlıklı sinyal, kartopu kar". Vision Spot ile Futures arasında ayrım yapmıyor — Futures pivot vision'ı **bozmuyor**, **gerçekleştirme medium**'unu güncelliyor.

### 25.3 Halihazır Mimaride Korunacaklar — Net Sınır

ADR-0024 Pattern-Composite mimarisi **tamamen korunur**:
- `Strategy` aggregate (Domain) — Type=PatternComposite=3 değişmez.
- `StrategySignal` entity — `SuggestedPrice/Stop/TakeProfit/ContextJson` korunur; **TradeDirection field eklenir** (yeni domain enum).
- `IPatternDetector` port + `BarSnapshot` shared dto + `IPatternSignalComposer` — yapı aynen kalır.
- `IStrategyEvaluator` + `PatternCompositeEvaluator` — Long/Short ayrım composer içinde, evaluator dış sözleşmesi değişmez.
- `MarkToMarketWorker` BE move + trailing — **işleyişi simetrize edilir**, varlığı korunur.
- `ICooldownService`, sizing, fee accounting (ADR-0020 §20.6), ADR-0023 R:R asymmetry — değişmez.

**Değişen / yeni:**
- `IBinanceTrading` port → `IExchangeClient` jenerik portuna refactor; tek implementation `BinanceFuturesClient`.
- `BinanceTradingClient` (Spot) + `BinanceMarketDataClient` (Spot endpoints) — **silinir**.
- `Position.Side` (mevcut PositionSide.Long/Short value object) zaten Long/Short biliyor; ama veriyolu yarım kullanılıyor (long-only emit). Yeni `TradeDirection` enum **StrategySignal**'a eklenir; Position.Side bunun karşılığında kalır.
- `VirtualBalance` aggregate — **futures cüzdan modeline genişler**: `WalletBalance + AllocatedMargin + UnrealizedPnl = Equity`. Spot mantığı (cash invariance) silinir.
- 10 yeni "short" pattern detector (`BearishEngulfingDetector`, vb.) eklenir. Toplam 20 detector; composer Long ve Short kovasını ayrı toplar, **yüksek skor olanı emit eder** (aynı bar'da iki yön çakışırsa 0 emit — guard).
- MTF gate Long için slope > 0, Short için slope < 0 — simetrik.
- Risk profili: Leverage default 1x, max 3x; MarginRatio threshold; Funding rate.

### 25.4 Trading Modes — ADR-0008 Sadeleştirmesi

ADR-0008 üç-yollu fan-out (Paper / LiveTestnet / LiveMainnet) tasarımı 12 loop boyunca sadece **Paper** modunda çalıştı. LiveTestnet "no_credentials" ile 0 trade attı; LiveMainnet kalıcı blocked. Bu ADR ile:

- `TradingMode` enum **korunur** (3 değer aynı kalır — schema breaking olmasın).
- Aktif emit hattı **sadece Paper** kalır (`StrategySignalToOrderHandler` fan-out devam — fakat Futures'ta Paper sim hattı yeniden yazılır; LiveTestnet/Mainnet branch'leri mevcut "reject + SystemEvent" sözleşmesinde kalır, sadece endpoint'ler Spot v3 yerine Futures v1).
- Mainnet'e geçişte ADR-0006 testnet-first politikası **aynen** geçerli — `AllowMainnet=false` guard, `LiveMainnet → Reject` kontratı.

### 25.5 ADR-0024 Pattern Subsystem'in Long/Short Genişlemesi

ADR-0024 §24.5 mimari diyagramı korunur. Tek yapısal eklenti: `BarSnapshot` ve `IPatternDetector` aynı, ama `PatternEvaluation` çıktısı **yön bilgisi taşımaz** (detector inherently long-bias veya short-bias). Composer **iki kova** yönetir:

```
LongDetectors  = [BullishEngulfing, HammerReversal, BollingerLowerRev, RsiOversoldRecov, Ema9SlopeUp, DonchianBreakoutHigh, ...]
ShortDetectors = [BearishEngulfing, ShootingStar, BollingerUpperRev, RsiOverboughtPull, Ema9SlopeDown, DonchianBreakoutLow, ...]
SharedGates    = [VolumeSurgeConfirm, SpreadGuard, AdxRegime]  // yön-agnostik (ikisinde de aynı)
```

Composer akışı: Long kova `total_long`, Short kova `total_short`. Hard-gate (Volume + Spread) ortak. Kararı:
- `total_long >= req && total_short < req` → emit Long.
- `total_short >= req && total_long < req` → emit Short.
- İkisi de >= req → emit yok (`skip:both_directions_qualified` — confluence belirsiz, frekans kaybı kabul edilir; yanlış yön daha pahalı).
- İkisi de < req → emit yok (`skip:no_direction_qualified`).

---

## Decision

### 25.6 IExchangeClient Strategy Pattern Portu

**Karar:** Application katmanında `IExchangeClient` jenerik port açılır; `IBinanceTrading` (Spot) ve `BinanceMarketDataClient` (Spot endpoints) **silinir**, yerlerine `BinanceFuturesClient` (Infrastructure) tek implementation gelir.

```csharp
// Application/Abstractions/Exchange/IExchangeClient.cs
public interface IExchangeClient
{
    Task<Result<ExchangeAccountInfo>> GetAccountInfoAsync(CancellationToken ct);
    Task<Result<IReadOnlyList<ExchangeInfoSymbolDto>>> GetExchangeInfoAsync(CancellationToken ct);
    Task<Result<TestOrderResponse>> PlaceTestOrderAsync(PlaceOrderRequest req, CancellationToken ct);
    Task<Result<LiveOrderResponse>> PlaceLiveOrderAsync(PlaceOrderRequest req, CancellationToken ct);
    Task<Result<IReadOnlyList<RestKlineDto>>> GetKlinesAsync(string symbol, KlineInterval interval, int limit, CancellationToken ct);
    Task<Result<OrderBookSnapshotDto>> GetOrderBookSnapshotAsync(string symbol, int depth, CancellationToken ct);
    Task<Result> SetLeverageAsync(string symbol, int leverage, CancellationToken ct);  // futures-only
    Task<Result> SetMarginTypeAsync(string symbol, MarginType type, CancellationToken ct);  // ISOLATED|CROSSED
}

public enum MarginType { Isolated = 1, Crossed = 2 }

public sealed record ExchangeAccountInfo(
    decimal WalletBalance,
    decimal AvailableBalance,
    decimal TotalUnrealizedPnl,
    decimal TotalMarginBalance,
    IReadOnlyList<ExchangePositionDto> OpenPositions);

public sealed record ExchangePositionDto(
    string Symbol,
    PositionSide Side,
    decimal PositionAmt,    // signed: + Long, − Short, 0 closed
    decimal EntryPrice,
    decimal UnrealizedPnl,
    decimal Leverage,
    decimal IsolatedMargin);
```

**Gerekçe:**
- `PlaceOrderRequest` zaten symbol-agnostik; sadece endpoint URL ve auth değişiyor.
- Strategy pattern formal isim: `IExchangeClient` "platform port", `BinanceFuturesClient` "concrete strategy". İleride başka exchange (OKX, Bybit) eklenirse interface stabil — Spot'a dönüş düşük olasılık olduğu için BinanceSpotClient yazılmaz (CLAUDE.md §13).
- WS supervisor (`BinanceWsSupervisor`) ve stream parser (`BinanceStreamParser`) Spot endpoint'lerine özel — Futures için yeniden yazılır (URL: `wss://stream.binancefuture.com/ws`, listenKey: `/fapi/v1/listenKey`). Eski supervisor silinir.

**Composition Root (Api/Program.cs):**
```csharp
// appsettings: Trading:Mode = "Futures" sabit (ileride enum).
services.AddHttpClient<IExchangeClient, BinanceFuturesClient>("binance-futures", c =>
    c.BaseAddress = new Uri(opts.RestBaseUrl));  // testnet: https://testnet.binancefuture.com
```

Spot kodu silinen dosyalar: `BinanceTradingClient.cs`, `BinanceMarketDataClient.cs`, `BinanceWsSupervisor.cs` (yeni FuturesWsSupervisor ile replace), `BinanceStreamParser.cs` (FuturesStreamParser).

### 25.7 TradeDirection Domain Enum

**Karar:** Yeni enum `Domain/Common/TradeDirection.cs`:

```csharp
public enum TradeDirection
{
    Long = 1,
    Short = 2,
}

public static class TradeDirectionExtensions
{
    public static OrderSide EntrySide(this TradeDirection d) =>
        d == TradeDirection.Long ? OrderSide.Buy : OrderSide.Sell;
    public static OrderSide ExitSide(this TradeDirection d) =>
        d == TradeDirection.Long ? OrderSide.Sell : OrderSide.Buy;
}
```

**Konum gerekçesi (DDD):** Tıpkı `TradingMode` gibi platform-wide concept; `Order`, `Position`, `StrategySignal` paylaşır. `Common` doğru yer (ADR-0008 §8.1 deseni).

**Değişiklikler:**
1. `StrategySignal.Direction : TradeDirection` (yeni required field, EF migration backfill `=1` Long).
2. `StrategySignalDirection` (eski Buy/Sell enum varsa) **silinir** — `OrderSide` ile karıştırılmasın; signal artık niyet (Long/Short), order side türev.
3. `Position.Direction : TradeDirection` (yeni required field, default Long backfill). `Position.Side` (PositionSide.Long/Short value object) hâlâ duruyor — **iki farklı concept**: PositionSide bir aggregate value object (DDD ubiquitous language), TradeDirection cross-cutting domain enum. Composition: Position.Direction yetkilidir, Side default Long-mapped (Long Position ⇔ PositionSide.Long). **Karar:** `Position.Side` value object **silinir**, sadece `Position.Direction : TradeDirection` kalır — duplicate concept temizlenir (CLAUDE.md §13).
4. `Order.Direction` **eklenmez** — Order side (Buy/Sell) zaten `OrderSide` ile ifade edilir; Direction fill iz sürmez. Idempotency: ClientOrderId schema `sig-{StrategyId}-{BarOpenUnix}-{ModeSuffix}-{DirectionChar}` (`L` veya `S`) — collision yok ama audit netleşir.

### 25.8 Position.UnrealizedPnl & Close — Direction Aware

`Position.MarkToMarket` ve `Position.Close` mevcut hesaplamalarda `Side == PositionSide.Long` üzerinden ayrım yapıyor (Position.cs L196-198, L353-355). Refactor:

```csharp
public void MarkToMarket(decimal markPrice, DateTimeOffset now)
{
    EnsureOpen();
    if (markPrice <= 0m) throw new DomainException("Mark price must be positive.");

    MarkPrice = markPrice;
    UnrealizedPnl = Direction == TradeDirection.Long
        ? (markPrice - AverageEntryPrice) * Quantity
        : (AverageEntryPrice - markPrice) * Quantity;
    UpdatedAt = now;
    RaiseDomainEvent(new PositionMarkedToMarketEvent(Id, Symbol.Value, markPrice, UnrealizedPnl));
}

public void Close(decimal exitPrice, string reason, DateTimeOffset now, decimal exitCommission = 0m)
{
    EnsureOpen();
    // ...
    var gross = Direction == TradeDirection.Long
        ? (exitPrice - AverageEntryPrice) * Quantity
        : (AverageEntryPrice - exitPrice) * Quantity;
    RealizedPnl = gross - EntryCommission - ExitCommission;
    // ...
}
```

`MoveStopToBreakEven` halihazırda `Side == PositionSide.Long` ile simetrik (L243-249); `Side` → `Direction` rename + `PositionSide` referansları silinir.

`UpdatePeakAndCheckTrailing` halihazırda **long-only** (L290-333). Refactor:

```csharp
public TrailingResult UpdatePeakAndCheckTrailing(decimal markPrice, decimal trailPct, DateTimeOffset asOf)
{
    EnsureOpen();
    if (markPrice <= 0m) throw new DomainException("Mark price must be positive.");
    if (trailPct <= 0m) throw new DomainException("Trail percentage must be positive.");
    if (BreakEvenAppliedAt is null) return TrailingResult.NotEligible;

    if (Direction == TradeDirection.Long)
    {
        if (markPrice > PeakMarkPrice) { PeakMarkPrice = markPrice; UpdatedAt = asOf; return TrailingResult.PeakUpdated; }
        var trailingStop = PeakMarkPrice * (1m - trailPct);
        if (markPrice < trailingStop)
        {
            RaiseDomainEvent(new PositionTrailingExitTriggeredEvent(Id, Symbol.Value, PeakMarkPrice, markPrice, trailPct));
            return TrailingResult.ExitTriggered;
        }
    }
    else // Short — trough mantığı
    {
        // PeakMarkPrice short'ta "trough" rolü oynar; default 0 + ilk eligible tick mark < ∞ için
        // initial state'i mark'a "yatırma" disiplini: TroughMarkPrice == 0 → tick mark'a yaz.
        if (PeakMarkPrice == 0m || markPrice < PeakMarkPrice)
        {
            PeakMarkPrice = markPrice; UpdatedAt = asOf; return TrailingResult.PeakUpdated;
        }
        var trailingStop = PeakMarkPrice * (1m + trailPct);
        if (markPrice > trailingStop)
        {
            RaiseDomainEvent(new PositionTrailingExitTriggeredEvent(Id, Symbol.Value, PeakMarkPrice, markPrice, trailPct));
            return TrailingResult.ExitTriggered;
        }
    }
    return TrailingResult.PeakUpdated;
}
```

**NOT:** `PeakMarkPrice` field ismi semantik olarak Long-bias (peak = high). Short için "trough" daha doğru — fakat aggregate'ta tek decimal field; rename `ExtremeMarkPrice` (Long: peak high, Short: trough low). EF migration: kolon rename `Positions.ExtremeMarkPrice`.

### 25.9 FuturesAccount Aggregate — VirtualBalance Genişlemesi

**Karar:** `VirtualBalance` aggregate **futures cüzdan modeline genişler**, ismi korunur (schema breaking olmasın). Yeni alanlar:

```csharp
public sealed class VirtualBalance : AggregateRoot<int>
{
    public TradingMode Mode { get; private set; }
    public decimal StartingBalance { get; private set; }
    public decimal WalletBalance { get; private set; }      // realized + free; eski "CurrentBalance"
    public decimal AllocatedMargin { get; private set; }    // open positions için bloke; YENI
    public decimal UnrealizedPnl { get; private set; }      // open positions toplam UPnl; YENI
    public decimal Equity { get; private set; }             // = WalletBalance + UnrealizedPnl (Allocated DAHIL DEĞİL — margin wallet'tan ayrı bucket)
    public Guid IterationId { get; private set; }
    // ... eski timestamp alanları korunur ...

    public void OpenPositionMargin(decimal initialMargin, DateTimeOffset now) { /* WalletBalance -= margin; AllocatedMargin += margin; */ }
    public void ClosePositionMargin(decimal returnedMargin, decimal realizedPnl, DateTimeOffset now) { /* AllocatedMargin -= returnedMargin; WalletBalance += returnedMargin + realizedPnl − fee; */ }
    public void ApplyUnrealized(decimal totalUnrealized, DateTimeOffset now) { /* UnrealizedPnl = totalUnrealized; Equity = WalletBalance + UnrealizedPnl; */ }
    public void ApplyFundingFee(decimal fundingFee, DateTimeOffset now) { /* WalletBalance -= fundingFee; (long pozitifte negatif olur, vb.) */ }
}
```

**Eski `ApplyFill(realizedDelta)` silinir** — futures'ta "round-trip realized delta" kavramı margin akışıyla parçalanır (open margin alır, close margin döner + PnL eklenir). Yerine 4 davranış: `OpenPositionMargin`, `ClosePositionMargin`, `ApplyUnrealized`, `ApplyFundingFee`.

**Migration `AddFuturesWalletColumns`:**
- `Positions.Mode` zaten var; backfill: `WalletBalance := CurrentBalance`, `AllocatedMargin := 0`, `UnrealizedPnl := 0`.
- Eski `CurrentBalance` kolonu **rename** to `WalletBalance` (EF Core `RenameColumn`).
- Migration **veri silmez**; loop 91 sonu zaten manuel reset yapılmış (DB tam sıfırla).

**Cash-flow invariance (futures):**
```
WalletBalance + AllocatedMargin + UnrealizedPnl = Equity (kabaca; gerçekte fee birikimi var)
```
ADR-0020 fee accounting korunur — `RealizedPnl = gross − EntryCommission − ExitCommission`; `WalletBalance` artımı close anında bu net'i alır.

### 25.10 PaperFillSimulator — Futures Margin Modeline Geçiş

**Karar:** `PaperFillSimulator` **mevcut spot mantığı ile silinir**, yerine `FuturesPaperFillSimulator` yazılır. CLAUDE.md §13 deprecated kod yasağı: spot fill simulator dosyası **kalmaz**.

Futures fill akışı:

**Open (entry):**
1. Filter validation (mevcut: tickSize, stepSize, minNotional, minQty/maxQty) — Futures'ta korunur.
2. Depth walking (mevcut FillMarket logic) — symbol agnostik, korunur.
3. **Margin hesabı:** `initialMargin = (qty × avgFillPrice) / leverage`.
4. **Wallet check:** `if (VirtualBalance.WalletBalance < initialMargin) → reject "insufficient_margin"`.
5. Commission (taker 0.04% futures; ADR-0020 fee abstraction `IFeeSimulator` ile config-driven).
6. `VirtualBalance.OpenPositionMargin(initialMargin, now)` — wallet → allocated transfer.
7. `Position.Open(symbol, Direction, qty, entryPrice, ...)` — Direction parametresi yeni.

**Close (exit, reverse leg):**
1. Aynı symbol + reverse OrderSide (Long position close = SELL; Short position close = BUY).
2. Depth walk + slippage hesabı (mevcut).
3. Commission exit-leg.
4. `realizedPnl = Direction == Long ? (exitPrice − entry) × qty : (entry − exitPrice) × qty − feeEntry − feeExit`.
5. `VirtualBalance.ClosePositionMargin(initialMargin, realizedPnl, now)` — allocated → wallet + PnL.
6. `Position.Close(exitPrice, reason, now, exitCommission)`.

**KEY DIFFERENCE vs Spot:** Spot'ta cash sürekli değişir (buy → cash azalır, sell → cash artar; round-trip = realized delta). Futures'ta margin **bloklanmış** kalır pozisyon açıkken; cash sadece **realized PnL** ile değişir close anında. Wallet invariance:
```
Spot:    WalletBefore_buy − cost  → WalletAfter_buy → +cost+pnl on sell  → Walletfinal = Walletinitial + RealizedPnl
Futures: WalletBefore_open − margin → WalletAfter_open → +margin+pnl on close → Walletfinal = Walletinitial + RealizedPnl
```
Net invariance aynı (final = initial + Σ realized), sadece intra-position davranış margin-bloked.

### 25.11 MarkToMarketWorker — Long/Short Aware

`MarkToMarketWorker.TickAsync` (mevcut: `Position.Side == Long` varsayımı bazı SL/TP/Trailing helper'larda implicit). Refactor:

**SL trigger:**
```csharp
var slTriggered = pos.Direction == TradeDirection.Long
    ? markPrice <= pos.StopPrice.Value
    : markPrice >= pos.StopPrice.Value;
```

**TP trigger:**
```csharp
var tpTriggered = pos.Direction == TradeDirection.Long
    ? markPrice >= pos.TakeProfit.Value
    : markPrice <= pos.TakeProfit.Value;
```

**BE move trigger:**
```csharp
var beReady = pos.Direction == TradeDirection.Long
    ? (markPrice / pos.AverageEntryPrice − 1m) >= beOpts.TriggerPct
    : (1m − markPrice / pos.AverageEntryPrice) >= beOpts.TriggerPct;
var newStop = pos.Direction == TradeDirection.Long
    ? pos.AverageEntryPrice * (1m + beOpts.OffsetPct)
    : pos.AverageEntryPrice * (1m − beOpts.OffsetPct);
```

**Trailing:** `Position.UpdatePeakAndCheckTrailing` aggregate-internal Direction aware — worker sadece markPrice + trailPct gönderir.

**Liquidation guard (yeni, futures-only):**
```csharp
var marginRatio = pos.UnrealizedPnl < 0
    ? Math.Abs(pos.UnrealizedPnl) / pos.AllocatedMargin
    : 0m;
if (marginRatio >= MaintenanceMarginRatio)  // %80 default
    dispatchClose(pos.Id, "margin_call");
```

### 25.12 Pattern Detector Genişlemesi — Simetrik 10 Yeni Detector

**Karar:** Mevcut 10 long-bias detector korunur; **10 yeni short-bias detector** eklenir. `PatternRegistry` 20 detector tutar.

| # | Long Detector (mevcut) | Short Detector (yeni) | Skor mantığı (short) |
|---|---|---|---|
| 1 | BullishEngulfingDetector | **BearishEngulfingDetector** | Prev bull + curr bear; curr body prev'i kapsıyor ⇒ 1.0; küçük body ⇒ 0.5. |
| 2 | HammerReversalDetector | **ShootingStarDetector** | Upper-shadow ≥ 2× body, lower küçük, üst %25 range içinde close ⇒ 1.0; zayıf ⇒ 0.5. |
| 3 | BollingerLowerReversalDetector | **BollingerUpperReversalDetector** | Close > BollingerUpper × (1−buffer) AND Rsi14 > 65 AND Rsi14 < Rsi14Prev ⇒ 1.0; sadece touch ⇒ 0.5. |
| 4 | BollingerSqueezeBreakoutDetector (long) | **BollingerSqueezeBreakDownDetector** | BBW past 6 bars min'i ≤ %0.4 + curr close < BollingerLower ⇒ 1.0; genişledi break yok ⇒ 0.5. |
| 5 | RsiOversoldRecoveryDetector | **RsiOverboughtPullbackDetector** | Rsi14 > 60 AND Rsi14 < Rsi14Prev ⇒ 1.0; 50-60 + falling ⇒ 0.5. |
| 6 | Ema9SlopeMomentumDetector (up) | **Ema9SlopeDownDetector** | Ema9 < Ema9Prev AND Close < Ema9 ⇒ 1.0; sadece slope ⇒ 0.5. |
| 7 | DonchianBreakoutDetector (high) | **DonchianBreakdownDetector** | Curr close < Donchian20Low ⇒ 1.0; close < prev Donchian + retest ⇒ 0.5. |
| 8 | VolumeSurgeConfirmDetector (HARD-GATE, ortak) | — (Long ve Short ortak) | Yön-agnostik. |
| 9 | SpreadGuardDetector (HARD-GATE, ortak) | — (Long ve Short ortak) | Yön-agnostik. |
| 10 | AdxRegimeDetector (ortak) | — (Long ve Short ortak) | Yön-agnostik. |

**Toplam:** 7 long-only + 7 short-only + 3 ortak hard-gate = **17 detector dosyası** (ortak 3 tek dosya, iki kovaya inject).

**Composer (`WeightedScorePatternComposer`) yeni davranış:**
1. Detector çıktısını yöne göre **iki ayrı bucket**'a topla (her detector `Direction` property exposeladı: `Long`, `Short`, `Neutral`).
2. Hard-gate (Volume + Spread) ortak; ikisinde de uygulanır.
3. AdxRegime ortak (regime soft skor).
4. `total_long = Σ(score × weight, direction in [Long, Neutral])`.
5. `total_short = Σ(score × weight, direction in [Short, Neutral])`.
6. Karar:
   - `total_long >= req && total_short < req` → emit `Long`.
   - `total_short >= req && total_long < req` → emit `Short`.
   - İkisi de >= req → `skip:both_directions_qualified`.
   - İkisi de < req → `skip:no_direction_qualified`.

**Ortak skor tavanı:**
- Long bucket max: 7 long detector × ağırlıklar + 3 ortak × ağırlıklar = ~11.5 (mevcut korunur).
- Short bucket simetrik.

**MTF Gate Direction aware:**
ADR-0024 §24.x dışında MTF gate `IPatternCompositeEvaluator` içinde post-emit filtre olarak çalışıyor (Loop 91 spec). Yeni:
```csharp
if (decision.Direction == TradeDirection.Long && ema21Slope15m < 0m) skip("mtf_against_long");
if (decision.Direction == TradeDirection.Short && ema21Slope15m > 0m) skip("mtf_against_short");
```
Slope threshold mevcut `< -0.001` strict — Long için `> +0.001` (mirror).

**Detector `Direction` property eklenir IPatternDetector interface'ine:**
```csharp
public interface IPatternDetector
{
    string Name { get; }
    decimal DefaultWeight { get; }
    bool IsHardGate { get; }
    PatternDirection Direction { get; }  // YENI: Long | Short | Neutral
    PatternEvaluation Evaluate(BarSnapshot snapshot);
}
public enum PatternDirection { Long = 1, Short = 2, Neutral = 3 }
```

### 25.13 RiskProfile Genişlemesi — Leverage + Margin + Funding

**Karar:** `RiskProfile` aggregate'ine 3 yeni alan eklenir:

```csharp
public sealed class RiskProfile : AggregateRoot<int>
{
    // ... mevcut: MaxOpenPositions, MaxPositionSizePct, MaxDailyLossPct, CircuitBreaker* ...
    public int Leverage { get; private set; }            // YENI default 1, max 3
    public decimal MaintenanceMarginRatio { get; private set; }  // YENI default 0.80 (=%80)
    public decimal MaxFundingFeePerHour { get; private set; }    // YENI default 0.001 ($)/saat — circuit
}
```

**Davranışlar:**
- `SetLeverage(int leverage)` — guard 1 ≤ x ≤ 3.
- Sizing handler `qty = (riskAmount × leverage) / slDistance` formülüne genişler. **DEFAULT 1x** — Loop 92 başlangıçta leverage genişlemesi YAPILMAZ; sadece schema hazır olur. Loop 93+ tune denenir.
- Funding rate threshold trip ⇒ tüm pozisyonlar kapanır + cooldown.

**Migration `AddRiskProfileFuturesFields`:**
- 3 kolon eklenir, default değerler backfill.
- HasData seed güncellenir (3 mode × default 1x leverage).

### 25.14 KlineWorker / FuturesUserDataStreamWorker

**Yeni BackgroundService'ler:**

1. `FuturesKlineWorker` (replace `BinanceKlineSubscriber` Spot WS): `wss://fstream.binancefuture.com/stream?streams=btcusdt@kline_1m/...`. Kline schema Futures'ta Spot ile **birebir aynı** — sadece base URL farklı.
2. `FuturesBookTickerWorker` (replace mevcut Spot subscriber): `wss://fstream.binancefuture.com/stream?streams=btcusdt@bookTicker/...`. Schema aynı.
3. `FuturesUserDataStreamWorker` (yeni — order/position eventleri): `POST /fapi/v1/listenKey` → `wss://fstream.binancefuture.com/ws/{listenKey}`. Yeni event'ler: `ACCOUNT_UPDATE` (margin/wallet), `ORDER_TRADE_UPDATE` (fill), `MARGIN_CALL` (liquidation warn). Polling 30dk (keepalive).
4. `FuturesFundingRateWorker` (yeni — 8h bir): `GET /fapi/v1/fundingRate?symbol=...&limit=1`. PaperFillSimulator + RiskProfile guard buradan beslenir.

`BinanceWsSupervisor` (Spot) **silinir** — futures-spesifik supervisor `FuturesWsSupervisor` (subscribe replay + reconnect + heartbeat aynı pattern).

### 25.15 DI / Composition Root

**`Api/Program.cs` yeni hat:**
```csharp
// Trading mode: Futures-only (Loop 92+)
builder.Services.Configure<BinanceOptions>(builder.Configuration.GetSection("Binance"));
builder.Services.AddHttpClient<IExchangeClient, BinanceFuturesClient>("binance-futures", ...)
    .AddPolicyHandler(...)
    .AddHttpMessageHandler<FuturesSignatureHandler>();

// Pattern detectors — 17 dosya (7 long + 7 short + 3 ortak)
builder.Services.AddSingleton<IPatternDetector, BullishEngulfingDetector>();
builder.Services.AddSingleton<IPatternDetector, BearishEngulfingDetector>();
// ... 17 satır
builder.Services.AddSingleton<IPatternRegistry, PatternRegistry>();
builder.Services.AddSingleton<IPatternSignalComposer, WeightedScorePatternComposer>();
builder.Services.AddSingleton<IStrategyEvaluator, PatternCompositeEvaluator>();

// Workers
builder.Services.AddHostedService<FuturesKlineWorker>();
builder.Services.AddHostedService<FuturesBookTickerWorker>();
builder.Services.AddHostedService<FuturesUserDataStreamWorker>();
builder.Services.AddHostedService<FuturesFundingRateWorker>();
builder.Services.AddHostedService<MarkToMarketWorker>();  // Direction-aware refactor
```

**`appsettings.json` (template):**
```jsonc
{
  "Binance": {
    "RestBaseUrl": "https://testnet.binancefuture.com",
    "WsBaseUrl": "wss://fstream.binancefuture.com",
    "AllowMainnet": false,           // ADR-0006 korunur
    "MainnetRestBaseUrl": "https://fapi.binance.com",
    "MainnetWsBaseUrl": "wss://fstream.binance.com"
  },
  "Trading": {
    "Mode": "Futures",                // sabit; Spot yok
    "DefaultLeverage": 1,
    "MarginType": "Isolated",
    "MaintenanceMarginRatio": 0.80
  }
}
```

`BinanceOptions` ApiKey + ApiSecret user-secrets'ta. Futures-spesifik: `recvWindow` query param desteği (Spot'ta 5000ms; Futures'ta da aynı).

### 25.16 Migration Sıralaması (5 Migration)

1. **`AddTradeDirectionToSignalAndPosition`**
   - `StrategySignals.Direction INT NOT NULL DEFAULT 1`
   - `Positions.Direction INT NOT NULL DEFAULT 1`
   - Backfill: tüm mevcut satırlar Direction=1 (Long).
   - Index `IX_Positions_Mode_Status_Direction` ekle.
   - Eski `Positions.Side` (PositionSide enum) **DROP** — artık Direction yetkilidir.

2. **`RenameVirtualBalanceCurrentToWallet`**
   - `Positions.PeakMarkPrice` → `Positions.ExtremeMarkPrice` rename.
   - `VirtualBalances.CurrentBalance` → `VirtualBalances.WalletBalance` rename.
   - Yeni kolonlar: `VirtualBalances.AllocatedMargin DECIMAL(28,10) NOT NULL DEFAULT 0`, `VirtualBalances.UnrealizedPnl DECIMAL(28,10) NOT NULL DEFAULT 0`.

3. **`AddRiskProfileFuturesFields`**
   - `RiskProfiles.Leverage INT NOT NULL DEFAULT 1`
   - `RiskProfiles.MaintenanceMarginRatio DECIMAL(10,4) NOT NULL DEFAULT 0.80`
   - `RiskProfiles.MaxFundingFeePerHour DECIMAL(28,10) NOT NULL DEFAULT 0.001`
   - HasData seed güncel.

4. **`Loop92FuturesPivotReset`**
   - `DELETE FROM OrderFills; DELETE FROM Orders; DELETE FROM Positions; DELETE FROM StrategySignals;` — DB tam sıfırla (Loop 91 sonu zaten manuel yapıldı, idempotent guard).
   - Strategy seed güncel: Direction-aware composer parametre defaultları (Long+Short req thresholds).
   - VirtualBalance Paper StartingBalance=$500 reset (mevcut).

5. **`AddFundingRateLedger`** (opsiyonel, Faz-2 — Loop 93+)
   - Yeni tablo: `FundingRatePayments(Id, PositionId, FundingRate, NotionalValue, FeeAmount, AppliedAt)`. Audit + RiskProfile threshold için trail. Loop 92'de **eklenmez** — `FuturesFundingRateWorker` log-only başlar.

**Sıralama disiplini:** Migration 1 → 2 → 3 → 4. EF tek `dotnet ef database update` ile sırayla uygulanır. CI/CD smoke test sırayı doğrular.

### 25.17 Test Stratejisi

| Katman | Test türü | Adet | Örnek |
|---|---|---|---|
| Domain | Position Long PnL/Trailing | 5 | Mevcut testler korunur, Direction=Long parametre eklenir. |
| Domain | Position Short PnL/Trailing/BE | 8 | Yeni: Short MarkToMarket pozitif/negatif, Short BE trigger asimetrik, Short trailing trough mantığı. |
| Domain | VirtualBalance OpenMargin/CloseMargin | 6 | wallet azalır → allocated artar; close PnL+ döner → wallet artar; PnL− → wallet eksilir. |
| Application | Composer Long bucket emit | 4 | Long detector setiyle threshold karşılanır → emit Long. |
| Application | Composer Short bucket emit | 4 | Short detector setiyle threshold karşılanır → emit Short. |
| Application | Composer both qualified skip | 2 | İki kovada da req aşıldı → emit yok. |
| Application | MTF gate Long/Short asymmetric | 4 | slope > 0 + Short skip; slope < 0 + Long skip. |
| Application | 7 yeni Short detector unit | 21 | Her detector full/partial/no-trigger. |
| Infrastructure | FuturesPaperFillSimulator open margin | 3 | initialMargin = (qty×price)/leverage; wallet check. |
| Infrastructure | FuturesPaperFillSimulator close PnL | 4 | Long+ Short+ Long− Short−. |
| Infrastructure | MarkToMarketWorker Short SL/TP | 4 | mark ≥ entry×(1+slPct) trigger Short; mark ≤ entry×(1−tpPct) trigger Short TP. |
| Infrastructure | BinanceFuturesClient REST mock | 6 | PlaceOrder, GetAccount, SetLeverage, SetMarginType, GetExchangeInfo, GetKlines. |
| Infrastructure | FuturesWsSupervisor reconnect | 3 | reviewer-ws-resiliency skill ile denetim. |
| End-to-end | Playwright (tester) | 1 | Loop 92 boot 30dk içinde ≥3 emit (en az 1 Short), dashboard Long/Short badge gösterimi. |

---

## Consequences

### Pozitif

1. **Çift yönlü emit ⇒ frekans hedefi gerçekleşir:** Long + Short paralel emit pencereleri 30+/h'i kolay tutturur (Loop 91 3 trade/h iki katına çıkar minimum). CLAUDE.md §12 kuralı sağlanır.
2. **Downtrend sinemada kâr:** Loop 86-91 boyunca 4 büyük downtrend dilimi vardı; long-only bot bunları "0 emit" ile geçti. Short bucket bu pencereleri yakalar.
3. **Mainnet hazırlığı:** Mainnet trading kullanıcı vizyonunda Futures-bias (`trading_vision.md` "kartopu kar" leverage olmadan zayıf). Spot mainnet → Futures mainnet geçişi mimari çatlağı olur; pivot şimdi yapılır, mainnet zamanı değişiklik dondurulur.
4. **IExchangeClient port ⇒ exchange-agnostic:** OKX, Bybit ileride port impl ekleyerek bağlanır. Strategy pattern temiz.
5. **Composer simetrisi ⇒ pattern setine genişlemeye hazır:** Long-only bias dışında, "yön-agnostik regime" detector'lar ortak kovaya yazılır (Volume, Spread, ADX). Yeni regime detector eklemek tek dosya.
6. **Spot kodu silinir ⇒ teknik borç temizlenir:** Loop 80 ADR-0024 KMS+BBR temizliği gibi, Loop 92 Spot endpoints + supervisor + spot-specific simulator silinir. CLAUDE.md §13 uyumu.
7. **PnL formula simetrisi domain'e gömülür:** Position aggregate Direction biliyor; UnrealizedPnl/RealizedPnl/Trailing davranışı yön-aware. Anemic model riski yok.

### Negatif / Tradeoff

1. **Schema breaking değişiklikler:** 4 migration (Direction ekle, kolon rename, RiskProfile genişle, full reset). Manuel rollback imkansız (Direction kolon sil = data loss). Mitigation: `Loop92FuturesPivotReset` migration tüm tablo veri silici → fresh start; rollback "yeniden boot" anlamı.
2. **Spot kodu silmek geri dönüş zorlar:** Spot mainnet seçenek istense kod yeniden yazılır. Kullanıcı talebi net: Futures-only — geri dönüş düşük. CLAUDE.md §13 deprecated kod yasağı bunu emrediyor.
3. **Futures testnet realism:** Testnet likidite spot'tan daha az; bookTicker spread mainnet'e göre %3-5 daha geniş. PaperFillSimulator slippage ayarı Loop 92 ilk loop'larda re-tune gerektirebilir. Mitigation: ADR-0023 SL clip %0.30 hâlâ aktif → max single-trade kayıp $0.30.
4. **Funding fee yeni risk vektörü:** Long pozisyon yüksek funding rate'de ücret öder (8h bir). Loop 92 başlangıçta 1x leverage + small position size → fee impact ihmal edilebilir; ama loop 93+ leverage tune'da kritik. RiskProfile.MaxFundingFeePerHour guard Loop 92 default 0.001 — koruma sertlikte.
5. **Test yükü 2× artar:** Long+Short simetrik test gerekir (60+ yeni test). Reviewer çıktı denetimi yoğunlaşır. Mitigation: parametrik test (xUnit `[Theory]` Direction parametresi).
6. **Composer "both qualified skip" kuralının frekans maliyeti:** Konflüent yön belirsizliği skip → emit kaybı. Kabul edilebilir (yanlış yön daha pahalı). Loop 92-95'te ölçülür; "skip:both" oranı %10'u aşarsa composer karar logic re-tune.

### Nötr

1. ADR-0023 R:R 1:2.5 asymmetry korunur — TP/SL ATR multiplier aynı, sadece yön ters.
2. ADR-0024 Pattern subsystem altyapısı korunur (BarSnapshot, IPatternDetector port, PatternCompositeEvaluator, IPatternRegistry); sadece detector seti çiftlenir + composer Direction-aware.
3. ADR-0008 TradingMode enum şeması korunur (3 değer); Paper aktif, LiveTestnet/LiveMainnet halen mevcut sözleşmede (reject + SystemEvent kaydı).
4. ADR-0020 fee accounting korunur (commission entry+exit ledger); sadece taker rate spot 0.1% → futures 0.04% (config).
5. `MarkToMarketWorker`, `ICooldownService`, `StrategySignalToOrderHandler` fan-out — sınıf isimleri korunur, içerik refactor.
6. Frontend Vue dashboard — yeni `Direction` badge (Long: yeşil, Short: kırmızı) eklenir; UI değişimi minimal (ADR-0024'te listede 5 strateji vardı; Direction kolonu eklenir).

---

## Alternatifler

### A. Spot mainnet'e geçip Long-only optimize etmek

Spot mainnet üzerinde 12 loop daha tune. **Reddedildi:**
- Long-only sermayenin doğal asimetrisi (downtrend = ya 0 emit ya zarar) çözülmez.
- Mainnet'te 12 loop test = $50-200 sermaye yakma riski (testnet bedava).
- Vision (`trading_vision.md`) "kartopu kar" leverage'sız Spot'ta yavaş; Futures default 1x bile sembolik mainnet hazırlığı.

### B. Spot + Futures paralel yaşat (Strategy Pattern iki concrete impl)

`BinanceSpotClient` + `BinanceFuturesClient` aynı solution'da; `Trading:Mode` enum runtime select. **Reddedildi:**
- CLAUDE.md §13 deprecated kod yasağı ihlali — Spot kodu kullanılmıyor, yaşatmak teknik borç.
- DI runtime branching karmaşık (her worker mode-dispatch); composition root statik registration daha net.
- Test yükü 2× (Spot path + Futures path her ikisini de doğrula). Maliyet faydadan büyük.

### C. Detector yön-agnostik tek liste, composer karar

Tek detector seti; her detector hem long hem short emit edebilir (`PatternEvaluation.Direction` field). **Reddedildi:**
- Detector mantığı **doğal olarak yön-bias** (BullishEngulfing inherently long, ShootingStar inherently short). Tek detector iki yön döndürmek SRP ihlali.
- Composer karar mantığı bulanıklaşır — hangi detector hangi yöne katkı verdi izleme zorlaşır.
- 17 detector dosyasının (7+7+3) okunabilirliği daha yüksek (her dosya tek niyet).

### D. Sadece Short bucket ekle, Long mevcut Spot ile devam

Spot kalır; Short için Futures USDT-M; karma sistem. **Reddedildi:**
- İki ayrı broker/wallet/credentials = composition root patlaması.
- Spot ve Futures hesap ayrı (reconciliation imkansız tek bot session içinde).
- Mainnet senaryosu absürd (kullanıcı iki broker yönetir).

### E. ML-tabanlı yön karar (LSTM / Transformer)

Detector'ları feature; ML model yön + yoğunluk emit eder. **Reddedildi (şimdilik):**
- ADR-0024'te de aynı gerekçeyle reddedilen yaklaşım — veri yetersiz (Loop 92 başlangıçta total 107 trade, ML training için <1000 örnek).
- Loop 92-100 manuel composer ile veri biriktir; sonra ADR-0026 olarak ML yönlendirme ele alınır.

### F. Futures Long-only, Short faz-2

Migration sadece Spot → Futures Long; Loop 95+ Short ekle. **Reddedildi:**
- Yarı-pivot iki migration döngüsü; CLAUDE.md §13 deprecated kod yasağı yine ihlal (geçiş anında Direction enum eklenir, kullanılmaz).
- "12 loop -$17 kaybı" çözümü gecikiyor — frekans + downtrend sorunu hâlâ duruyor.
- Pivotun manası "2 yönlü kazanım"; tek yönlü Futures Spot'a göre sadece "leverage" katkısı sunar, asıl sorunu çözmez.

---

## Migration Notları

1. **Sıralı migration uygulaması:** `dotnet ef database update --project Infrastructure --startup-project Api` tek seferde 4 migration uygular (1→2→3→4). Migration 4 `Loop92FuturesPivotReset` veri siler — Loop 91 sonu zaten temiz, idempotent.
2. **Bot restart CB reset etmez** (`reference_circuit_breaker_reset.md`). Deploy sonrası `POST /api/risk/circuit-breaker/reset` (X-Admin-Key) loop boot script'ine dahil.
3. **Futures testnet hesabı setup:** `https://testnet.binancefuture.com/` üzerinde anahtar oluştur (Spot testnet anahtarı işe yaramaz — ayrı testnet domain). User-secrets güncel:
   ```
   dotnet user-secrets set "Binance:ApiKey" "<futures-testnet-key>"
   dotnet user-secrets set "Binance:ApiSecret" "<futures-testnet-secret>"
   ```
4. **Initial position size ayarı:** Futures'ta 1x leverage notional = (qty × price); Spot ile aynı sermaye %1 risk per trade kuralı korunur. Margin requirement = notional / leverage = notional (1x). Wallet $500 → max 5 simultaneous position × ~$100 notional.
5. **WS endpoint farkı:**
   - Spot: `wss://stream.binance.com:9443/stream` (silindi)
   - Futures testnet: `wss://stream.binancefuture.com/stream`
   - Futures mainnet: `wss://fstream.binance.com/stream`
6. **REST endpoint farkı:**
   - Spot order: `/api/v3/order`
   - Futures order: `/fapi/v1/order`
   - SetLeverage: `POST /fapi/v1/leverage` (Spot'ta yok)
   - SetMarginType: `POST /fapi/v1/marginType`

---

## Implementation Order — backend-dev için 14 Atomik Commit

Her satır ayrı commit (mantıksal sıralama; tek `development` branch CLAUDE.md §10).

1. **Domain — TradeDirection enum + StrategySignal.Direction + Position.Direction.**
   - `Domain/Common/TradeDirection.cs` (enum + extensions).
   - `Domain/Strategies/StrategySignal.cs`: Direction property + `Emit(...)` factory parameter.
   - `Domain/Positions/Position.cs`: `Side` (PositionSide value object) **silinir**, `Direction : TradeDirection` eklenir; `Open/MarkToMarket/MoveStopToBreakEven/UpdatePeakAndCheckTrailing/Close` Direction-aware refactor. `PeakMarkPrice` → `ExtremeMarkPrice` rename.
   - Test: `tests/Tests/Domain/Positions/PositionShortTests.cs` yeni dosya (8 test); mevcut `PositionTests.cs` Long tests Direction parametresi alır.

2. **EF Migration — `AddTradeDirectionToSignalAndPosition`.**
   - `Infrastructure/Persistence/Configurations/PositionConfiguration.cs` Direction kolon + `IX_Positions_Mode_Status_Direction` index, eski `Side` mapping silinir.
   - `Infrastructure/Persistence/Configurations/StrategySignalConfiguration.cs` Direction kolon.
   - Backfill SQL: `UPDATE Positions SET Direction = 1; UPDATE StrategySignals SET Direction = 1;`.
   - `dotnet ef migrations add AddTradeDirectionToSignalAndPosition`.

3. **Application — IExchangeClient port refactor.**
   - `Application/Abstractions/Exchange/IExchangeClient.cs` (yeni — 8 method).
   - Eski `Application/Abstractions/IBinanceTrading.cs` **silinir** (içeriği IExchangeClient'a taşındı).
   - Eski `BinanceMarketDataClient` interface'i (varsa Application'da) silinir; market data IExchangeClient'a entegre.
   - `PlaceOrderRequest` DTO'ya `TradeDirection Direction` eklenir.

4. **Infrastructure — BinanceFuturesClient REST iskelet.**
   - `Infrastructure/Binance/BinanceFuturesClient.cs` — IExchangeClient impl, `/fapi/v1/order`, `/fapi/v1/account`, `/fapi/v1/exchangeInfo`, `/fapi/v1/leverage`, `/fapi/v1/marginType` endpoint mappings.
   - `Infrastructure/Binance/Handlers/FuturesSignatureHandler.cs` — futures-spesifik signing (Spot ile aynı HMAC-SHA256 ama header `X-MBX-APIKEY` aynı format). Mevcut `BinanceSignatureHelper` reuse.
   - `BinanceTradingClient.cs` (Spot) **silinir**.
   - `BinanceMarketDataClient.cs` (Spot) **silinir**.
   - Test: `tests/Tests/Infrastructure/Binance/BinanceFuturesClientTests.cs` (6 mock HTTP test).

5. **Infrastructure — Futures WS supervisor.**
   - `Infrastructure/Binance/Streams/FuturesWsSupervisor.cs` — reconnect + heartbeat + replay (mevcut Spot supervisor pattern reuse).
   - `Infrastructure/Binance/Streams/FuturesStreamParser.cs` — kline + bookTicker + userData event parse.
   - `Infrastructure/Binance/Streams/FuturesUserDataStreamWorker.cs` — listenKey lifecycle + ACCOUNT_UPDATE + ORDER_TRADE_UPDATE.
   - Eski `BinanceWsSupervisor.cs` + `BinanceStreamParser.cs` + `BinanceStreamBus.cs` **silinir veya rewrite edilir** (BinanceStreamBus exchange-agnostik kalabilir; Spot-spesifik kısımları temizle).

6. **Infrastructure — FuturesPaperFillSimulator (Spot'a replace).**
   - `Infrastructure/Trading/Paper/FuturesPaperFillSimulator.cs` — IPaperFillSimulator impl; margin akışı (open: wallet → allocated; close: allocated → wallet + PnL).
   - Eski `Infrastructure/Trading/Paper/PaperFillSimulator.cs` **silinir**.
   - Test: `tests/Tests/Infrastructure/Trading/FuturesPaperFillSimulatorTests.cs` (7 test: Long open/close +/-, Short open/close +/-, insufficient margin reject).

7. **Domain — VirtualBalance Futures genişlemesi.**
   - `Domain/Balances/VirtualBalance.cs` — `WalletBalance/AllocatedMargin/UnrealizedPnl` field; `OpenPositionMargin/ClosePositionMargin/ApplyUnrealized/ApplyFundingFee` davranış. Eski `ApplyFill` silinir.
   - `Domain/Balances/Events/` — `PositionMarginAllocatedEvent`, `PositionMarginReturnedEvent`, `FundingFeeAppliedEvent`.
   - Test: `tests/Tests/Domain/Balances/VirtualBalanceFuturesTests.cs` (6 test).

8. **EF Migration — `RenameVirtualBalanceCurrentToWallet`.**
   - `VirtualBalances.CurrentBalance` rename `WalletBalance`; `AllocatedMargin/UnrealizedPnl` kolonlar.
   - `Positions.PeakMarkPrice` rename `ExtremeMarkPrice`.
   - `Infrastructure/Persistence/Configurations/VirtualBalanceConfiguration.cs` güncel.

9. **Infrastructure — MarkToMarketWorker Direction-aware.**
   - `Infrastructure/Positions/MarkToMarketWorker.cs` — SL/TP/BE/Trailing Direction-aware (yukarıda §25.11 kod örneği).
   - Liquidation guard ekle (marginRatio).
   - Test: `tests/Tests/Infrastructure/Positions/MarkToMarketWorkerShortTests.cs` (4 test: Short SL/TP/BE/Trailing trigger).

10. **Application — 7 yeni Short pattern detector.**
    - `Application/Strategies/Patterns/BearishEngulfingDetector.cs`
    - `Application/Strategies/Patterns/ShootingStarDetector.cs`
    - `Application/Strategies/Patterns/BollingerUpperReversalDetector.cs`
    - `Application/Strategies/Patterns/BollingerSqueezeBreakDownDetector.cs`
    - `Application/Strategies/Patterns/RsiOverboughtPullbackDetector.cs`
    - `Application/Strategies/Patterns/Ema9SlopeDownDetector.cs`
    - `Application/Strategies/Patterns/DonchianBreakdownDetector.cs`
    - `IPatternDetector` interface'ine `PatternDirection Direction { get; }` eklenir; mevcut 10 detector için Direction property = Long (7 long-bias) veya Neutral (3 ortak hard-gate / regime).
    - Test: 7 × 3 senaryo = 21 unit test.

11. **Application — WeightedScorePatternComposer Direction-aware.**
    - `Application/Strategies/Patterns/WeightedScorePatternComposer.cs` iki-kova logic (§25.12).
    - `CompositeSignalDecision.Direction : TradeDirection` field eklenir (skip durumunda null).
    - Test: 4 emit + 2 skip + 4 MTF gate = 10 yeni test.

12. **Application — PatternCompositeEvaluator MTF gate Direction-aware.**
    - `Application/Strategies/Patterns/PatternCompositeEvaluator.cs` — composer çıktısı Direction'a bakıp MTF gate Long/Short asymmetric uygular (§25.12 kod).
    - StrategyEvaluation çıktısı StrategySignal.Direction'a propagate.

13. **Infrastructure + Api — DI Composition Root + appsettings güncel.**
    - `Api/Program.cs` Futures-only DI (§25.15).
    - `appsettings.json` template (§25.15) — Trading:Mode=Futures.
    - `appsettings.Development.json` Futures testnet RestBaseUrl.
    - Eski Spot `BinanceOptions` flag'leri (varsa) silinir.

14. **EF Migration — `AddRiskProfileFuturesFields` + `Loop92FuturesPivotReset` + Frontend Direction badge + tester Playwright.**
    - `Infrastructure/Persistence/Configurations/RiskProfileConfiguration.cs` Leverage/MaintenanceMarginRatio/MaxFundingFeePerHour kolon + HasData seed.
    - `dotnet ef migrations add AddRiskProfileFuturesFields`.
    - `dotnet ef migrations add Loop92FuturesPivotReset` — DB sıfırla migration.
    - `wwwroot/js/components/PositionList.js` (veya equivalent Vue CDN component) — Direction badge (Long: yeşil arrow-up, Short: kırmızı arrow-down).
    - Tester agent Playwright senaryo: boot → 30dk → ≥3 emit (en az 1 Short) → screenshot dashboard.

**Reviewer kontrol noktaları:**
- ADR-0006 testnet-first guard hâlâ aktif (`AllowMainnet=false`).
- Spot kodu 0 dosya kalmış (grep `BinanceSpot`, `api/v3/order`, `binance.com:9443` → 0 sonuç).
- `IBinanceTrading` reference 0 kalmış (port refactor tam).
- `Position.Side` reference 0 kalmış (Direction migration tam).
- Futures fee rate (0.04% taker) ADR-0020 fee abstraction üzerinden config-driven, hard-coded değil.
- Test coverage: Domain Short %85+, Application composer %90+, Infrastructure FuturesPaperFillSimulator %85+.

**Tester agent done-definition:**
- Loop 92 boot 30dk içinde:
  - ≥3 emit (composer Long+Short bucket çalışıyor).
  - En az 1 Short emit (Direction=2 satırı StrategySignals'da).
  - Open positions tablosunda Direction kolonu rendered.
  - VirtualBalance dashboard'unda WalletBalance + AllocatedMargin + UnrealizedPnl ayrı görünüyor.
  - 0 unhandled exception (logs).
  - 0 ADR-0006 mainnet guard ihlali.

---

## Kaynak

- ADR-0006 (testnet-first-policy) — mainnet guard korunur.
- ADR-0008 (trading-modes) — TradingMode enum semantik korunur, Spot endpoint silinir.
- ADR-0020 (fee-aware-paper-accounting) — fee abstraction futures rate'e config-driven adapte edilir.
- ADR-0023 (risk-first-tp-sl-asymmetry) — R:R 1:2.5 + SL clip korunur.
- ADR-0024 (pattern-based-scalping) — pattern subsystem tamamen korunur, detector seti çiftlenir.
- Memory `trading_vision.md` — 5-10 chart pattern detector + ağırlıklı sinyal + kartopu vizyonu.
- Memory `feedback_frekans_kartopu.md` — 30+/h frekans kuralı, çift yönlü emit ile gerçekleşir.
- Memory `feedback_no_dead_code.md` — Spot kodu silme zorunluluğu (CLAUDE.md §13).
- Memory `feedback_no_session_split.md` — uniform 24/7; Direction simetrisi saat-agnostik.
- [Binance USDT-M Futures REST API](https://binance-docs.github.io/apidocs/futures/en/) — endpoint reference.
- [Futures Testnet](https://testnet.binancefuture.com/) — ayrı API key + ayrı WS host.
- [DDD reference: Vaughn Vernon — Implementing DDD §10](https://www.amazon.com/Implementing-Domain-Driven-Design-Vaughn-Vernon/dp/0321834577) — aggregate-internal value object (PositionSide) silmenin gerekçesi (concept duplication).
- Loop 80-91 raporları — long-only zarar tarihi (loops/loop_80..91/check-tNN.md).
- User request (2026-05-03): "Spot → Futures Long+Short pivot, 12 loop −$17 sonrası".
