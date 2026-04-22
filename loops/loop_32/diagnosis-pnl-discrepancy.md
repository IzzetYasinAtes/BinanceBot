# Loop 32 — PnL Discrepancy Diagnosis

**Tarih:** 2026-04-22 18:54 UTC
**Bildirim:** Kullanıcı, portfolio summary'nin "saatte ~$0.5 kar" gösterdiğini, ancak Kapalı İşlemler tablosundaki Net PnL kolonlarının toplamının hafif zarar çıkardığını raporladı. İki değer uyumsuz.

---

## 1. Gözlem (`GET /api/portfolio/summary` t=2026-04-22T18:54:13Z)

| Field | Değer |
|---|---|
| startingBalance | 100.0000 |
| currentCash | 84.1301 |
| openPositionsValue | 16.2862 |
| **trueEquity** | **100.4164** |
| realizedPnl24h | 0.0000 |
| realizedPnlAllTime | **-0.0109** |
| unrealizedPnlTotal | +0.5281 |
| **netPnl** | **+0.5173** |
| totalCommissionPaid | 0.1199 |
| winningTrades / losingTrades | 12 / 13 |
| openPositionCount / closedTradeCount | 3 / 25 |

## 2. DB Doğrulama (`loops/loop_32/diag-query.ps1`)

### 2.1 Kapalı pozisyonlar
```
N=25   TotalRealized=-0.010856   FirstClose=2026-04-21 16:10 UTC   LastClose=2026-04-21 17:05 UTC
```

**Kritik:** Son kapanan trade **21 Nisan 17:05 UTC**, şu an **22 Nisan 18:54 UTC** — yani son 25 saattir hiç trade yok. `realizedPnl24h=0` **doğru** (24 saat penceresinde hiç trade yok), bug değil. **Gerçek sessizlik** (aşağıda Loop 32 sağlık bölümüne bak).

### 2.2 Symbol breakdown
```
BNBUSDT  7 trade  Net=+0.006704
BTCUSDT  7 trade  Net=-0.004198
ETHUSDT  5 trade  Net=+0.001463
XRPUSDT  6 trade  Net=-0.014826
Toplam  25 trade  Net=-0.010857
```

### 2.3 Win/Loss
```
Wins=12   Losses=13   GrossWin=+0.074715   GrossLoss=-0.085571   Net=-0.010856
```

### 2.4 Açık pozisyonlar
```
Id=326 ETHUSDT qty=0.0023 entry=2308.06 unrl=+0.2089  notional=5.31
Id=327 XRPUSDT qty=3.60   entry=1.4286  unrl=+0.0735  notional=5.14
Id=328 BTCUSDT qty=0.00007 entry=75806.25 unrl=+0.2259 notional=5.31
Toplam cost basis = 15.76 USD   sum(UnrealizedPnl @ snapshot) = +0.5083
```

### 2.5 Balance
```
VirtualBalance  Paper  Starting=100.00  CurrentBalance=84.1301  Updated=2026-04-21 17:05
```
**Dikkat:** `UpdatedAt=21.04` → virtual balance 25 saattir dokunulmamış (yeni fill yok).

---

## 3. Matematiksel Çelişki

### Beklenen cash invariant
`starting + sum(RealizedPnl_closed) − sum(open_cost_basis) = currentCash`
`100 + (−0.0109) − 15.7581 = **84.2310**`

### Gerçek cash
`currentCash = **84.1301**`

### Fark
`84.2310 − 84.1301 = **0.1009 USD daha az**`

### trueEquity ile netPnl uyumsuzluğu
- `trueEquity − startingBalance = 100.4164 − 100 = **+0.4164**` (gerçek cash-based equity artışı)
- `netPnl (API)         = **+0.5173**`
- Fark = **0.1009 USD** (aynı 0.10 farkı)

→ **İki metrik aynı miktar kadar uyumsuz.** Bu kaynak aynı bug.

