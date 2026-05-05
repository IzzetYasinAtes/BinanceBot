# Loop 105 — Binance Expert: Entry Timing & R:R Analizi

**Agent:** binance-expert  
**Tarih:** 2026-05-05  
**Konu:** Pattern-based scalping entry timing araştırması (25 loop içgörüsü)

---

## 1. 25 Loop Pattern Analizi — Ne Yanlış, Neden

Botun 25 loop boyunca tekrarladığı sorun klasik bir **"peak entry at bar close"** problemidir. 5dk uptrend detector'lar sinyal ürettiğinde bar kapanış fiyatı zaten o bar'ın en yüksek noktasındadır (yukarı kapanış = yükseliş momenti tüketilmiş). MARKET order ile o fiyata giriş yapıldığında momentum tükendiği için fiyat geri çekilir. Mevcut mimari sinyal üretimini bar kapanışına bağlamış olup fill'i anında MARKET ile gerçekleştiriyor — bu kombinasyon **peak entry + zero edge** demektir.

İkinci sorun R:R asimetrisidir. SL -%0.40-0.50 (ATR×1.2) iken TP %0.80 hedefi, yatay/düşük volatilite pazarda nadiren ulaşılır. Fiyat TP hedefine gitmeden önce consolidation veya ters hareketle SL'e çarpıyor. Sonuç: avg win $0.04, avg loss $0.60, R:R efektif olarak 1:15 ters yönde. BE trigger %0.10 eşiği ise peak entry sonrası neredeyse hiç tetiklenmiyor çünkü fiyat open'ın %0.10 üstüne çıkmadan geri dönüyor.

---

## 2. Entry Timing — En İyi 1-2 Öneri

### Öneri A: Pullback Limit Order (Birincil Öneri)

Bar close'da sinyal üretilir ama MARKET order AÇILMAZ. Bunun yerine:

```
limitPrice = barClosePrice × (1 - pullbackPct)   // örn. pullbackPct = 0.0008 → %0.08
```

Binance Spot API'de `POST /api/v3/order` ile `type=LIMIT, side=BUY, timeInForce=GTC, price=limitPrice` gönderilir. Fiyat bar close altına çekilince fill gerçekleşir. GTC order, kullanıcı iptal edene veya trigger süresi dolana kadar açık kalır.

**Pratik timeout:** sinyal üretildikten sonra N dakika (örn. 2 bar = 10dk) içinde fill olmazsa `DELETE /api/v3/order` ile iptal. Bu şekilde "stale signal + geç fill" riski elimine edilir.

**Önerilen pullback offset:** %0.07-%0.12 (bar ATR'ının yaklaşık %30-50'si). Çok dar offset (<%0.05) → peak entry sorununu çözmez. Çok geniş (>%0.20) → fill oranı düşer, fırsatlar kaçar.

**Binance kısıtı:** Spot LIMIT order için fiyatın piyasanın ne kadar altında olabileceğine dair açık bir kısıt yoktur (Futures'ın aksine Spot'ta price cap/floor ratio Spot için yayınlanmamıştır). GTC buy limit below market price standart kullanım senaryosudur.

### Öneri B: Next Bar High Confirmation (İkincil Öneri)

Bar close'da sinyal "candidate" olarak kaydedilir, AÇILMAZ. Bir sonraki bar'ın HIGH'ı bir önceki bar'ın HIGH'ını aşarsa MARKET order açılır ("breakout confirmation"). 

**Avantaj:** sahte breakout'ların önemli bir kısmı filtrelenir (araştırma: gerçek breakout'lar için hacim %50-100 ortalamanın üstünde olmalı — bu kontrolü de ekleyebilirsin).  
**Dezavantaj:** fill fiyatı bir sonraki bar open'ından gerçekleşir → yine de peak'e yakın olabilir. Bu yüzden Öneri A'ya göre ikincildir.

**Kombinasyon:** A + B birlikte → "signal üret, limit koy, confirm olunca limit'i kapat, confirm olmadan limit timeout ile iptal et" şeklinde sıralı akış da mümkündür.

---

## 3. R:R Önerisi

**Mevcut:** SL %0.40-0.50 / TP %0.80 → R:R 1:2, win rate <%30 → negatif expectancy  
**Öneri: R:R 1:1 ile başla, win rate yeterince yüksekse tut**

