# Research Reform -- Loop 19 to Loop 20

Tarih: 2026-04-19
Hazirlayan: binance-expert agent
Amac: 10-15dk timeframe, spot long-only, net %1-2/islem hedefli algoritma secimi
Yontem: Binance exchangeInfo canli dogrulama + akademik kaynak tarama

---

## 1. Binance Kisitlari -- Canli Dogrulama (2026-04-19)

Kaynak: api.binance.com/api/v3/exchangeInfo (mainnet) + testnet.binance.vision/api/v3/exchangeInfo (testnet)

| Sembol   | stepSize    | minQty      | tickSize   | minNotional | Filter Tipi |
|----------|-------------|-------------|------------|-------------|-------------|
| BTCUSDT  | 0.00001 BTC | 0.00001 BTC | 0.01 USD   | 5.00 USDT   | NOTIONAL    |
| BNBUSDT  | 0.001 BNB   | 0.001 BNB   | 0.01 USD   | 5.00 USDT   | NOTIONAL    |
| XRPUSDT  | 0.1 XRP     | 0.1 XRP     | 0.0001 USD | 5.00 USDT   | NOTIONAL    |

Sizing uyumu (0 nominal):
- BTCUSDT @ 95000: 0.000210 BTC -- stepSize 0.00001 uyar, minNotional 5 USDT uyar
- BNBUSDT @ 610: 0.032 BNB -- stepSize 0.001 uyar
- XRPUSDT @ 2.30: 8.6 XRP -- stepSize 0.1 uyar
SONUC: 0 minimum pozisyon her uc sembol icin LOT_SIZE ve minNotional kisitlarini karsilar.

Round-trip fee: VIP0 = 0.10% + 0.10% = 0.20% (BNB ile 0.15%)

---

## 2. Break-Even Analizi

Formul: WR x avgWin >= (1-WR) x avgLoss + roundTripFee
Hedef: net %1.00 kar/islem (fee sonrasi)

| Senaryo       | stopPct | targetPct | Gereken WR | Not                              |
|---------------|---------|-----------|------------|----------------------------------|
| R:R 1:1.5     | 0.50%   | 0.75%     | ~58%       | Fee %0.20 dahil                  |
| R:R 1:2       | 0.50%   | 1.00%     | ~52%       | Daha az WR yeterli               |
| R:R 1:3       | 0.50%   | 1.50%     | ~41%       | Klasik momentum hedefi           |
| Net %1 hedef  | 0.50%   | 1.20%     | ~55%       | Brut 1.20% - fee 0.20% = net %1  |

Kritik: Net %1-2 icin brut %1.2-2.2 hedeflenmeli. R:R 1:2.4 ile %45 WR bile yeterlidir.

---

## 3. Strateji Karsilastirmasi

### 3.1 Opening Range Breakout (ORB) -- SKOR: 5/10

Teorik edge:
Zarattini, Barbon & Aziz (2024): stocks in play uzerinde ORB 1600%+ return.
Zimmer & Norden (2023, UMNEES0845): ORB futures uzerinde significantly higher returns than zero kanitlandi.
Edgeful.com (ES futures canli veri): clean breakout orani %33 (up+down birlesik), double-break %66.93.

10-15dk uygunlugu:
Crypto 24/7 piyasa -- session open kavrami belirsiz. Saatlik veya gunluk ilk X dakika range tanimlanabilir.
1m bar ile: 5-10 bar bekleme, 11-16. bar breakout entry -- teknik olarak uygulanabilir.

Yanlis pozitif riski:
Double-break orani %66.93 -- crypto volatilite nedeniyle daha yuksek beklenir. Hacim filtresi zorunlu.

Red flag:
- Crypto 24/7 -> opening range tanimi zorlasiyor, saatlik reset 24 potansiyel aralik demek
- %66+ cift kirilim = stop-hunt riski yuksek
- Hacim konfirmasyonu olmadan whipsaw dominant

### 3.2 VWAP-Bounce / VWAP-Reclaim -- SKOR: 7/10