---

## 4. Root Cause

### 4.1 Asimetrik fee deduction — `PaperFillSimulator.cs:170-190`

```csharp
foreach (var f in fills) {
    var (commission, commissionAsset) = ComputeCommission(order.Side, ...);
    order.RegisterFill(tradeId, f.Price, f.Quantity, commission, commissionAsset, now);

    if (order.Side == OrderSide.Buy) {
        // Spend quote: -price*qty; base commission is taken from received base (doesn't affect cash)
        realizedCash -= f.Price * f.Quantity;        // ← FEE CASH'TEN DÜŞÜLMÜYOR
    } else {
        // Receive quote: +price*qty - quote commission
        realizedCash += (f.Price * f.Quantity) - commission;   // ← FEE CASH'TEN DÜŞÜLÜYOR
    }
}
```

**Asimetri:**
- **BUY fee:** base asset cinsinden `OrderFill.Commission` alanına yazılıyor ama `VirtualBalance.CurrentBalance`'a etkisi yok ("base commission is taken from received base" yorumu). Ancak Position.Quantity, fee düşmeden tam qty olarak kaydediliyor — yani gerçek hayatta 0.00007 BTC alırken 0.00006993 kalıyor olması gerekirken, paper'da 0.00007 BTC pozisyon açılıyor. Kaybolan fee "ghost" halde.
- **SELL fee:** quote asset cinsinden hem `OrderFill.Commission`'a yazılıyor hem `CurrentBalance`'tan düşülüyor.

**Sonuç:** Round-trip başına cash delta = `(exit − entry) * qty − SELL_commission`. BUY tarafı fee hiç cash ekonomisine girmiyor.

### 4.2 `Position.RealizedPnl` gross hesaplanıyor — `Position.cs:151`

```csharp
RealizedPnl = Side == PositionSide.Long
    ? (exitPrice - AverageEntryPrice) * Quantity     // ← GROSS, fee yok
    : (AverageEntryPrice - exitPrice) * Quantity;
```

Domain tamamen fee-agnostic. Fee hiçbir yerde RealizedPnl'e netted değil.

### 4.3 `GetPortfolioSummaryQuery.cs:144-147` yanlış yorum

```csharp
// Commissions are already netted into RealizedPnl/UnrealizedPnl by the
// paper simulator, so NetProfitAfterFees == NetPnl. The field is kept
// explicit to make the intent obvious to UI consumers.
var netAfterFees = netPnl;
```

**Bu yorum YALAN.** Fee ne RealizedPnl'e ne UnrealizedPnl'e netted. Handler bu iddiayla `NetProfitAfterFees = NetPnl = gross_realized + gross_unrealized` döndürüyor → hero UI'da $0.52 gösteriliyor.

### 4.4 Mixed-currency `totalCommissionPaid` aggregation

`SUM(OrderFills.Commission)` BUY fee'sini base asset (BTC/ETH/BNB/XRP), SELL fee'sini quote asset (USDT) cinsinden **aynı kolona** yazıyor. Toplam `0.1199` değeri anlamlı bir USD miktarı değil — BNB (0.63 USDT), BTC (75806 USDT) gibi birim değişken. UI bu alanı "fee" olarak gösteriyor ama değer yarıdan az anlamlı.

---

## 5. Kullanıcının Orijinal Çelişkisinin Parçalı Cevapları

1. **"Saatte $0.5 kar"** = `netPnl = gross_realized + gross_unrealized = -0.01 + 0.53 = +0.52`. Bunun **yaklaşık tamamı açık pozisyonların kağıt karı**, kapalı trade'ler değil. Zaten son 25 saattir **hiç yeni trade yok**.

2. **"Kapalı işlemler Net PnL toplamı zarar"** = doğru, `sum(Position.RealizedPnl) = -$0.0109`. 25 trade, 12W / 13L, GrossWin $0.075, GrossLoss $0.086.

