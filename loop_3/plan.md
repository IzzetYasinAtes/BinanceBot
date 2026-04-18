# Loop 3 Plan — Backlog 11 Madde Tanı + Çözüm

**Boot zamanı:** 2026-04-18 ~01:10 UTC
**DB durumu:** korundu (Loop 2 verisi üzerinde tanı)
**Sıra:** plan → uygulama (delegasyon zincirleri) → DB drop → Loop 3 4h cycle

---

## Öncelik Sıralaması

| Pri | # | Madde | Etki | Agent |
|---|---|---|---|---|
| **P0** | #11 | /api/orders/history 500 | UI çoğu sayfa kırık | backend-dev |
| **P0** | #10 | Retro-sinyal patlaması | Trade distorsiyonu, ölçüm bozuk | architect+backend-dev |
| **P0** | #3+#4+#7+#8 | Sizing+Exit+Risk reform | Risk yönetimi tamamen kırık | architect+backend-dev |
| **P1** | #5+#6 | UI portföy/orders düzeltme | Yanlış sunum | frontend-dev |
| **P2** | #1+#2 | Timezone + header ticker | UX iyileştirme | frontend-dev |
| **P3** | #9 | GitHub achievements | Rozet stratejisi | PM (workflow change) |

---

## P0 Maddeler

### #11 — /api/orders/history ve /api/strategies/signals/latest 500 hatası

**Tanı:**
- `src/Application/Behaviors/ValidationBehavior.cs:47` — `typeof(Result).GetMethods().Single(m => m.Name == nameof(Result.Invalid) && m.IsGenericMethodDefinition && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(List<ValidationError>))`
- Ardalis.Result yeni sürümünde `Result.Invalid<T>(IEnumerable<ValidationError>)` veya `params ValidationError[]` imzası olabilir → `List<ValidationError>` predicate eşleşmiyor → boş sequence → `.Single()` patlıyor.
- Tetikleyici: validation hata olan herhangi bir Query (örn. boş optional parametre validator boş döndürse bile failures.Count > 0 ise giriyor).

**Çözüm:**
- `Single()` yerine `FirstOrDefault()` + null kontrol; veya
- Predicate genişlet: `IEnumerable<ValidationError>` veya `List<ValidationError>` her ikisini de kabul et.
- Ya da Ardalis.Result API'yi kullan: `Result.Invalid<T>(errors)` doğrudan, reflection yerine generic helper.

**Agent:** backend-dev (tek dosya).

**Done def:** /api/orders/history?take=1000 200 dönsün, /api/strategies/signals/latest?limit=200 200 dönsün.

---

### #10 — Backfill retro-sinyal patlaması

**Tanı:**
- `KlineBackfillWorker` boot'ta 1000 mum REST'ten çeker → `KlinePersister.PersistAsync` her bar için DB insert.
- `StrategyEvaluationHandler` muhtemelen yeni Kline insert'i evt veya cron'la dinliyor → her insert için evaluator çalıştırıyor → geçmiş bar'lar şimdiki saatte sinyal üretiyor.
- Loop 2 boot'ta 21:00:02'de tek anda 11 sinyal emit oldu (hepsi farklı `barOpenTime` ama aynı `emittedAt`).

**Çözüm seçenekleri:**
- (A) `BackfillCompletedEvent` MediatR notification → `IBackfillState.IsSeeded` flag → Evaluator önce `IsSeeded` kontrol etsin.
- (B) `KlinePersister.PersistAsync(payload, ct, isBackfill: bool)` — backfill modunda evaluator'ı tetikleme.
- (C) Evaluator sadece "son kapanan bar"a değil "son N saniyede yazılan ve barOpenTime > UtcNow - 2dk" filtresine baksın.

**Önerim:** (B) en temiz — `KlinePersister`'da iki overload, `KlineBackfillWorker` `isBackfill: true` geçer; WS path doğal `false`.

**Agent:** architect (ADR-0010) → backend-dev.

**Done def:** Loop 4 boot sonrası ilk sinyal en az 1 yeni canlı bar sonra (≥1 dk gecikme); retro-sinyal sayısı 0.

---

### #3+#4+#7+#8 — Sizing + Exit + Risk Reform (Tek Atomik İş)

**Tanı (birleşik):**

