# 0020. Fee-Aware Paper Accounting + Cash-Symmetric PaperFillSimulator

Date: 2026-04-22
Status: Proposed
Relates to: [ADR-0008 Trading Modes](./0008-trading-modes.md), [ADR-0011 Equity-Aware Sizing](./0011-equity-aware-sizing-and-risk-tracking.md), [ADR-0018 Micro-Scalping 30s VWAP Reclaim](./0018-micro-scalping-30s-vwap-reclaim.md), [ADR-0019 Per-Coin Parameters + Capital 10x](./0019-per-coin-parameters-capital-10x.md)
Impacts: `Domain/Positions/Position.cs`, `Infrastructure/Trading/Paper/PaperFillSimulator.cs`, `Application/Portfolio/Queries/GetPortfolioSummary`, `Infrastructure/Persistence/Migrations/*`

> Bu ADR üç eksende karar verir: (A) **domain sorumluluk genişletme** — `Position` aggregate artık `EntryCommission` + `ExitCommission` alanlarını (quote-denominated) taşır; `Close(exitPrice, reason, exitCommission, now)` imzası fee'yi `RealizedPnl`'e net'ler. (B) **cash-symmetric simulator** — `PaperFillSimulator.FillMarket` her fill için `cash_delta = side_sign * price * qty - commission_quote` uygular; BUY fee'si ghost olmaz, cash'ten quote-equivalent düşer, `Position.Quantity` tam qty kalır. (C) **commission ledger görünümü** — `PortfolioSummaryDto.TotalCommissionPaid` artık `SUM(Position.EntryCommission + ExitCommission)` (saf quote USDT) okur; mixed-currency `OrderFills.Commission` SUM'u ledger/denetim amacıyla saklı kalır ama UI'da gösterilmez. Yeni aggregate yok, yeni domain event yok, sermaye $100 sabit — sadece domain alanı, handler imza genişletme, migration, simulator cash path değişir.

## Context

### 20.1 Tanı (loops/loop_32/diagnosis-pnl-discrepancy.md)

Loop 32 kapalı-pozisyonlar tablosu PnL toplamı `−$0.0109` iken portfolio summary `netPnl=+$0.52` gösterdi. DB doğrulaması sonrasında invariant kırığı matematiksel olarak izole edildi:

```
Beklenen  cash = starting + sum(realized_closed) - sum(open_cost_basis)
              = 100 + (-0.0109) - 15.7581
              = 84.2310
Gerçek    cash = 84.1301
Fark           = 0.1009 USD (25 round-trip üzerine dağılmış)
```

`trueEquity - startingBalance = +0.4164` (cash-based), `netPnl (gross component) = +0.5173` — aynı `$0.1009` farkı. Tek kaynaklı bug.

### 20.2 Root Cause — Fee Asimetrisi

`PaperFillSimulator.cs:170-190` her fill için:

```csharp
if (order.Side == OrderSide.Buy) {
    // base commission taken from received base (doesn't affect cash)
    realizedCash -= f.Price * f.Quantity;        // fee cash'e değmez
}
else {
    realizedCash += (f.Price * f.Quantity) - commission;  // fee cash'ten düşer
}
```

Sonuçlar:

1. **BUY tarafı fee ghost** — `OrderFill.Commission`'a base asset (BTC/ETH/BNB/XRP) birimiyle yazılır. `VirtualBalance.CurrentBalance`'a hiç dokunmaz. `Position.Quantity` **tam qty** olarak kaydedilir (örn. 0.00007 BTC) — gerçek Binance'in 0.00006993 BTC verip 0.00000007 BTC fee tuttuğu davranışın tersine.
2. **SELL tarafı fee tam düşer** — quote (USDT) olarak hem `OrderFill.Commission` hem `CurrentBalance` etkilenir.
3. **`Position.Close` (line 151-153) gross** — `RealizedPnl = (exit - entry) * qty`, fee yok.
4. **`GetPortfolioSummaryQuery` eski yorum yalan** — "commissions already netted into RealizedPnl" iddiası kod kanıtıyla yanlış. Loop 32 Fix-A sonrası `netPnl = trueEquity - startingBalance` tek-satır yaması bu tutarsızlığı UI katmanında gizledi ama domain + cash arasındaki sapma kökünde duruyor.