3. **İki değer uyumsuz görünme nedeni:** UI hero'da "netPnl" açık+kapalı birleşik metrik; tablo satırları sadece kapalı. Farklı kümeleri topluyor. Ayrıca hero değeri fee-agnostic (gross) — UI gerçek cash equity'den ~$0.10 fazla gösteriyor.

---

## 6. Loop 32 Sağlık Şikayeti (bonus)

25 saat hiç trade yok → strateji pratik olarak bitmiş/sessiz. Sebep olası:
- Slope tolerance `-0.003` + VwapTol %0.6-1.0 için koşullar birleşmiyor
- 3 açık pozisyon Max 8dk TimeStop'u aşmış olmalı, ama kapanmamış — **StopLossMonitor/TakeProfitMonitor çalışıyor mu?**
- Kullanıcı briefing'te "Loop 32 aktif" demiş — gerçekte loop effectively **stall** halde

Bu ayrı bir konu — fix zinciri dışı, Loop 33'e not olarak geçecek.

---

## 7. Fix Önerisi (Öncelik sırasıyla)

### Fix A — EN ACİL (tek hat, risk sıfır): `netPnl = trueEquity − startingBalance`

`GetPortfolioSummaryQueryHandler` içinde:
```csharp
var netPnl = trueEquity - balance.StartingBalance;
// UnrealizedTotal ve RealizedAllTime component olarak UI'a ayrı gitmeye devam eder
```

**Neden:** `trueEquity = CurrentCash + OpenPositionsValue(MTM)` — tek bir "ne kadar elim var şu an" cevabı. Fee asimetrisi olsa bile cash + MTM birlikte gerçek resmi verir. UI hero'su artık ~$0.42 gösterir (gerçek). "Kapalı Net" (realizedAllTime) ayrı metrik olarak tabloyla tutarlı.

Bu tek satır Fix-A **ana tutarsızlığı** çözer.

### Fix B — ADR-0020: Fee-aware RealizedPnl + cash-symmetric simulator

Domain-level tutarlılık için:
1. `Position.Close(exitPrice, reason, entryFeeEquivalent, exitFee, now)` imzası → RealizedPnl = gross − fees
2. `PaperFillSimulator.FillMarket` BUY'da cash'ten quote-equivalent fee düş (OrderFill.Commission kaydı kalsın)
3. `Position` entity'de `EntryCommission` + `ExitCommission` alanları (UI için)
4. `Portfolio.totalCommissionPaid` = `SUM(OrderFills.Commission * quote_conversion_at_fill)` — mixed-currency aggregation sorununu çöz

Bu kapsamlı, **architect ADR-0020** yazar, **backend-dev** uygular. Migration gerekir (2 kolon).

### Fix C — UI adlandırma netliği

Dashboard hero'su:
- "Toplam Net K/Z" = trueEquity - startingBalance (tek doğruluk kaynağı, Fix A sonrası `netPnl` bu olur)
- "Kapalı Net" = realizedAllTime
- "Açık (Kağıt)" = unrealizedTotal
- Üçü arasında eşitlik **matematiksel olarak** geçerli olsun (Fix B sonrası)

`frontend-dev` uygular.

---

## 8. Aksiyon Zinciri

| Adım | Agent | İş | Öncelik |
|---|---|---|---|
| 1 | backend-dev | Fix A — tek satır netPnl formülü + yorum düzelt | HIGH (acil) |
| 2 | architect | ADR-0020 — fee-aware domain + simulator symmetry | HIGH |
| 3 | backend-dev | ADR-0020 uygulama + migration | HIGH (ADR sonrası) |
| 4 | frontend-dev | UI hero reorganizasyon (Fix C) | MED |
| 5 | tester | Playwright + DB sanity invariant doğrulama | MED |
| 6 | reviewer | SOLID + simulator symmetry scan | HIGH |

Loop 32 sessizliği ayrı konu (Loop 33 reform scope).