**#3 (adet yanlış):** Evaluator'larda `OrderSize` config'ten sabit (BTC 0.001, BNB 0.01). `100$ × 1% = $1` risk olması gerekirken 0.01 BNB × $640 = **$6.40**. Sizing equity-aware değil, fee/min-notional uyumsuz.

**#4 (exit eksik):** Evaluator `Direction.Exit` sinyali üretiyor ama PlaceOrderCommand bunu pozisyon kapatma olarak işlemiyor. Açık pozisyonlar uzun süre OPEN kalıyor.

**#7 (risk tablosu boş):** `RiskProfile.RealizedPnl24h/PeakEquity/CurrentDrawdownPct` hep 0. Trade kapanışında risk profile güncelleme akışı yok. CB tetikleme mantığı koşulsuz Healthy.

**#8 (parametre az):** Risk %1, Max %10 → 100$ portfoyde tek trade $1 → komisyon (%0.1 × $1 = $0.001) hesabı çalışmıyor. Min notional Binance Spot için BTC/USDT=$10, BNB/USDT=$10.

**Birleşik çözüm tasarımı (architect ADR-0011):**

1. **`PositionSizingService`** (yeni Application servisi):
   - `decimal CalculateQty(equity, entryPrice, stopDistance, riskPct, maxPositionPct, minNotional)`
   - `riskAmount = equity * riskPct`
   - `qty = riskAmount / stopDistance` (stop-aware sizing)
   - `notional = qty * entryPrice`
   - `if (notional < minNotional) return 0` (skip trade — too small)
   - `if (notional > equity * maxPositionPct) qty = (equity * maxPositionPct) / entryPrice` (cap)
