# Loop 92 — Cash / UI Audit (pre-spec)

**Tarih:** 2026-05-03
**Agent:** backend-dev
**Kapsam:** SADECE READ — `GetPortfolioSummaryQuery` + VirtualBalance + frontend response uyumu
**Durum:** Bot KAPALI, DB tam reset ($500 baseline, 0 pos, 0 trade event)
**Karar Özeti:** **TEMİZ (kritik bug yok) + 3 RİSKLİ alan + 1 DÜŞÜK-ÖNCELİK BUG**

---

## 1. `GetPortfolioSummaryQuery` Cash Formülü — TEMİZ

**Dosya:** `src/Application/Portfolio/Queries/GetPortfolioSummary/GetPortfolioSummaryQuery.cs`

Loop 84'te kalıcı çözülen formül (satır 165-169):

```csharp
ledgerCash = StartingBalance
           + Σ closed.RealizedPnl       // satır 166
           − Σ open.cost-basis          // satır 167  (AverageEntryPrice × Quantity)
           − Σ open.EntryCommission;    // satır 168
trueEquity = ledgerCash + Σ open.cost-basis + Σ open.UnrealizedPnl;  // satır 169
```

- **Doğrulanan:** ledger-driven, `VirtualBalance.CurrentBalance` snapshot'ı bypass'lanıyor (satır 165-168). Snapshot drift Loop 84'te gözlemlenen $157 phantom'un sebebiydi; mevcut implementasyon tek source-of-truth: `Positions` tablosu.
- **Test pinleri:** `tests/Tests/Application/Portfolio/GetPortfolioSummaryQueryTests.cs` satır 105-134 (`OpenPositionWithUnrealizedPnl_TrueEquityExceedsCash`), 261-369 (`LedgerCash_DerivesFromPositions_NotFromVirtualBalanceSnapshot`) — formül arithmetic'i pinli.
- **Invariant:** `NetPnl == TrueEquity − StartingBalance` üç senaryoda da test edilmiş (satır 180, 196, 213).
- **EF kullanımı:** `AsNoTracking()` (satır 70, 85, 107, 127) — read path doğru. Lazy loading yok. CQRS pattern temiz.

**Karar: TEMİZ.** Fresh DB ($500, 0 pos, 0 closed) durumda formül `500 + 0 − 0 − 0 = 500` döndürür. Loop 92 boot'ta UI hero "Toplam Net K/Z = $0", "Mevcut Bakiye = $500", "Gerçek Özkaynak = $500" gösterir.

---

## 2. VirtualBalance ↔ Positions Tutarlılığı — RİSKLİ (yarı-bozuk-snapshot)

**Dosyalar:**
- `src/Domain/Balances/VirtualBalance.cs` (ApplyFill satır 94-107)
- `src/Application/Orders/Commands/PlaceOrder/PlaceOrderCommand.cs` satır 304-316
- `src/Infrastructure/Risk/VirtualBalanceConsistencyChecker.cs` satır 39-90

### 2.1 Snapshot dışlanmış ama hâlâ yazılıyor

`PaperFillSimulator` her fill'de `outcome.RealizedCashDelta` üretir, `PlaceOrderCommand` satır 310'da `paperBalance.ApplyFill(...)` ile VirtualBalance.CurrentBalance/Equity'yi günceller. **AMA** `GetPortfolioSummaryQuery` artık bu sütunu okumuyor (Loop 84 fix). Sonuç:

- **Headline UI** (`/api/portfolio/summary`): ledger-derived → doğru.
- **`/api/balances`** (`GetBalancesQueryHandler`, satır 48-71): hâlâ `b.CurrentBalance` ve `b.Equity` döndürüyor (sat 51-53).
- **`EquitySnapshotProvider.GetEquityAsync`** (satır 40-59): sizing-dışı caller'lar (yorumdaki "ADR-0008 §8.4") hâlâ `Equity > 0 ? Equity : CurrentBalance` okuyor.