Scalping literatüründe yüksek frekanslı yaklaşımlar R:R 1:1 ile çalışır; breakeven için %50+ win rate yeterlidir. Pullback entry uygulandıktan sonra win rate artacak (peak'e değil pullback'e giriliyor) — bu koşulda 1:1 veya 1:1.2 sürdürülebilirdir.

Somut parametre önerileri (ATR bazlı, sabit değil):

| Senaryo | SL | TP | BE Trigger |
|---|---|---|---|
| Mevcut (bozuk) | ATR×1.2 (~%0.40) | ATR×2.4 (~%0.80) | +%0.10 |
| Öneri — geçiş | ATR×0.8 (~%0.27) | ATR×0.8 (~%0.27) | +%0.07 |
| Öneri — hedef | ATR×1.0 (~%0.33) | ATR×1.2 (~%0.40) | +%0.08 |

SL'i daraltmak (ATR×0.8) başlangıçta loss büyüklüğünü küçültür; TP'yi de aynı oranda daraltmak win rate'i artırır. Stabilize olduktan sonra TP kademeli genişletilebilir.

---

## 4. Volatility Filter Önerisi

ATR/Price (ATR%) filtresi teorik olarak sağlamdır ve mainstream'dir. Spesifik eşik doğrulanmış sayısal veri bulunamamıştır (%0.30 değeri tahmine dayanır), bu yüzden empirik kalibrasyon gerekir.

**Önerilen yaklaşım:**

```
atrPct = atr14(5m) / closePrice × 100
if atrPct < threshold → skip emit
```

Başlangıç threshold için %0.15-%0.25 bandında test et (BTC 5m için tipik değer). Eşiğin altındaki periyotlarda bot fiyat hareketi çok küçük olduğu için ne SL ne TP tetiklenebilir hâle gelir.

**Ek filtre:** Bollinger Band Width (BBW) de benzer görevi görür ve daha yaygın kullanılır:  
`bbWidth = (upperBB - lowerBB) / middleBB`  
BBW < belirli eşik → ranging pazar → skip.

Her iki filter birbirini tamamlar: ATR% anlık volatilite, BBW son N bar'ın genişliği.

---

## 5. Backend-Dev Implementation Hints

### Pullback Limit Order (Öneri A)

- `ISignalEvaluator` → `Emit()` metodunda `EntryType = Pullback` enum değeri ekle  
- `PositionManager` veya `OrderService`'te: sinyal `Pullback` ise `limitPrice = signalPrice × (1 - PullbackOffsetPct)` hesapla  
- Binance API: `POST /api/v3/order` → `type=LIMIT, timeInForce=GTC, price=limitPrice`  
- Timeout job: sinyal timestamp'inden N dakika sonra `orderId` hâlâ `NEW` veya `PARTIALLY_FILLED` ise `DELETE /api/v3/order` çağır, pozisyonu `Cancelled` olarak kapat  
- `appsettings.json` (veya DB config): `PullbackOffsetPct`, `PullbackTimeoutMinutes`, `MinAtrPct`, `SlMultiplier`, `TpMultiplier`

### ATR% Volatility Gate

- Mevcut bar hesaplama pipeline'ına (5m kline handler) ATR14 zaten varsa → `atrPct = atr / closePrice * 100` ekle  
- `ISignalEvaluator.CanEmit()` kontrolüne: `if (atrPct < config.MinAtrPct) return false;`  
- `MinAtrPct` config key olarak dışarıda tutulmalı, hardcode yapılmamalı

### R:R Değişikliği

- `SlMultiplier = 0.8` (ATR çarpanı), `TpMultiplier = 0.8` ile başla  
- `BeTriggerPct = 0.0007` (=%0.07) — pullback'ten giriş yapıldığı için daha kolay ulaşılır  
- `TrailPct` mevcut %0.30'dan %0.20'ye çek — küçük win'leri realize etmek için

---

## Kaynaklar

- https://developers.binance.com/docs/binance-spot-api-docs/rest-api/trading-endpoints — LIMIT order, GTC, DELETE /api/v3/order
- https://fxopen.com/blog/en/5-crypto-scalping-strategies/ — pullback entry scalping
- https://fsr-develop.com/scalping-strategy-limit-orders-reliability — limit order reliability
- https://marketlab-academy.org/en/library/atr-and-atr-percent/ — ATR% formula
- https://proptradingvibes.com/blog/risk-reward-ratio-trading — R:R 1:1 vs 1:2 win rate analizi
- https://www.tradingview.com/script/VpeVyX0N-Scalper-s-Volatility-Filter-QuantraSystems/ — volatility filter mantığı
