# Kullanıcı UI Feedback — 2026-04-19 ~11:00 UTC (Loop 20 t0)

Kullanıcı not bıraktı: "Loop 20 koşsun, aşağıdakiler **bir sonraki loop'ta** (Loop 21) düzeltilecek."

---

## 1) Ana Panel (index.html) — Hero yazı boyutu çok büyük

**Nerede:** Sadece `index.html` hero bölümü.
**Durum:** "TOPLAM NET K/Z $0.00 %0.00", "MEVCUT BAKİYE $100.00", "GERÇEK ÖZKAYNAK $100.00" üçlüsünün font-size'ı aşırı büyük (~40-48px).
**İstek:** Hero rakamları biraz küçült (28-32px range). Diğer sayfalar normal — sadece burası problem.
**Çözüm:** `css/style.css` — `.hero-kpi .value` ya da equivalent class font-size düşür; padding/margin dengele. `src/Frontend/index.html` içindeki hero template'i incele.

## 2) Sinyaller bölümü (strategies.html alt kısım) — layout bozuk

**Nerede:** `src/Frontend/js/pages/strategies.js:92` — "Son Sinyaller" section.
**Durum:** Screenshot'ta:
- "XRP-VwapEma-ScalperVwapEmaHybrid" (text'ler yapışık)
- "ACTIVE" badge büyük ama kartı yok
- "SembollerXRPUSDTOluşturma13:28:52Güncelleme13:28:52" — key/value pair arası boşluk yok
- Durum dropdown stilsiz plain select
**Gerekli:** `.kv` key-value pair class uygulanmamış, `.trade-card` / `.card-grid` layout signal list'e uygulanmamış. Stratejiler kartları gibi grid'lemek.

## 3) Canlı Grafik (klines.html) — mumlar belli değil

**Nerede:** TradingView lightweight-charts BTCUSDT 1m 500 bar render.
**Durum:**
- Y-ekseni 64000-77000 arası aşırı geniş → mumlar çok ince/yoğun
- Birkaç spike (64000, 68000'e inen iğneler) scale'i bozuyor — muhtemelen testnet data quirk / outlier bar
- OHLC değerleri "75,062.59" üzerinde sabit — auto-scale + outlier filter gerek
**Çözüm:**
- `priceScale().applyOptions({ autoScale: true, scaleMargins: { top: 0.1, bottom: 0.15 } })`
- Outlier detection: |O-C|/C > %5 ise bar atla veya klip
- Bar sayısı default 200'e düşür (500 çok), zoom in başlat
- Volume histogram paneli küçült

## 4) Derinlik (orderbook.html) — sayfa bozuk/boş

**Nerede:** `src/Frontend/orderbook.html` + `js/pages/orderbook.js`
**Durum:** Screenshot'ta başlık "Derinlik" görünüyor ama içerik yok (screenshot aynı klines chart'ı iki kez görünüyordu → muhtemelen orderbook.js render etmiyor ya da layout tam collapse olmuş).
**Çözüm:**
- Playwright ile orderbook.html aç, console error / snapshot kontrol
- `/api/orderbook/snapshot?symbol=BTCUSDT` endpoint'i doğru dönüyor mu teyit
- Bid/Ask ladder kart render'ını tekrar yaz

---

## 5) Genel uçtan uca inceleme + iyileştirme araştırması

**Kullanıcı:** "genel bi uçtan uca baksın ui güzel olmuş ama bikaç komponent bikaç daha güzel bişeyler eklenebilir onları bi araştırsın"

**Loop 21 frontend-dev için open research brief:**
- Modern kripto bot dashboard örneklerini incele (Hummingbot, 3Commas, Gainium, Bitsgap UI inspirations — sadece görsel konsept)
- "Daha güzel" eklenebilecek component adayları:
  - Gerçek zamanlı trade ticker (animated marquee son 5 trade)
  - Heat-map kart: semboller × zaman dilimi, kazanç renk yoğunluğu
  - Donut chart: aktif/kapalı pozisyon dağılımı, sembol bazlı commission payı
  - Skeleton loader animasyonu daha smooth (shimmer)
  - Toast notification (trade açıldı/kapandı flash mesaj sağ alt)
  - Strategy performance timeline (son 24h trade-by-trade area chart)
  - PnL distribution histogram (kazanan/kaybeden trade büyüklük dağılımı)
  - CTA button micro-interactions (ripple, shake on error)
  - Empty-state illustration (SVG inline, hiç pozisyon yok gibi durumlarda)
- Dark theme cosmetic:
  - Neon accent glow hover (subtle box-shadow)
  - Animated gradient background hero (slow shift)
  - Numerik counter animation ($0 → $23.45 ease-out)

**Seçim prensibi:** CDN only, npm YASAK, Vue 3 composition API, 2-3 en etkili component ekle, aşırı yükleme.

---

## Ek feedback (t90 sonrası 2. tur screenshot)

**Kullanıcı:** "ansayfada da bak bu güzel gözükmüyor yazılar çok büyük, kartlar iyi olmuş ama biraz daha böyle afilli şeylere ihtiyaç var ui sal olarak"

- Ana sayfa hero rakam boyutu hala büyük — `$0.00` "TOPLAM NET K/Z" 48px+ → 28-32px aralığı
- "MEVCUT BAKİYE / GERÇEK ÖZKAYNAK" rakamları da aynı scale — küçült
- Sembol kartları ("BTC/USDT $75,691.10" + sparkline) **beğenildi, devam**
- "afilli" component istekleri:
  - Numerik counter animation (+$0 → +$0.45 ease-out geçiş)
  - Hover'da subtle neon glow / gradient border
  - Sembol kartlarının üstünde rozet (TESTNET, LIVE) varsayılan şu an sadece sol altta — hero yanına da gelebilir
  - "Son İşlemler" bölümüne boş state için illustration / animasyon (henüz işlem yok görsel rehber)
  - Hero'ya mini trend chip ("son 1h: +%0.45" gibi)
  - Canlı fiyat tikker bandı üstte (borsa stilinde kayan marquee)
  - Glassmorphism kartlara animated gradient shine (hover'da parlayan ışık çizgisi)
  - Risk / DD kartına ring progress meter (dairesel %)

## Loop 21 t60 ek feedback (2026-04-19 ~16:30 UTC)

### Kritik bug 1 — Cash negatif gösterim
Screenshot: Ana Panel `MEVCUT BAKİYE: -$18.75`. Kullanıcı: "nasıl - olabiliyor - olmaması gerek"

**Sebep (backend):**
- Sizing $20 yerine $39-40 açıyor (bug — her emir 2× büyük)
- Ardışık aynı symbol sinyal koruması yok (BNB 3 dk arayla 2 emir açmış)
- MaxOpenPositions=2 sağlandığı halde toplam notional $118 (equity $100'ün %120'si)

**Çözüm yolu:**
- Backend: `StrategySignalToOrderHandler` sizing formül denetimi — `SnowballSizing.CalcMinNotional(equity)` gerçekten $20 verir mi, override zinciri
- Backend: Aynı symbol + strategy için açık pozisyon varsa yeni sinyal skip (duplicate signal protection)
- UI cosmetic: `currentCash < 0` olduğunda hero'da "Mevcut Bakiye: -$18.75" yerine "Kullanılabilir: $0.00" + "Limit aşıldı" badge

### Bug 7 — Ana Panel hero hala çok büyük (ikinci tur feedback)
Screenshot: index.html Ana Panel hero kartı — "TOPLAM NET K/Z -$0,06", "MEVCUT BAKİYE -$18,75", "GERÇEK ÖZKAYNAK $99,94". Rakamlar Loop 21'de 42px → 30px indirilmişti ama hala büyük.

Kullanıcı: "anasayfanın içeriği ana panel hala çok büyük onu diğer sayfalar gibi yap"

**Sebep:** Hero halen 30px, diğer sayfaların normal KPI kartları ~22-24px civarında. Tutarsızlık.

**Çözüm:**
- Hero rakamlar 30px → **22-24px** (diğer sayfa KPI'ları ile aynı scale)
- Hero kartı padding-y daha da azalt (sp-5 → sp-3)
- Sub-text "%0.06", "başlangıç bakiyesi $100" 12px → 11px
- Hero kartının kendi glass bg'i korun ama **ezici görünmesin** — diğer sayfa header'larıyla aynı görsel ağırlık
- Alternatif: Hero kartı tamamen kaldır, 3 KPI'yı normal kart grid olarak göster (diğer sayfalardaki "Hızlı Metrikler" ile aynı şablon)

### Bug 6 — Sistem Olayları filtre chip'leri çalışmıyor
Screenshot: Sistem Olayları sayfası — "Tümü / Bilgi / Uyarı / Hata" chip'leri tıklanıyor ama içerik değişmiyor, hep aynı event listesi görünüyor.

**Sebep (muhtemel):**
- `logs.js` template'de chip'lere `@click="filter = 'info'"` gibi handler bağlanmış ama reactive `filter` state `usePolling` data'sına filter uygulamıyor, ya da `computed` filtered list filter'a watch etmiyor
- "Tümü" aktif chip styling'i var, diğerleri tıklanınca state güncellenmiyor veya computed yeniden çalışmıyor

**Çözüm (frontend):**
- `logs.js` setup'ta `const filter = ref('all')`
- `const filtered = computed(() => filter.value === 'all' ? items.value : items.value.filter(e => e.severity.toLowerCase() === filter.value))`
- Chip butonuna `:class="{ active: filter === 'all' }" @click="filter = 'all'"` (her chip için)
- Template'te `v-for="e in filtered"` (direkt items yerine)
- Test: 4 chip tıklanınca farklı count görünsün; empty-state filter'a göre "Uyarı yok" / "Hata yok" mesajı

### Feedback 4 — Gerçek kripto logoları
Şu an BTC/BNB/XRP/ETH için "BTC" gibi 3-harf rozet kullanılıyor (CSS dot içinde yazı). Kullanıcı: "bunların kendi logolarını kullan"

**Çözüm:**
- `src/Frontend/assets/logos/` klasörü oluştur
- SVG formatında 4 logo (BTC turuncu, ETH mavi, BNB sarı, XRP siyah) — cryptologos.cc veya Binance CDN'den alıp local kopyala (npm yok, CDN bağımlılık minimize)
- `js/components/symbolLogo.js` — prop: symbol, size; `<img src="/assets/logos/${base}.svg">` fallback harf rozet
- Kullanım noktaları: `dashboard.js` sembol carousel, `positions.js` trade kart, `orders.js` kart, `strategies.js` kart, `klines.js` chip, `priceTicker.js` marquee
- Boyut standart: 28px (kart), 20px (chip), 32px (carousel hero)

### Feedback 5 — BinanceBot marka logosu (sidebar brand)
Şu an sidebar'da `BinanceBot` yazısı + CSS dot. Kullanıcı: "bizim de bir logomuz olsun menüdeki BinanceBot kısmında"

**Çözüm:**
- Mevcut `favicon.svg` (indigo→cyan gradient + uptrend line) zaten marka. Onu daha büyük / stilize kopyasını `assets/logos/binancebot.svg` olarak koy (32-40px sidebar için)
- `js/ui.js` Sidebar component `<div class="brand">` bloğu:
  ```html
  <div class="brand">
    <img src="/assets/logos/binancebot.svg" class="brand-logo" alt="BinanceBot" width="32" height="32" />
    <span>BinanceBot</span>
  </div>
  ```
- CSS: `.brand-logo` filter drop-shadow + hover rotate 3deg micro-interaction
- Alternatif marka tasarım önerisi: "BB" monogram circuit/candlestick combo, gradient indigo→cyan, corner radius 8px

### Kritik bug 3 — ETH emir defteri 404
Screenshot: Emir Defteri → ETH chip → "404 Not Found — Snapshot for ETHUSDT not available yet."

**Sebep:** `appsettings.json` → `Binance.Symbols: ["BTCUSDT","BNBUSDT","XRPUSDT"]`. ETH orada yok, backend ETH için kline/depth stream'e abone değil, snapshot boş.

**UI ise** BTC/ETH/BNB/XRP chip'lerini hardcoded gösteriyor (index/orderbook/klines sayfalarında). Tutarsızlık.

**Çözüm yolları:**
- **A)** Backend'e ETH ekle (Symbols listesine `"ETHUSDT"`, stream otomatik subscribe, snapshot dolar). ETH strateji seed opsiyonel — izleme+sinyal ayrı.
- **B)** UI'dan ETH chip'ini kaldır (sadece backend'de olan semboller).

**A tercih** (kullanıcı ETH görmek istiyor, major sembol). Backend restart gerek, mevcut WS subscription list'i ETHUSDT@kline_1m + ETHUSDT@kline_1h + ETHUSDT@depth ekler.

### Kritik bug 2 — Position card'da TP/SL eksik
Screenshot: Pozisyonlar sayfası, kart içinde Miktar/Giriş/Maliyet/Tutulma var ama **Hedef (TP)** ve **Stop-Loss (SL)** yok.

**DB'de mevcut veri (API'den dönüyor):**
- BNB pos: `StopPrice=623.85`, `TakeProfit=628.86`
- XRP pos: `StopPrice=1.41939960`, `TakeProfit=1.43222550`

**Çözüm (frontend):**
- `positions.js` template'ine iki yeni `.kv` bloğu:
  ```
  <div class="k">Hedef Fiyat</div>
  <div class="v good">${ fmt.price(p.takeProfit) }</div>

  <div class="k">Stop-Loss</div>
  <div class="v bad">${ fmt.price(p.stopPrice) }</div>
  ```
- Mark fiyatına göre mesafe yüzdesi subtext: "+%0.48 uzak" / "-%0.32 yakın"
- TP/SL rozetli mini progress bar (opsiyonel afilli)

## Uygulanma sırası (Loop 21 önerilen)

1. Hero font-size fix (5 dk, CSS only)
2. Sinyaller layout fix (30 dk, strategies.js template revizyonu)
3. Orderbook sayfası debug + rewrite (1 saat)
4. Klines autoscale + outlier filter (30 dk)
5. Frontend-dev component research + 2-3 yeni component (2 saat)

**NOT:** Bu feedback Loop 20 4h cycle'ını kesmiyor. Loop 20 normal t30/t90/t150/t210/t240 akışıyla devam edecek; Loop 21 boot anında bu dosya PM tarafından okunup frontend-dev agent'a handoff edilecek.
