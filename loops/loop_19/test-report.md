# Loop 19 — UI Test Raporu

**Tarih:** 2026-04-19  
**Tester:** tester agent (Playwright MCP)  
**API Base:** http://localhost:5188  

---

## Sayfa Bazlı Sonuçlar

| Sayfa | Durum | Console Error | TR Etiket | Screenshot |
|---|---|---|---|---|
| index.html (Ana Panel) | GEÇTI | 1 (favicon 404, önemsiz) | Tam TR | loops/loop_19/screenshots/index.png |
| positions.html (Pozisyonlar) | GEÇTI | 0 | Tam TR | loops/loop_19/screenshots/positions.png |
| orders.html (Emir Geçmişi) | BASARISIZ | 1 (SyntaxError) | N/A — sayfa render yok | loops/loop_19/screenshots/orders.png |
| strategies.html (Stratejiler) | GEÇTI | 0 | Tam TR | loops/loop_19/screenshots/strategies.png |
| risk.html (Risk Ayarları) | GEÇTI | 0 | Tam TR | loops/loop_19/screenshots/risk.png |
| klines.html (Mum Grafikleri) | GEÇTI | 0 | Tam TR | loops/loop_19/screenshots/klines.png |
| orderbook.html (Emir Defteri) | GEÇTI | 0 | Tam TR | loops/loop_19/screenshots/orderbook.png |
| logs.html (Sistem Olayları) | GEÇTI | 0 | Tam TR | loops/loop_19/screenshots/logs.png |

---

## Ozel Kontrol Sonuclari

### index.html — Hero Metrikler
- Toplam Net K/Z: **-$0.39** — API netPnl = -0.3937 — ESLESME
- WinRate: **43.75%** — API winRate = 0.4375 — ESLESME
- Sembol Carousel: BTC / ETH / BNB / XRP kartlari mevcut, sparkline ciziliyor
- Son Islemler kart grid: 6 kart gorunur (kalanlar scroll ile)

### positions.html
- Acik pozisyon: 0 — DOGRU (API ile eslesiyor)
- Kapali islemler: 16 — DOGRU
- Kart formati: EVET (tablo yok)
- LONG/SHORT rozeti gorunur

### klines.html
- Sembol chip secici: CALISIO (BTC->BNB degisimi test edildi)
- Timeframe chip: 1m / 5m / 15m / 1s — MEVCUT
- lightweight-charts render: EVET (candle + volume)
- Price tick: canlı gosterim aktif

### strategies.html
- 3 strateji: BTC-VwapEma-Scalper, BNB-VwapEma-Scalper, XRP-VwapEma-Scalper — MEVCUT
- Tip: VwapEmaHybrid — DOGRU
- Son Sinyaller: "Bekleniyor — henuz sinyal uretilmedi" (beklenen davranis)

---

## API Cross-Check Tablosu

| Metrik | UI Degeri | API Degeri (/api/portfolio/summary) | Eslesme |
|---|---|---|---|
| Net K/Z | -$0.39 | netPnl = -0.3937612202 | EVET |
| WinRate | 43.75% | winRate = 0.4375 | EVET |
| Islem Sayisi | 16 | closedTradeCount = 16 | EVET |
| Mevcut Bakiye | $98.69 | currentCash = 98.6901850457 | EVET |
| Gercek Ozkaynak | $98.69 | trueEquity = 98.6901850457 | EVET |
| Odenen Komisyon | $1.83 | totalCommissionPaid = 1.8325012283 | EVET |
| Acik Pozisyon | 0 | openPositionCount = 0 | EVET |

---

## Build ve Test Sonucu

- `dotnet build BinanceBot.sln`: BASARILI — 0 hata, 0 uyari
- `dotnet test BinanceBot.sln`: BASARILI — 164/164 test gecti

---

## Kritik Bulgular (BLOCKER)

### BLOCKER-1: orders.html — SyntaxError: Unexpected token '.'

**Dosya:** `src/Frontend/js/pages/orders.js` satir 87

**Hata:** Template literal icinde `${{ fmt.price(o.stopPrice) }}` yazilmis.  
JavaScript `${` ifadesini template literal interpolation olarak yorumluyor; `{ fmt.price(o.stopPrice) }` gecersiz JS ifadesi oldugundan `SyntaxError: Unexpected token '.'` firlatiliyor.  
Sayfa tamamen bos render oluyor — hicbir icerik gorunmuyor.

**Etkilenen satirlar:**
```
87:  <span v-if="o.stopPrice" class="muted tiny"> · SL ${{ fmt.price(o.stopPrice) }}</span>
```

**Cozum:** `${{ fmt.price(o.stopPrice) }}` ifadesini `\${{ fmt.price(o.stopPrice) }}` veya  
template literal'den cikip Vue template interpolation'i duzeltmek gerekiyor.  
Ornek dogru yazi: `` \${{ fmt.price(o.stopPrice) }} `` (backslash ile dollar'i escape et).

---

## Ozet

- 7/8 sayfa gecti
- 1 BLOCKER: orders.html tamamen render olmuyor (JS SyntaxError)
- Favicon 404 haric hicbir console hatasi yok
- Tum etiketler Turkcede
- API deger uyumu tam (7/7 metrik eslesiyor)
