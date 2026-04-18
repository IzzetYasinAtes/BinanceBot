# Loop 14 Backlog — UI Uçtan Uca Reform

**Kullanıcıdan:** 2026-04-18 16:33 UTC (Loop 13 t30 sırasında)
**Talimat:** "Bir sonraki loop'ta UI'ı uçtan uca gözden geçir, daha anlamlı/Türkçe/kolay anlaşılır. Playwright ile açtır kontrol et. Her sayfa kolay anlaşılır. Sıfırdan tasarla ama şuanki halinden çok uzaklaştırma. Yazı tipleri değiştir, boyutlar biraz büyüt. AR-GE yaptırt alt agent'lara — Loop 14'te, şimdi değil."

---

## Kapsam (Loop 14 boot'ta)

### -1. AR-GE: Paper ↔ Live DAVRANIŞ BİREBİR (CRITICAL)

**Kullanıcı 2026-04-18 16:55:** "Araştırma yaparken live Binance'ta alım satım yaparken ne yaşayacak isek birebir aynısını yaşamalıyız paper'da da. Varsayımsal sabitlerle değil, gerçekten canlıda ne yaşancak ise onla."

**Paper ↔ Live divergence kaynakları (mevcut):**
1. **Sabit slippage %0.05** → YANLIŞ. Gerçek slippage = order size × depth × momentum. Küçük order ($15) likit sembolde ~0-1bps; büyük order ve ince book geniş. Dinamik depth walk zaten var ama FixedSlippagePct override ediyor.
2. **Sabit latency 100ms** → KABA. Gerçek: REST round-trip 50-200ms (coğrafi + congestion). Testnet latency mainnet'ten ~2x daha yavaş bazen.
3. **Fee %0.1 sabit** → VIP tier ve BNB indirimi dahil değil. Mainnet'te: VIP0 %0.1 maker/taker, BNB ödeme %0.075, 30d volume tier'ları daha düşük.
4. **MARKET execution** → Paper anlık fill tek price'tan. Live: IOC multi-level fill, slippage = avg_fill_price - best_bid/ask.
5. **Testnet depth ≠ Mainnet depth** → Testnet BID/ASK depth sığ, mainnet derin. Paper testnet BookTicker'dan çeker → live ile farklı.
6. **Testnet fiyat drift** → Testnet fiyatlar mainnet'ten bağımsız (yapay fiyat feed). Paper performans testnet'te anlamsız olabilir.
7. **Order rejection scenarios** → Live: INSUFFICIENT_BALANCE, MIN_NOTIONAL, LOT_SIZE, MAX_NUM_ORDERS, exchange maintenance. Paper sadece minNotional.
8. **Fill rate variance** → Live MARKET likit semboller %100 fill, illiquid'de partial + reject. Paper her zaman %100 fill.
9. **Network partition simulation** → Live'da WS disconnect → pozisyon görünürlüğü kaybı. Paper WS-only.

**Araştırma kapsamı (binance-expert + architect AR-GE):**

1. **Binance mainnet order book depth profile** — BTC/BNB/XRP VIP0 taker için gerçek avg slippage (AAA Ranger, SlowTrade academic papers).
2. **Order execution latency telemetry** — `/api/v3/time` vs order response time, p50/p95/p99.
3. **Fee tier schedule** — binance.com/en/fee/schedule live scrape: maker/taker tier tablosu + BNB discount + market maker rebate.
4. **Adverse selection + spread maliyet** — research: küçük trader spread cost'u $X/trade, market order taker premium.
5. **Testnet vs mainnet fiyat divergence** — testnet XRP=$1.47 mainnet=$? — parity check.
6. **Paper simulator realism framework** — Backtrader, Zipline, vectorbt paper vs live divergence nasıl azaltıyor? Best practices.
7. **Post-trade analysis** — TCA (Transaction Cost Analysis), implementation shortfall ölçümü.
8. **Exchange connectivity simulation** — WS reconnect behavior, order state drift (partial fill vs kısmi ack).

**Hedef: PAPER = LIVE - sadece gerçek para akışı farkı.**