**Risk:** Eğer ileride yeni bir UI panel veya health check `/api/balances` üzerinden okumaya başlarsa Loop 84 phantom sorununun bir varyantı geri gelir (snapshot drift). Şu anki frontend (`api.balances.list()`) reset modal'ı dışında çağırmıyor, ama dosyada export edilmiş halde duruyor (`api.js` satır 105).

### 2.2 GetEquityAsync inconsistency

`EquitySnapshotProvider.GetEquityAsync` (sizing-dışı yol) `balance.Equity` döndürüyor. Bu, `ApplyUnrealized` çağrılmazsa `CurrentBalance` ile eşit kalıyor. **Yarı-kapalı pozisyon problemi:** açık pozisyonlar `ApplyUnrealized`'ı her tick çağırmıyorsa Equity stale; çağırıyorsa MTM race'e açık (Loop 18 trace'de bahsedilen). Bot kapanıp tekrar açıldığında Equity = CurrentBalance ile başlar (bot restart'ta unrealized snapshot kaybolur — DB'de duruyor ama VirtualBalance.Equity stale yazılı).

**Şu anki etki:** Bot kapalıdır, DB resetlenmiş; `VirtualBalance(Paper)` row'u `CurrentBalance=500, Equity=500, IterationId=<new>, ResetCount` artmış durumda. `GetEquityAsync` bunu doğru döndürür. Ancak pivot sırasında bu yol kullanılırsa stale-snapshot riski sürer.

### 2.3 ConsistencyChecker probe'u korunmalı

`VirtualBalanceConsistencyChecker.StartAsync` (satır 39-90) — boot'ta `LastResetAt > Position.OpenedAt` ihlali için warning emit eder, mutate etmez. **Pre-Loop 92 reset** sonrası DB'de stale paper position kalmadığı için boot'ta sessiz olmalı. Loop 92 spec yazılmadan önce bu probe'u kaldırmaya gerek yok; gelecek reset'lerde değerli.

**Karar: RİSKLİ (P2).** Loop 92'de büyük backend implementasyonu sırasında `/api/balances` endpoint'i ya kaldırılmalı ya da ledger-derived'a dönüştürülmeli. `EquitySnapshotProvider.GetEquityAsync` da realized-only (Loop 17 yolu) kullanmaya çevrilmeli — sadece `GetSizingEquityAsync` zaten realized; non-sizing caller'ları audit edilip aynı kontrata çekmek lazım.

---

## 3. Frontend Response Shape Uyumu — TEMİZ

**Dosya:** `src/Frontend/js/pages/dashboard.js` + `src/Frontend/js/api.js`

### 3.1 Backend DTO ↔ Frontend kullanım haritası

`PortfolioSummaryDto` (GetPortfolioSummaryQuery.cs satır 25-44) → dashboard.js'in tükettiği alanlar:

| Backend alan | Frontend kullanım | Satır |
|---|---|---|
| `mode` / `modeName` | (kullanılmıyor) | — |
| `startingBalance` | hint "başlangıç ${money}" + SnowballChart | dashboard.js 114, 215 |
| `currentCash` | "Mevcut Bakiye" kart + clamp logic | dashboard.js 134, 340 |
| `openPositionsValue` | "Gerçek Özkaynak" hint + KPI | 159, 204, 207 |
| `trueEquity` | "Gerçek Özkaynak" + SnowballChart current | 152, 216 |
| `realizedPnl24h` | (kullanılmıyor — TODO?) | — |
| `realizedPnlAllTime` | "Kapalı Net" hero | 50 |
| `unrealizedPnlTotal` | "Açık (Kağıt)" hero | 61 |
| `netPnl` | "Toplam Net" hero + KPI #1 | 73, 102 |
| `netPnlPct` | hero sub + badge | 78, 111 |
| `totalCommissionPaid` | "Ödenen Komisyon" KPI | 192 |
| `netProfitAfterFees` | (kullanılmıyor) | — |
| `winningTrades` / `losingTrades` | "İşlem Sayısı" hint | 170-171 |
| `winRate` | "Kazanma Oranı" KPI | 181 |
| `openPositionCount` | "Açık (Kağıt)" hero sub + KPI #7 | 66, 202 |
| `closedTradeCount` | "Kapalı Net" hero sub + KPI #4 | 54, 168 |
| `asOfUtc` | (kullanılmıyor) | — |

**Test:** `api.portfolio.summary()` (api.js satır 123) → `request("/api/portfolio/summary")` → endpoint `MapGet("/summary"...)` (PortfolioEndpoints.cs satır 21-27).

### 3.2 JSON case sensitivity

ASP.NET Core default `camelCase` policy ile `PortfolioSummaryDto` PascalCase property'leri JSON'a `currentCash`, `trueEquity`, ... olarak iniyor. Frontend `summary.currentCash`, `summary.trueEquity` okuyor — eşleşme doğru. Test edilmiş test uçlarında ek olarak `Camel` policy override yok (Program.cs default).

**Karar: TEMİZ.** Dashboard 4 kullanılmayan alan var (`mode`, `modeName`, `realizedPnl24h`, `netProfitAfterFees`, `asOfUtc`) — backend tarafında zarar yok, frontend'de küçük dead-data. Loop 92'de kaldırılabilir veya UI panel'e işlenebilir; audit kapsamı dışı.

---

## 4. DÜŞÜK-ÖNCELİK BUG — `cashClamped` artık tetiklenemez

**Dosya:** `src/Frontend/js/pages/dashboard.js` satır 339-349

```js
const cashClamped = computed(() => {
    const c = summary.value?.currentCash;
    return typeof c === "number" && c < 0;
});
```

**Sorun:** `currentCash < 0` UI clamp'i, Loop 84 öncesi `VirtualBalance.CurrentBalance`'in negatif gidebileceği zaman (Loop 18 invariance fix sonrası) anlamlıydı. Loop 84 fix sonrası **`ledgerCash = StartingBalance + Σ realizedPnl − Σ open.cost-basis − Σ open.commission`** matematik olarak ancak şu durumda negatif olur:
- `StartingBalance + realizedPnl < open.cost-basis + open.commission`
- yani: gerçekleşmiş kayıp + open notional toplam capital'ı aşmış. Sizer (Loop 19 ADR-0011) `MaxOpenPositions × MaxPositionSizePct < 1` kontratıyla çalıştığı sürece teorik olarak imkansız (Long-only, leverage yok).

**Ancak:** Pozisyon-cap gate'lerinin breach edildiği test senaryolarında (manual admin-open, race-condition seed) clamp hâlâ koruyucu. Production zararsız → DÜŞÜK ÖNCELİK.

**Karar: BUG VAR (P3).** Mevcut algoritmada tetiklenmesi imkansız ama silmek için Domain invariance kontratlarının (RiskProfile.MaxOpenPositions × MaxPositionSizePct ≤ 1) write-time enforcement'ı doğrulanmalı. Şu an hibe bedava güvenlik ağı; Loop 92 backend-dev büyük delegasyonunda kaldırma kararı verilebilir veya Pattern-Pivot'a sentinel olarak bırakılabilir.

---

## 5. Position MTM ve Cash Tutarlılığı — RİSKLİ

**Dosya:** `src/Domain/Positions/Position.cs` (Close satır 335-363, MarkToMarket 187-202) + Loop 76 trailing satır 290-333

### 5.1 RealizedPnl fee-net kontratı

`Position.Close` satır 356:
```csharp
RealizedPnl = gross - EntryCommission - ExitCommission;
```

`GetPortfolioSummaryQuery` formül satır 166'da `Σ closed.RealizedPnl` olarak çekiyor → fee-net.

**Tutarlılık doğrulaması (DB dump dışı):** `tests/Tests/Application/Portfolio/GetPortfolioSummaryQueryTests.cs` satır 261-369 senaryoda −0.61 net realized PnL pinli, formül ile doğrulanıyor. Domain ve handler aynı kontratı paylaşıyor.

### 5.2 Open position EntryCommission akışı

`Position.AddFill` (satır 165-185) — partial fills için cumulative fee accumulation (`EntryCommission += addCommission`). Hiç fee credit (negatif) kabul etmiyor (`if (addCommission < 0m) throw`). Handler `Σ open.EntryCommission` (satır 93) bunu doğru çekiyor.

**Risk:** Open position için `ExitCommission` her zaman 0 (Close'a kadar yazılmıyor). `netAfterFees = realizedAllTime + unrealizedTotal − openCommission` (handler satır 184) doğru — ama unrealizedPnl **fee-gross** (MarkToMarket fee düşmüyor), exit commission daha sonra Close'da düşecek. Hero "Açık (Kağıt)" UI metrik, açık pozisyon kapanınca commission düştüğü için **iyimser** görünür (~%0.10 fee × notional iki yan = % 0.20 hero overstate). dashboard.js satır 84-86'daki açıklama notu zaten kullanıcıya bunu söylüyor — kontrat tutarlı.

### 5.3 Trailing exit ve break-even idempotency

`Position.UpdatePeakAndCheckTrailing` (satır 290-333) ve `MoveStopToBreakEven` (satır 225-261) Result-style enum dönüyor (CLAUDE.md root rule #5 uyumlu, exception-for-flow yok). Cash'e direkt etki yok — exit dispatch sonrası `CloseSignalPositionCommand` zincirinden geçiyor; cash hesabı close'da realize olur. Audit'in hedefi olan cash UI bu iki yoldan etkilenmiyor.

**Karar: RİSKLİ (P2 informational).** Loop 92 spec için "açık unrealized fee-gross gösterimi" UI tooltip'i koruyor; backend tarafından açıkça yazılmış kontrat var. Pivot'ta dikkat edilecek noktalar §6'da.

---

## 6. Futures Pivot — Cash → Wallet + Margin Geçişi (Uyarılar)

Mevcut model **spot Long-only** kabul ediyor. Futures pivot'unda dikkat:

1. **Margin lock kavramı yok.** `cost-basis = AverageEntryPrice × Quantity` formülü spot için doğru ("notional cash bağlandı"). Futures'da **initial margin = notional / leverage** ve `wallet = freeCollateral + maintenanceMargin + lockedMargin`. Mevcut `ledgerCash = start + realized − notional − fee` formülü 1x leverage'da doğru, ama 2x+ leverage'da `notional/leverage` bağlanır, kalan sermaye serbest. Formül leverage-aware olmalı.

2. **Funding fee periodicity.** Spot'ta yok; perpetual futures'da 8h'de bir funding cash'e direkt etki eder. `Position` aggregate'inde yeni alan: `AccumulatedFunding`. `RealizedPnl = gross − entryFee − exitFee + funding` veya ayrı `Σ funding` kalemi `GetPortfolioSummaryQuery`'ye eklenmeli.

3. **Mark price ≠ index price.** Liquidation hesabı için `markPrice − maintenanceMargin` izlenmeli. Domain'e `LiquidationPrice` (computed) gerekecek. UI'da ayrı kart.

4. **Cross vs Isolated.** Cross margin'de `wallet.balance` tüm açık pozisyonlar için ortak; bir pozisyon batırırsa cüzdan sıfırlanabilir. Isolated'da pozisyon başına margin → mevcut domain modeline yakın. ADR yazılmadan önce `architect` + `binance-expert` danışılmalı.

5. **Negative wallet (insurance fund).** Mainnet futures'da liquidation fee ile bakiye negatife inebilir. Loop 18'deki "negatife izin ver" kararı (ApplyFill clamp kaldırma) burada da geçerli ama daha agresif (insurance fund balance koruması var).

6. **Quantity sign convention.** Spot `Quantity > 0`. Futures'da position size signed (Long +qty, Short −qty) olabilir veya `PositionSide` tutulup `Quantity` her zaman pozitif. Mevcut kod `PositionSide` enum + `Quantity > 0` (Position.cs satır 113-115) — futures'a taşırken konvansiyon korunabilir, sadece P&L formülü `Side`'a göre dallanıyor (zaten satır 196-198 ve 353-355'te yapılıyor). KISS açısından mevcut yaklaşım futures'a da uygun.

7. **Reduce-only orders.** Futures'da pozisyon kapatma için `reduceOnly=true` flag gerekir (yanlış yön açma riski). Order entity'sine yeni alan eklenmeli; PaperFillSimulator de respect etmeli.

8. **Cash lock vs unrealized PnL ayrımı.** Spot formülde `openPositionsValue = costBasis + unrealizedPnl` doğru — açık pozisyonun pencerede ne kadar değer tuttuğu. Futures'da bu kalem ikiye ayrılmalı: `lockedMargin` (initial margin) + `unrealizedPnl`. UI'da "kasada kilitli" vs "kağıt karı" ayrı gösterim.

9. **Reset komutu temizliği.** `ResetPaperBalanceCommandHandler` (satır 152-169) `Positions` + `Orders` + trade-related `SystemEvents` siliyor. Futures geçişinde yeni tablo (`FundingPayments`, `LiquidationEvents` gibi) eklenirse bu komuta reset listesine alınmalı, yoksa Loop 81 stale-row bug'ının futures versiyonu doğar.

10. **`ResetPaperBalanceCommandHandler` transaction yok.** Satır 195'te tek `SaveChangesAsync(ct)` — EF Core implicit transaction içerisinde tüm RemoveRange + Add + Update'leri atomic yapıyor (default behavior). Ancak SignalR/MarkToMarketWorker race'i için **explicit `BeginTransactionAsync` yok**. Loop 81 spec'inde "single transaction" notu var ama kod implicit'e güveniyor — futures'da daha çok tablo + concurrent worker olunca explicit transaction önerilir.

---

## Özet Tablo

| # | Alan | Karar | Öncelik | Loop 92 aksiyon |
|---|---|---|---|---|
| 1 | `GetPortfolioSummaryQuery` cash formülü | **TEMİZ** | — | Yok |
| 2 | VirtualBalance ↔ Positions consistency | **RİSKLİ** | P2 | `/api/balances` ya ledger'a çevir ya kaldır |
| 3 | Frontend response shape | **TEMİZ** | — | Yok (4 kullanılmayan alan opsiyonel temizlik) |
| 4 | `cashClamped` UI clamp | **BUG (düşük)** | P3 | Pivot sırasında değerlendir, şu an zarar yok |
| 5 | Position MTM + fee + trailing | **RİSKLİ (info)** | P2 | UI tooltip mevcut, futures'da netleştir |
| 6 | Futures pivot uyarıları | — | — | Spec yazımında §6'daki 10 madde dikkate alınmalı |

---

## Read-only doğrulama referansları

- Backend handler: `src/Application/Portfolio/Queries/GetPortfolioSummary/GetPortfolioSummaryQuery.cs:165-169, 184`
- Domain VirtualBalance: `src/Domain/Balances/VirtualBalance.cs:94-107` (cash invariance fix Loop 18)
- Domain Position close (fee-net): `src/Domain/Positions/Position.cs:335-363`
- Reset komutu (atomic-via-implicit-tx): `src/Application/Balances/Commands/ResetPaperBalance/ResetPaperBalanceCommand.cs:195`
- Boot probe: `src/Infrastructure/Risk/VirtualBalanceConsistencyChecker.cs:39-90`
- Sizing equity (realized-only): `src/Infrastructure/Trading/EquitySnapshotProvider.cs:105-106`
- Endpoint: `src/Api/Endpoints/PortfolioEndpoints.cs:21-27`
- Frontend tüketim: `src/Frontend/js/pages/dashboard.js:284, 99-209, 339-349`
- API client: `src/Frontend/js/api.js:120-124`
- Pin testi (cash regression): `tests/Tests/Application/Portfolio/GetPortfolioSummaryQueryTests.cs:261-369`
- Cash apply yolu (production): `src/Application/Orders/Commands/PlaceOrder/PlaceOrderCommand.cs:304-316`

---

**Sonuç:** Mevcut DB reset durumu için cash UI **kritik bug barındırmıyor**. Loop 92 spec yazılırken §2 (balances endpoint legacy snapshot okuması) ve §6 (futures pivot) backend-dev büyük delegasyonuna en üst düzey risk olarak işaretlenmeli. Bot başlatıldığında headline UI fresh ($500/$0/$500) tutarlı dönecektir.