Round-trip başına toplam kaybolan muhasebe: `BUY_notional * 0.001` (VIP0) veya `BUY_notional * 0.00075` (BNB discount). 25 trade × avg $4.03 notional × 0.001 ≈ $0.1008 — gözlenen sapma birebir uyuyor.

### 20.3 Mixed-Currency Aggregation Sorunu

`SUM(OrderFills.Commission)` BUY fee'sini base asset (farklı coin'ler), SELL fee'sini quote USDT cinsinden **aynı numeric kolona** topluyor. `totalCommissionPaid = 0.1199` değeri ne USDT ne tutarlı bir birim — UI "fee ödendi" göstergesi olarak teknik doğru değil. BNB fee ≈ $0.63, BTC fee ≈ $75806, XRP fee ≈ $1.42 — doğrudan toplanamaz.

### 20.4 Neden Şimdi?

ADR-0019'da sermaye `$100` seviyesinde sabitlendi (Loop 32 briefing'inde $100 net karar). Loop 32 gözlemi 25 trade sonrası sapma net — daha fazla trade birikince DB/UI tutarsızlığı katlanarak büyür. Reviewer `$100 starting` + `$0.10 ghost fee` bias'ı yüzünden reform ölçütlerini (net kar kontrolü) doğrulayamaz. Fee-aware domain kararı geciktirilemez.

## Decision

### 20.5 Yaklaşım Seçimi — Opsiyon Analizi

Üç alternatif değerlendirildi (§20.11):

| Alt | BUY fee yeri | qty davranışı | Cash symmetry | Gerçekçilik | Seçim |
|---|---|---|---|---|---|
| **A** | Cash'ten quote düş | Tam qty | EVET | Yaklaşık (exchange base düşürür) | **KABUL** |
| B | Base'den qty düş | Fee kadar azaltılmış qty | Parça — base fee VirtualBalance'tan düşmez, cash tam kalır | Exchange davranışına bire bir | Reddedildi |
| C | Fee'siz paper | Tam qty | Kırık kalır (kendi içinde tutarlı ama fee drag yok) | Üretime hazır değil | Reddedildi |

**Seçim:** **Opsiyon A — BUY fee cash'ten quote-equivalent düş, qty tam kalsın.**

Gerekçeler:
- Cash-symmetric invariant (`starting + sum(realized) - sum(open_cost_basis) = cash`) tek adımda restore olur.
- `Position.Quantity` matematik iç tutarlılığı korur (entry notional = qty × entry price — yan etki yok).
- Invariant hesapları, sizing hesapları, stop/take-profit hesapları fee düşüşünden etkilenmez.
- Gerçek Binance davranışından sapma: BUY sonrası paper'da 0.00007 BTC, mainnet'te 0.00006993 BTC — fee drag **cash'e** bindirildi. Üretim geçişinde (ADR-0008 LiveTestnet / LiveMainnet) `OrderFill.Commission` exchange'den geldiği için simulator path devre dışı kalır; paper ile live arasındaki bu `$0.00000007 BTC` sapma production'da **yoktur**.

Opsiyon B reddi: base'den qty azaltırsak `Position.Open` çağrısından önce fee hesabı Infrastructure'dan Domain'a sızar; Position aggregate kendi quantity'sinin nasıl belirlendiğini bilemez (sizing service ve fill simulator arasında ince sözleşme kırılır). Ayrıca SELL tarafı zaten cash-side düştüğü için mimari asimetri başka yerde belirir (`AddFill` yerine `AddFillMinusBaseFee`). Opsiyon C reddi: fee drag fiili kar/zarar realizmini öldürür — Loop 31+ "net kar" kararları için fee-dahil ölçüm şart.