Değişecek sabitler:
- `PaperFillOptions.FixedSlippagePct` → **dinamik** (depth walk yeterli, fixed override kaldır)
- `PaperFillOptions.SimulatedLatencyMs` → **dağılımsal** (p50=80, p95=200 Gaussian veya real measurement)
- Fee → tier-aware (VIP level config'te, BNB flag)
- Fill engine → multi-level walk (mevcut kod kısmen yapıyor, review)
- Reject simülasyonu → mainnet error code table (opsiyonel rare)

### 0. AR-GE: Küçük Portföy ($100) Position Sizing Optimumu (PRIORITY)

**Kullanıcı 2026-04-18 16:50:** "100 dolar için düzgün ayarla, internette akademik makalelerde en optimumunu arasın. Günlük %0.7/gün çok az ve saçma. Sağlam kar etmemiz lazım. Bütün metrikleri gözden geçir — bu oranlarla olmaz."

**Gözlem (Loop 13 gerçek değerler):**
- Trade notional: $12-14 (MaxPositionSizePct %15 bottleneck)
- Fee round-trip: $0.028 (%0.2)
- Break-even: %0.2 hareket gerek
- Günlük kar potansiyeli: ~%0.7 (5 trade × %0.14) — anlamsız az

**Araştırma kapsamı (binance-expert — WebFetch + WebSearch):**
1. **Kripto HFT / scalping bot optimal position sizing** akademik makaleler (arxiv.org, SSRN)
2. **Kelly Criterion fractional** küçük portföylerde (half/quarter Kelly) — win rate %50, R:R 1:1 stratejilerde optimum
3. **Small account ($100-500) trading realistik günlük/aylık getiri** benchmarks:
   - Profesyonel quant funds: %10-30/yıl = ~%0.03-0.08/gün (fund level)
   - Scalping bots (academic): %1-5/gün iddia (çoğu overfit)
   - Gerçekçi long-term: %0.1-0.5/gün sürdürülebilir (iyi strateji)
   - $100 portfoy niche: komisyon drag yüksek → daha agresif sizing zorunlu
4. **Fee threshold**: total annual fee / portfolio ne olmalı? Binance %0.1 maker, %0.075 BNB indirimi. Aktif 50 trade/gün × %0.2 round-trip = %10/gün fee — **portföyü yer**
5. **Sharpe ratio, Sortino, Calmar** hedefleri: iyi bot Sharpe >1.5, Sortino >2.0. Hedef belirle.
6. **Win rate vs R:R tradeoff**: %40 WR + R:R 2:1 ↔ %60 WR + R:R 1:1 ↔ %70 WR + R:R 0.5:1 — karşılaştır.
7. **Kripto rejim değişikliği**: trend vs range vs volatility spike — her rejimde sizing nasıl değişir?
8. **Commission/slippage budget**: günlük gelir-gider dengesi, max trades/day optimum

**Önerilecek optimum (mevcut $100 için kaba aday):**
- `MaxPositionSizePct`: %15 → **%40-50** (tek trade $40-50 notional, R:R 1.5:1 ile $0.80+ kazanç hedefi)
- `RiskPerTradePct`: %1 → **%2-3** (daha agresif ama küçük port için gerekli)
- Aynı anda max 2-3 pozisyon ($100 × 2-3 × %40-50 = aşırı — sıkı concurrency control)
- Strateji frekans: 10-20 trade/gün hedef (fee drag makul)
- **Hedef:** sürdürülebilir **%1-3/gün** net (fee düştükten sonra)

**Çıktı:**
- `loops/loop_14/research-sizing-optimum.md` — academic references + Kelly math + concrete parameter recommendations for $100 portfoy
- `loops/loop_14/decision-risk-reform.md` — architect concrete parameter + domain validation changes
- backend-dev PR — appsettings.json + domain validation gevşetme + acceptance tests

### 1. AR-GE (binance-expert+architect+frontend-dev)
- **Kripto trading bot UI UX best-practices** (binance-expert araştırma)
- Dashboard layout patterns, heatmap, sparkline usage
- Türkçe finansal terim sözlüğü (realized → gerçekleşen, unrealized → gerçekleşmemiş, drawdown → düşüş, vb.)
- Typography: mevcut mono font yerine okunabilir sans-serif + tabular numbers
- Boyut: base 14px → 16px, heading'ler büyüsün

### 2. UI Reform Listesi (sıfırdan tasarla, şu anki pattern koru)
**Sayfa bazında:**
- `index.html` (Genel Bakış) — portföy özeti net, sinyaller daha görsel, canlı piyasa kompakt
- `portfolio.html` — açık pozisyonlar P/L bazlı renk, realize edilen kar/zarar grafik
- `orders.html` — aç ık/kapanan sekmeleri, her order için badge net
- `strategies.html` — strateji status (Active/Paused) büyük badge, son sinyal kartları
- `risk.html` — drawdown grafik daha net, CB durumu hero, threshold bar
- `klines.html` — kandil grafik daha büyük, indicator overlay (EMA/BB)
- `orderbook.html` — depth ladder net, spread hero
- `positions.html` — son trade'ler, P/L summary
- `logs.html` — severity renkler, filter chip'leri

### 2.5 Anlamlı KPI'lar (kullanıcı kritik)

**Kullanıcı 2026-04-18 16:42 ek geri bildirim:**
> "Başlangıç $100, Mevcut $93.84, Realized $0, Unrealized -$0.05, Fill Başarısı %100, Toplam 6 işlem, 1 açık — bu değerler **anlaşılmıyor**. Kar ne zarar ne işlemlerde **ne kadardan girdik ne kadardan sattık komisyon ne kadar kar ne zarar ne gözükmüyor** bunları da rahat anlaşılır yapsın."

**Eksik bilgiler (Loop 14'te eklenmeli):**

**Portföy özeti (üst kart):**
- **Net K/Z (toplam):** Mevcut - Başlangıç → tek hero rakam, büyük renkli (-$6.16 kırmızı veya +$X.XX yeşil)
- **Yüzde değişim:** -%6.16 (renkli)
- **Bugün gerçekleşen kar/zarar:** ayrı kart (realized today) Türkçe açık
- **Açık pozisyon değer:** mevcut "Unrealized" ama anlamlı: "Açık pozisyon kar/zarar"
- **Toplam komisyon ödenen:** yeni KPI (sum(order.cumulativeQuoteQty * 0.001))
- **Net kar = Realized - Komisyonlar** (gerçek elde edilen)

**İşlemler tablosu (orders.html) için yeni sütunlar:**
- Giriş Fiyatı (entry, mevcut)
- Çıkış Fiyatı (exit — kapanmışsa, mevcut "price")
- Adet
- Notional (qty × price)
- **Komisyon** ($ veya %0.1)
- **Net Kar/Zarar** (her trade için, renkli)
- **Süre** (open/close arası)

**Pozisyonlar tablosu (positions.html) için:**
- Sembol
- Açılış Fiyatı
- Mevcut Fiyat (mark)
- Açık Pozisyon K/Z (= (mark - entry) × qty, renkli)
- **Stop / TP fiyatları gösterilsin** (ne kadar uzakta görünür)
- Açılış Zamanı / Süre

**Trade pair view (yeni özellik):**
Her closed pozisyon için "trade pair card":
```
BTC Long #42
Açıldı: 76,500 @ 12:30
Kapandı: 76,800 @ 12:45 (Stop tetiklendi)
Adet: 0.0002
Notional: $15.30 → $15.36
Komisyon: $0.0306 toplam
Net Kar: +$0.029  (+%0.19)
```

Bu card pattern user'a "ne aldım, ne sattım, ne kazandım/kaybettim" net gösterir.

### 3. Türkçeleştirme
**İngilizce etiketler:**
- "Realized Today" → "Bugünkü Gerçekleşen K/Z"
- "Unrealized" → "Gerçekleşmemiş K/Z"
- "Fill Başarısı %" → "Doluluk Oranı"
- "iter #0" → "iterasyon #0"
- "nakit" → kalabilir
- "Reset Iteration" → "Yeniden Başlat"
- "BLOCKED/OFFLINE" → "Bloklu/Kapalı"
- "EXIT/UP/DOWN" badge'leri → "Çıkış/Yüksel/Düşüş"
- "FILLED/REJECTED/EXPIR" → "Gerçekleşti/Reddedildi/Süresi Doldu"
- Tablo başlıkları (Time, Price, Qty, Symbol, Status)

### 4. Typography + Boyut
- Base 14px → 16px
- Heading 20/24 → 24/28
- Tabular numbers (JetBrains Mono kalabilir sayı için)
- Body text sans-serif (Inter/system-ui)
- Line-height 1.4 → 1.5

### 5. Playwright ile doğrulama (tester agent)
- Her sayfa headless açılsın, screenshot alınsın
- UI'daki değerler API ile cross-check (balance, orders count, risk params, vb.)
- Her kontrol noktasında otomatik açtır (trader mode check routine)
- Tester agent: `playwright-scenario` skill ile her sayfa için senaryo

### 6. Regression pattern (ilerledikçe)
- UI değerlerinin doğruluğunu her loop health check'te verify et
- Frontend'den görünen "Mevcut Bakiye" vs API `/api/balances` uyumlu mu

---

## Agent Zinciri (Loop 14 boot)
1. **binance-expert** — Kripto bot UI UX best practices research → `loops/loop_14/research-ui.md`
2. **architect** — UI information architecture + Türkçe term sözlüğü + typography scale → `loops/loop_14/design-ui.md`
3. **frontend-dev** — Her sayfa sıfırdan tasarla (mevcut pattern koru) + Türkçeleştirme + typography → PR'ları (sayfa başı PR?)
4. **tester** — Playwright senaryoları her sayfa için → `loops/loop_14/test-ui.md`
5. **reviewer** — Final UI review + kural kontrolü

## Done
- Tüm sayfalar Türkçe
- Playwright her sayfa pass
- API değerleri UI ile uyumlu (cross-check)
- Typography büyüdü
- Tasarım anlamlı/kolay