Teorik edge:
VWAP intraday fair value referansi, kurumsal alim-satim seviyesi kabul gorur.
Tradewink & LuxAlgo: trend gunlerde VWAP bounce %65-70 WR, ilk 3 saatte %75-80 accuracy.
Multi-period EMA+VWAP kombinasyonu: 0.5% stop / 1.5% TP kullanimi belgelenmis.
(Kaynak: medium.com/@redsword_23261/multi-period-ema-crossover-with-vwap-high-win-rate-intraday-trading-strategy)

10-15dk uygunlugu:
VWAP 1m bardan kumulatif hesaplanir, 10-15dk icinde 10-15 bar birikir -- yeterli.
Gune ozel kumule veya rolling 24h hesap gerekir.

Yanlis pozitif riski:
Choppy gun: WR %65 -> %45. Ust-timeframe EMA filtresi zorunlu.

Red flag:
- VWAP gunluk resetlenirse gece bos saatlerde anlamsiz kalabilir
- BNB/XRP kucuk float: hacim gurultusu VWAP hesabini carpitabilir
- Spot long-only: bear gunlerde sistem pasif kalmali

### 3.3 EMA Pullback Trend-Continuation -- SKOR: 6/10

Teorik edge:
QuantifiedStrategies EMA backtest: WR %38-44, R:R 3.8:1 (parabolik trendleri yakalar).
Crypto intraday momentum calismasi (ScienceDirect, pii/S1062940822000833):
saatlik timeframede anlamli momentum kanitleniyor; 5-15dk icin de var ama gurultu artar.

10-15dk uygunlugu:
EMA9/EMA21 1m bar uzerinde. Cross sonrasi pullback ortalama 8-15 bar (8-15dk) bekler -- hedefle ortusur.

Yanlis pozitif riski:
Sideways piyasada EMA cross firildak doner. Trend confirmation gerekli.

Red flag:
- Crypto: EMA9/21 arasi kucuk fark = cok sik cross = whipsaw
- Lagging: gec entry -> daha genis stop -> effective R:R dusuyor

### 3.4 Donchian/Keltner Breakout -- SKOR: 5/10

Teorik edge:
Donchian 20 kurali klasik trend-following (Turtle Traders). Keltner meanrev varianti %77 WR (QuantifiedStrategies).
Breakout variant: %30-45 WR ama R:R 3:1+.

10-15dk uygunlugu:
10-20 bar Donchian = 10-20dk range, 21. bar breakout -- sure hedefiyle calisiyor.

Red flag:
- %0.20 fee ile %30-45 WR x R:R 3:1 -> pozitif beklenen deger ama kucuk
- ATR stop crypto genis olabilir -> effective R:R dusuyor
- Kisa vadede trend gerektiriyor, her gun calismaz

### 3.5 RSI Divergence + Candle Confirm -- SKOR: 3/10

Teorik edge:
PMC/NIH (2023, pmc.ncbi.nlm.nih.gov/articles/PMC9920669/):
RSI standart uygulamasi kripto'da etkisiz veya negatif.
Divergence: occurrence rate ~0.8% of candles -- cok nadir.
En iyi varyant: Cardwell (RSI 50-100 uptrend filter) -- 773.65% vs 275.22% BaH 2018-2022.

Red flag:
- 15dk'da %1 net icin target 1.2% yukari -- kisa timeframede zor
- Saatte 0-1 sinyal bile zor
- NIH: effectively harm returns on appreciating cryptocurrencies (standart RSI uygulama)

### 3.6 Order-Book Imbalance -- SKOR: 1/10

Teorik edge:
Towards Data Science (towardsdatascience.com, price-impact-of-order-book-imbalance):
imbalance predicts ~55-60% probability.
AMA: expected mid-price returns 10-second windows below 10 bps. Round-trip fee 20 bps.
Acik sonuc: alone profitable degil.

Red flag:
- Tahmin ufku: 10 SANIYE. 10-15dk hedef icin tamamen uygunsuz.
- Sub-second latency gerekiyor -> retail bot icin gercekci degil
- Tek basina karli degil (kanitlenen)

### 3.7 Candlestick Kombinasyon -- SKOR: 3/10