### 20.6 Domain Değişikliği — `Position` Aggregate

`Position` aggregate iki yeni alan alır (quote-denominated, non-null, default `0m`):

```
decimal EntryCommission { get; private set; }   // quote (USDT) cinsinden, açılış fee'si
decimal ExitCommission  { get; private set; }   // quote (USDT) cinsinden, kapanış fee'si
```

**Konum:** `src/Domain/Positions/Position.cs` — aggregate root invariantı, başka aggregate'ten yazılamaz.

**`Open` fabrikası (breaking parameter addition, default destekler):**

```
static Position Open(
    Symbol symbol,
    PositionSide side,
    decimal quantity,
    decimal entryPrice,
    decimal entryCommission,    // YENİ — quote cinsinden, >= 0
    decimal? stopPrice,
    long? strategyId,
    TradingMode mode,
    DateTimeOffset now,
    decimal? takeProfit = null,
    TimeSpan? maxHoldDuration = null)
```

Invariant: `entryCommission >= 0m` (negative fee yok). `entryCommission == 0m` LiveMainnet blocked-stub ve test path'leri için izinli.

**`AddFill` genişletme:**

```
void AddFill(decimal addQuantity, decimal addPrice, decimal addCommission, DateTimeOffset now)
```

`EntryCommission += addCommission`. Fee ağırlıklı ortalama değil, toplam — kümülatif muhasebe.

**`Close` imza değişikliği:**

```
void Close(decimal exitPrice, string reason, decimal exitCommission, DateTimeOffset now)
```

Davranış:

```
ExitCommission = exitCommission
RealizedPnl = Side == Long
    ? (exitPrice - AverageEntryPrice) * Quantity - EntryCommission - ExitCommission
    : (AverageEntryPrice - exitPrice) * Quantity - EntryCommission - ExitCommission
```

`PositionClosedEvent.RealizedPnl` artık **net** değeri taşır. Aggregate invariant: `entryCommission >= 0 && exitCommission >= 0`.

### 20.7 Infrastructure Değişikliği — `PaperFillSimulator` Cash-Symmetric

`FillMarket` fee bloğu (§170-190) şu hale gelir:

```
foreach (var f in fills) {
    var (quoteFee, reportedCommission, commissionAsset) =
        ComputeCommissionV2(order.Side, f.Price, f.Quantity, instrument, _options.UseBnbFeeDiscount);

    order.RegisterFill(tradeId, f.Price, f.Quantity, reportedCommission, commissionAsset, now);

    // Cash-symmetric: BUY ve SELL aynı formül
    var signedNotional = order.Side == OrderSide.Buy
        ? -f.Price * f.Quantity
        : +f.Price * f.Quantity;
    realizedCash += signedNotional - quoteFee;   // her zaman quote fee düş
}
```

- `quoteFee` → her iki yönde USDT cinsinden hesaplanır (`PaperFeeSimulator.CalculateCommission(notional, bnbDiscount)`).
- `reportedCommission` + `commissionAsset` → ledger backward compatibility: BUY tarafında base asset cinsinden `quoteFee / price`, SELL tarafında quote asset olduğu gibi yazılır. Binance fill-report şekli korunur (ADR-0008 §8.2 idempotency kontratı).

**Handler/Command bağlamı:** `PaperFillOutcome` yeni alan taşır:

```
record PaperFillOutcome(..., decimal RealizedCashDelta, decimal QuoteCommissionTotal)
```

`OpenPaperPositionCommand` / `ClosePaperPositionCommand` handler'ları bu `QuoteCommissionTotal` değerini `Position.Open(entryCommission: ...)` / `Position.Close(exitCommission: ...)` çağrılarına geçirir. Partial fill senaryosunda (MARKET IOC) her fill leg'inin quote fee'si toplanır.

### 20.8 Application Layer — `GetPortfolioSummaryQuery`

Tek değişiklik `TotalCommissionPaid` kaynağı:

