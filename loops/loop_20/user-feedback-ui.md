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

## Uygulanma sırası (Loop 21 önerilen)

1. Hero font-size fix (5 dk, CSS only)
2. Sinyaller layout fix (30 dk, strategies.js template revizyonu)
3. Orderbook sayfası debug + rewrite (1 saat)
4. Klines autoscale + outlier filter (30 dk)
5. Frontend-dev component research + 2-3 yeni component (2 saat)

**NOT:** Bu feedback Loop 20 4h cycle'ını kesmiyor. Loop 20 normal t30/t90/t150/t210/t240 akışıyla devam edecek; Loop 21 boot anında bu dosya PM tarafından okunup frontend-dev agent'a handoff edilecek.