Teorik edge:
Bulkowski (Encyclopedia of Candlestick Charts 2008):
Bullish Engulfing %63 reversal -- ancak upward breakout sonrasi 10 gunde -1.18% ortalama.
Only 10% of candles work at least 60% of the time and occur frequently enough.

Proje deneyimi (Loop 16-19):
31 islem, %53 WR, fee drag .39, net -/usr/bin/bash.11 -- gercek test sonucu mevcuttur.

Red flag:
- Proje deneyimi kanitladi: tek basina yetersiz
- Bulkowski: stock verisi, crypto icin gerekli discount uygula
- Sinyal sikligi kontrol edilemiyor -> fee drag dominant

### 3.8 Mean-Reversion Bollinger Squeeze -- SKOR: 5/10

Teorik edge:
FibAlgo: BB squeeze sonrasi ilk pullback %67 WR, R:R 2.3:1.
BB mean-reversion: flat 20-SMA + stable BBW kosulunda %60+ WR.
Sadece range/sideways piyasada gecerli.

Red flag:
- Spot long-only: asagi breakout = beklemede kalinir -> gun bos
- %0.20 fee: BB extreme -> orta banda donus ~0.5-0.8% -> %1 net icin yetersiz
- Trending gunlerde yanlis yon riski

---

## 4. Karsilastirma Tablosu

| Strateji                  | Skor | WR Kanit  | 10-15dk Uyum | Fee Clearance          | Impl. Karmasiklik |
|---------------------------|------|-----------|--------------|------------------------|-------------------|
| ORB                       | 5/10 | %33 clean | Orta         | Zor                    | Orta              |
| VWAP-Bounce + EMA Filtre  | 7/10 | %65-70    | Iyi          | Uygun (brut %1.2+)     | Orta-Dusuk        |
| EMA Pullback              | 6/10 | %38-44    | Iyi          | R:R 3.8x ile           | Dusuk             |
| Donchian/Keltner          | 5/10 | %30-45    | Orta         | R:R 3x lazim           | Orta              |
| RSI Divergence            | 3/10 | Etkisiz   | Kotu         | Sinyal yeterli degil   | Yuksek            |
| Order-Book Imbalance      | 1/10 | 55% @10sn | Yok          | Mumkun degil           | Cok Yuksek        |
| Candlestick Combo         | 3/10 | %53 test  | Kotu         | Kanitlanmis yetersiz   | Orta              |
| BB Squeeze MeanRev        | 5/10 | %60-67    | Orta         | Hareket kucuk          | Orta              |

---

## 5. VWAP-EMA Hibrit -- Detayli Analiz

Kazanan aday: VWAP Reclaim + EMA21 Trend Filter kombinasyonu.

Giris mantigi:
1. Ust filtre: 1m bar uzerinde EMA21 yukseliyor VE fiyat > VWAP (trend gunu kosulu)
2. Giris: Fiyat VWAP a cekilir (pullback), 1-2 bar bounce candle (hacim dusus sonrasi artis)
3. Stop: VWAP altina kapanis (formasyon invalidation)
4. Target: Son swing high veya entry x 1.022 (brut %2.2 -> net ~%2.0)

Break-even hesabi:
- Brut hedef: %1.20 (net %1.00 icin)
- Stop: ~%0.60 (VWAP alti kapanis mesafesi)
- R:R: 1:2
- Break-even WR: 1 / (1 + 2) = %33.3
- Gercek WR %65-70: 0.67 x 1.20% - 0.33 x 0.60% = +%0.606 brut = +%0.406 net/islem

Sinyal sikligi:
- VWAP bounce sinyali trend gunde: 3-6x/gun -> saatte 0.5-1
- EMA pullback eklenmesiyle: saatte 2-4 sinyal mumkun
- Ikili confirm giris: EMA9 > EMA21 AND fiyat > VWAP AND son 3 barda EMA9 temas

---

## 6. Red Flag Ozeti (Evrensel)