```csharp
// Commission source — Position.EntryCommission + ExitCommission (quote USDT only).
// ADR-0020 öncesi OrderFills.Commission SUM mixed-currency (BUY base + SELL quote)
// olduğu için UI yanıltıcıydı; artık aggregate-level fee ledger kullanılır.
var totalCommission = await _db.Positions
    .AsNoTracking()
    .Where(p => p.Mode == mode)
    .SumAsync(p => (decimal?)(p.EntryCommission + p.ExitCommission), ct) ?? 0m;
```

`OrderFills.Commission` kolonu ledger-audit için korunur (binance-expert reviewer skill'i buna bakar), UI'da `NetProfitAfterFees` artık `RealizedPnlAllTime + UnrealizedPnlTotal` (her ikisi de fee-dahil: realized §20.6 net, unrealized entry fee sunk cost sayılır) veya `trueEquity - startingBalance` tek kaynak — handler yorumundaki yalan kaldırılır.

`NetProfitAfterFees` artık `RealizedPnlAllTime`'ın tam kendisi (kapalı işlem fee'leri domain tarafında net'lenmiş). `NetPnl = trueEquity - startingBalance` korunur (Fix-A sonrası tek doğruluk kaynağı); domain net + cash invariant bir araya geldiğinde `RealizedAllTime + UnrealizedTotal ≡ NetPnl` **matematiksel kimlik** olur (reviewer invariant-check testi).

### 20.9 Sermaye Kuralı — $100 Sabit (Değişmez)

ADR-0008 §8.x + Loop 30 brief + Loop 32 briefing: **paper başlangıç bakiyesi $100 USDT sabit**. Fee modeli değişse de `VirtualBalance.StartingBalance = 100` aynı kalır. Bu ADR **hiçbir sermaye artışı getirmez**; yalnızca var olan `$100`'ün **nereye gittiğinin** muhasebesini düzeltir.

Cash-symmetric fix sonrası beklenen durum (diagnosis sayılarıyla):

- Açılan 25 trade'in toplam BUY notional ≈ $100.85
- BNB discount açıksa toplam fee ≈ $100.85 × 0.00075 × 2 = $0.1513 (round-trip)
- Fee düzeltmeyle `RealizedPnl_net ≈ -$0.0109 - $0.1513 = -$0.1622` (net kayıp artar, gerçek)
- `trueEquity` gross→net kayar, `netPnl` negatif bölgede raporlanır — fakat **tutarlı**

Bu "kötü haber" gerçek davranıştır; reviewer + tester Loop 33+'ta stratejinin fee drag'ini aşamadığını net görür → parametre reform zemini oluşur.

### 20.10 Geriye Dönük Migration — 25 Kapalı + 3 Açık Pozisyon

**Karar:** Migration EF Core Code First şema değişikliği yapar; mevcut veri için **best-effort quote-equivalent backfill** uygulanır.

Migration adımları (`Infrastructure/Persistence/Migrations/<timestamp>_AddPositionCommissions`):

1. `Positions.EntryCommission` + `Positions.ExitCommission` kolonları eklenir — `decimal(18, 8) NOT NULL DEFAULT 0`.
2. Backfill SQL (migration `Up` gövdesinde parametrik, deterministik):
   - Her `Position.Id` için ilgili `Orders.Id` + `OrderFills.Commission` join'lenir.
   - BUY fill'leri (entry leg'leri) → `OrderFills.Commission * OrderFills.Price` ile quote'a çevrilir (fee_base × price ≈ fee_quote).
   - SELL fill'leri (exit leg'leri) → `OrderFills.Commission` doğrudan quote kabul edilir.
   - Pozisyon open değilse (sadece entry var) `ExitCommission = 0`.