2. **Evaluator değişikliği:** `suggestedQuantity` artık config'ten değil, `PositionSizingService`'den hesaplanır. Bunun için evaluator'a `currentEquity` ve `instrumentMetadata.minNotional` inject edilir.
3. **Exit handling:** `PlaceOrderCommand` veya yeni `ClosePositionCommand` — `Direction.Exit` sinyali → ilgili sembolün açık pozisyonunu kapat.
4. **RiskProfile güncelleme:** Her trade fill veya exit sonrası `UpdateRiskProfileCommand`-benzeri içsel akış: `realizedPnl24h += closedPnl`, `peakEquity = max(peakEquity, equity)`, `currentDrawdownPct = (peakEquity - equity) / peakEquity`.
5. **CB tetikleme:** `currentDrawdownPct > maxDrawdown24hPct` veya `consecutiveLosses >= maxConsecutive` → CB Tripped, tüm strateji Paused.
6. **Risk parametre defaults:** `RiskPerTradePct: 0.05` (5%), `MaxPositionPct: 0.25` (25%), `MaxConsecutiveLosses: 3` (kalıyor). 100$ × 5% = $5 risk → minNotional $10 ile uyumlu (2x leverage'siz Spot).

**Agent zinciri:** binance-expert (min notional/fee doğrulama) → architect (ADR-0011 + servis tasarımı) → backend-dev (Sizing + Exit + RiskUpdate) → reviewer.

**Done def:**
- Yeni trade sizing equity-aware ($1.5 risk değil, %5 risk)
- Exit sinyali pozisyon kapatıyor
- RiskProfile her trade sonrası güncelleniyor
- CB %5 24h DD'de tripped oluyor

---

## P1 Maddeler

### #5 — Portföy özeti doğruluk
**Tanı:** `index.html:281-294` — winRate `(Filled / (Filled + Rejected)) × 100`. Bu **fill başarı oranı**, "kazanan trade oranı" değil. Kullanıcının gördüğü %33.3 = 5 fill / 15 toplam → çoğu Mainnet rejected.

**Çözüm:** Win rate hesabını trade-pair bazına çevir. `closedPositions.filter(p => p.realizedPnl > 0).length / closedPositions.length`. Backend'den `/api/positions/closed` endpoint'i varsa onu kullan, yoksa eklen.

**Agent:** backend-dev (yeni endpoint?) + frontend-dev (UI bind).

### #6 — İşlemler sayfası açık tablo
**Tanı:** `orders.html` "Açık İşlemler" `api.orders.open` çağırıyor. Open endpoint = Status `New|PartiallyFilled` order'lar. Tüm fill'ler Market type instant Filled olduğu için Open her zaman boş. **Açık pozisyon ≠ açık order.**

**Çözüm:** `Açık İşlemler` yerine `Açık Pozisyonlar` — `api.positions.open()` (yeni endpoint). Veya tabloyu Open Order anlamında kalsın, "Açık Pozisyonlar" ayrı sekme açılsın.

**Agent:** backend-dev (endpoint) + frontend-dev (UI).

---

## P2 Maddeler

### #1 — UTC kaydet, UTC+3 (İstanbul) göster
**Tanı:** `format.js:20-32` — `timeIso` ve `timeHms` `toISOString()` döndürür → UTC. İstanbul TZ çevirisi yok.

**Çözüm:**
```js
timeIso: (v) => {
    if (!v) return "-";
    return new Date(v).toLocaleString("tr-TR", {
        timeZone: "Europe/Istanbul",
        year: "numeric", month: "2-digit", day: "2-digit",
        hour: "2-digit", minute: "2-digit", second: "2-digit",
        hour12: false,
    }).replace(",", "");
},
timeHms: (v) => {
    if (!v) return "-";
    return new Date(v).toLocaleTimeString("tr-TR", {
        timeZone: "Europe/Istanbul",
        hour12: false,
    });
},
```

**Agent:** frontend-dev (tek dosya).

### #2 — Header'da kripto son 1dk %hareket
**Tanı:** `index.html` "Canlı Piyasa" tablosunda 24h değişim var ama header'da yok.

**Çözüm:** Index page'in en üstüne (Sidebar'ın yanına veya `<main>`'in başına) sticky bar — BTC/BNB/XRP için son 1dk close-prev_close %change. Polling 5s.
- Backend: `GET /api/klines?symbol=X&interval=1m&limit=2` (zaten var) → son 2 mum'dan hesapla.
- Frontend: yeni `MarketTickerBar` component.

**Agent:** frontend-dev.

---

## P3 Madde

### #9 — GitHub Achievements
**Mevcut rozet:** ekran görüntüsünde 4 rozet (x2 başlangıç + Quickdraw + Pair Extraordinaire + x3 başka).

**Strateji (her loop'ta uygulanacak):**
1. **Pull Shark** (PR merge) — Loop sonu commit'i artık branch + PR ile merge. Her loop +1 PR.
2. **Pair Extraordinaire** (Co-Authored-By) — Zaten her commit'te var, kalıcı.
3. **YOLO** (no-review merge) — Pull Shark ile çakışıyor, dikkat. Atla.
4. **Quickdraw** (issue/PR <5dk close) — Loop sonu otomatik issue aç + 5dk içinde close.
5. **Galaxy Brain** (Discussions accepted answer) — manuel.
6. **Heart On Your Sleeve** (PR review yorumu) — bot kendi PR'ına review attırabilir mi? Sınırlı.
7. **Starstruck** (16+ star) — organik manuel.

**Loop 3 değişiklik:**
- PM'in commit/push akışı `branch + PR + merge` modeline geç.
- Auto-issue + close hook (her loop sonu).

**Agent:** PM (workflow change).

**Done def:** Loop 3 sonunda 1 yeni PR merged + 1 yeni issue close (Quickdraw için <5dk).

---

## Uygulama Stratejisi (Loop 3 Boot'un Geri Kalanı)

**Token bütçesi göz önünde:** Hepsini tek wakeup'ta yapmak zor. Önerilen sıra:

**Wakeup 1 (şu an):** Plan + commit ✅
**Wakeup 2:** P0 — #11 fix (5 dk iş) + #1 fix (timezone, 5 dk iş) — basit kazanımlar.
**Wakeup 3:** P0 — #10 retro-sinyal (architect ADR + backend-dev + reviewer).
**Wakeup 4-5:** P0 — #3+#4+#7+#8 birleşik reform (en büyük iş).
**Wakeup 6:** P1 — #5+#6 UI fix.
**Wakeup 7:** P2 — #2 ticker.
**Wakeup 8+:** P3 — workflow + Loop 3 normal 4h cycle başlat.

Her wakeup sonu: build + test + commit + push.

DB drop **en sona** — tüm reformlar bittikten sonra Loop 3 normal cycle başlar.