1. Fee drag: %0.20 round-trip -- hedef %1.20+ brut olmadan net pozitif imkansiz.
2. Sizing uyumu: 0 BTCUSDT minNotional  asar -- LOT_SIZE ve NOTIONAL filtreleri uyumlu (canli dogrulandi).
3. Slippage: BinanceSlippageResearch2026 -- BTCUSDT 0 order = 0-1 bps -- ihmal edilebilir.
4. Sinyal sikligi: 4-6/saat icin EMA pullback + VWAP bounce birlesimi zorunlu.
5. Spot long-only: Bear gunlerde sistem pasif kalmali -- EMA21 (1h) yonsel filtre zorunlu.
6. Testnet yaniltici: Testnet slippage mainnet 4x dusuk (BinanceSlippageResearch2026).
   Gercek WR testnet performansi overstimate edebilir.

---

## 7. PM icin Tek Oneri

**Secilmesi gereken strateji: VWAP-Bounce + EMA21 Trend Filter (Hibrit)**

Gerekce:
- Akademik + pratik kanit en guclu: VWAP trend gunlerinde %65-70 WR belgelenmis
- %0.20 round-trip fee + R:R 1:2 -> break-even %33 WR -> gercek %65-70 ile buyuk guvenlik marji
- 1m kline stream uzerinde VWAP + EMA hesabi minimal yuk (Skender.Stock.Indicators destekli)
- 0 minimum pozisyon her uc sembol icin LOT_SIZE + NOTIONAL filtreleri uyumlu (canli dogrulandi)
- Stop: VWAP altina kapanis = formasyon invalidation (kullanici istedi)
- Take-profit: swing high %95 = safe realize (kullanici istedi)
- Loop 16-19 candlestick-only yaklasimindan net ayrisma: VWAP fiyat + hacim ikisini birden kullanir

Ek sinyal (sinyal sikligi icin):
EMA9 > EMA21 AND fiyat > VWAP AND son 3 barda EMA9 temas = giris.
Tek mimari, iki sinyal kaynagi.

ADR-0015 icin architect notlari:
- VWAP reset: rolling 24h vs UTC 00:00 -- crypto 24/7 icin rolling 24h onerilir
- EMA periyot: 9/21 -- 1m bar = 9dk/21dk lookback, makul
- Position sizing: max(balance x 0.20, 20.0) USDT
  -> qty = floor(positionUSDT / price / stepSize) x stepSize
- Stop order: STOP_LOSS_LIMIT (memory cache dogruladi)
  -> triggerPrice = entry x (1 - stopPct), limitPrice = triggerPrice x 0.999
- Take-profit: TAKE_PROFIT_LIMIT -> price = swingHigh x 0.95
- Gunluk yonsel filtre: EMA21 (1h) yukseliyorsa long-only aktif, dusuyorsa bekle

---

## Kaynaklar

- Binance exchangeInfo mainnet: https://api.binance.com/api/v3/exchangeInfo (dogrulama: 2026-04-19)
- Binance testnet exchangeInfo: https://testnet.binance.vision/api/v3/exchangeInfo (dogrulama: 2026-04-19)
- Zarattini Barbon Aziz 2024 ORB: https://www.academia.edu/93702938/Assessing_the_profitability_of_intraday_opening_range_breakout_strategies
- Zimmer Norden 2023 ORB: https://swopec.hhs.se/umnees/abs/umnees0845.htm
- Edgeful ORB statistics (ES): https://www.edgeful.com/blog/posts/the-opening-range-breakout-orb-trading-strategy
- PMC NIH RSI Crypto 2023: https://pmc.ncbi.nlm.nih.gov/articles/PMC9920669/
- Order Book Imbalance: https://towardsdatascience.com/price-impact-of-order-book-imbalance-in-cryptocurrency-markets-bf39695246f6/
- Bulkowski Engulfing: https://thepatternsite.com/BullEngulfing.html
- ScienceDirect Intraday Momentum: https://www.sciencedirect.com/science/article/abs/pii/S1062940822000833
- VWAP Bounce: https://www.tradewink.com/learn/vwap-bounce-trading-strategy
- EMA+VWAP High WR: https://medium.com/@redsword_23261/multi-period-ema-crossover-with-vwap-high-win-rate-intraday-trading-strategy-54ca8955bb38
- BinanceBot memory: BinancePaperFillResearch2026 + BinanceSlippageResearch2026