3. `Position.RealizedPnl` kapalı pozisyonlar için **geriye dönük güncellenmez**. Gerekçe: UI kapalı işlemler tablosundaki geçmiş satır değeri "tarihi gerçeklik" olarak kalır; `RealizedPnl` var olan kolon olarak sıfırdan değil 25 satırdan oluşan ledger, değer tutarlılığı sağlandı (Loop 32 öncesi gross rapor edildi — bu bilgi korunur). Loop 33+ yeni kapanan trade'ler net değer yazar.
4. Opsiyonel telafi için diagnosis.md ekli; reviewer "close enough" varsayımıyla kabul eder.

**Düşünülen alternatif:** mevcut 25 pozisyonun RealizedPnl'ini de backfill SQL'den `gross - (entryFee + exitFee)` ile güncellemek. **Reddedildi** — domain event `PositionClosedEvent` o tarihte yayınlanmış değere sahipti, subscriber'lar (varsa idempotent log) eski değer üzerinden çalıştı. Geriye gidip yeniden yazmak event-sourcing kırılımı yaratır. Backfill commission kolonlarıyla sınırlı; realized aynen kalır ama "komisyon şeffaflığı" eklenir.

**3 açık pozisyon:** `EntryCommission` backfill yapılır, `ExitCommission = 0` (henüz kapanmadı). Close geldiğinde yeni imza çağrılır → `RealizedPnl_net` o andan itibaren doğru.

### 20.11 Alternatifler (Ayrıntı)

**Alt-B — BUY fee base asset'ten kes (qty azalt):**
Gerçek Binance davranışı. `Position.Open` `quantity = simulatorProvidedNetQty` ile çağrılır; simulator'da `netQty = rawQty * (1 - feeRate)`. Reddetme sebepleri:
- Sizing service ($5.10 floor) ve `Position.Quantity` arasındaki tutarlılık kırılır: sizing $5.10 notional hedefledi ama pozisyon 0.999 $5.10 olur — downstream MinNotional-benzeri validator'lar iç tutarlılığı sorgular.
- Cash muhasebesi BUY tarafında **hiç fee düşmez** → `VirtualBalance.CurrentBalance` kağıt üzerinde her BUY için fazla kalır, `Equity = cash + qty × mark` hesabı bu sefer `qty × entry` tarafında azaltılmış olduğu için `notional_saved = fee_base × price` toplam MTM'de fark yaratmaz. Cash-side invariant ise **yine kırık** kalır (cash tam, qty azaldı, ama `open_cost_basis = reduced_qty × entry` hesabı bu farkı emer). Matematiksel olarak tutarlı ama **yarı-kırık** — SELL tarafı hâlâ cash-side fee düşürdüğü için iki yönde iki farklı denklem çalışır.

**Alt-C — Fee'siz paper:**
`PaperFeeSimulator.CalculateCommission → 0`. Her şey tutarlı, kod basitleşir. Reddetme sebebi: Loop 32 strateji reform ölçümü fee drag'i görmek zorunda. ADR-0018 fee sim kararını doğrudan çürütür. Üretim geçişinde %100 hesap bozar.

**Alt-D — Sadece `totalCommissionPaid` metriğini düzelt, domain gross kalsın:**
`RealizedPnl` gross kalır, UI'da `NetProfitAfterFees = RealizedPnl - commission_quote` handler'da hesaplanır. Reddetme: Fee muhasebesi **her yerde** kırık kalır; `VirtualBalance.CurrentBalance` BUY fee'sini asla görmez → cash invariant onarılamaz. Sadece UI kozmetik.

## Consequences

### Pozitif

- **Cash invariant tam onarıldı** — `starting + sum(RealizedPnl_net) - sum(open_cost_basis) = cash` her çağrıda doğru.
- **Tek doğruluk kaynağı** — `trueEquity - startingBalance = netPnl = RealizedPnlAllTime + UnrealizedPnlTotal`; reviewer matematiksel kimlik olarak doğrular.
- **Fee drag görünür** — Loop 33+ parametre reform gerçek "net kar sonrası fee" değerine bakar.
- **OrderFill ledger korunur** — Binance fill-report shape (base/quote asset discrimination) idempotency için aynı.
- **UI commission metriği anlamlı** — tek birim (USDT), Position aggregate kolonundan.

### Negatif / Tradeoff

- **Paper ≠ mainnet bire bir** — gerçek Binance BUY fee base asset'ten keser, paper cash'ten keser. `Position.Quantity` paper'da tam qty; live'da fee-reduced. Kabul edilebilir sapma (ADR-0018 zaten sabit 0.10%/0.075% + sabit slippage pct'yi paper'a özel yazmış; bu sapma o onay setine girer).
- **25 mevcut kapalı pozisyonun `RealizedPnl`'i gross kalır** — tarihsel veri set asymmetric (Loop 32 öncesi gross, Loop 33 sonrası net). UI "Kapalı İşlemler" tablosu loop kesiti bilgisi olarak metin notu ister.
- **Breaking API — `Position.Open` + `Position.Close` imzası** — tüm çağıranlar güncellenmeli (handler, test, migration seed). Ama `backend-dev` için compile-time error olduğu için "sessiz bug" riski yok.
- **Test envanteri artar** — domain unit test (Position.Close net realized), simulator test (cash-symmetric invariant property-based), application handler test (TotalCommissionPaid yeni kaynak).

### Nötr

- Migration çalışma süresi < 1s (25 pozisyon, az satır).
- `OrderFills.Commission` kolonu değişmez — geri kalan ledger path'i stable.
- `PositionClosedEvent.RealizedPnl` anlam kayması (gross → net): downstream subscriber yok (Loop 32 itibarıyla sadece logging/notification), dolayısıyla pratik etki yok. Yine de reviewer "hangi event semantic versiyonunda" kontrolü ister — bu ADR event semantics bump eder (dokümantasyon düzeyinde).

## Alternatifler

§20.11'de üç alternatif (Alt-B / Alt-C / Alt-D) gerekçeleriyle reddedildi.

Ek düşünülen: **Transactional Outbox** (ADR-0012'de ileri atılmış) ile fee event'lerini ayrı akışa çıkarmak — bu ADR **kapsam dışı**; amaç paper muhasebe tutarlılığı, event propagation değil.

## Kaynak

- `loops/loop_32/diagnosis-pnl-discrepancy.md` — tanı, root cause, matematik kanıt
- `docs/research/paper-fill-research.md` — paper fill sim kontratı
- [ADR-0008 Trading Modes](./0008-trading-modes.md) §8.2 — fill ledger idempotency şeması
- [ADR-0011 Equity-Aware Sizing](./0011-equity-aware-sizing-and-risk-tracking.md) §11.5 — fee + slippage sabit oranlar
- [ADR-0018 Micro-Scalping](./0018-micro-scalping-30s-vwap-reclaim.md) §18.12 — BNB discount toggle (`UseBnbFeeDiscount`) ve `PaperFeeSimulator`
- [ADR-0019 Per-Coin + Capital](./0019-per-coin-parameters-capital-10x.md) §19.x — fee path doğrulama bayrağı (bu ADR ile kapatılıyor)
- Binance Spot Commission: VIP0 taker = 0.10%; BNB discount (fee paid in BNB) = 0.075%

## Sonraki Adım

`backend-dev` ADR-0020'yi uygular:
1. `Position` aggregate alan + imza değişikliği (Domain).
2. `AddPositionCommissions` migration + backfill SQL (Infrastructure).
3. `PaperFillSimulator.FillMarket` cash-symmetric bloğu + `PaperFillOutcome.QuoteCommissionTotal` (Infrastructure).
4. `OpenPaperPositionCommand` + `ClosePaperPositionCommand` handler'ları yeni imza (Application).
5. `GetPortfolioSummaryQuery` `TotalCommissionPaid` kaynağı aggregate-level (Application).
6. Domain + simulator + handler testleri (Tests).

`tester` Playwright + DB invariant check ile doğrular (`trueEquity - starting ≡ realizedAll + unrealizedAll`). `reviewer` simulator-symmetry + domain net-realized invariant skill'leriyle denetler.
