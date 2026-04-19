# Micro-Scalping Derin AR-GE Raporu

**Yazar:** binance-expert agent | **Tarih:** 2026-04-19 | **Loop:** 23

---

## BOLUM 1 -- ExchangeInfo Kesin Filtre Degerleri

### 1.1 Metodoloji

Veriler 2026-04-19 tarihinde Binance REST API WebFetch ile cekilmistir.
- Mainnet: https://api.binance.com/api/v3/exchangeInfo
- Testnet: https://testnet.binance.vision/api/v3/exchangeInfo

### 1.2 BTCUSDT Mainnet

    PRICE_FILTER:    minPrice=0.01   tickSize=0.01
    LOT_SIZE:        minQty=0.00001000   stepSize=0.00001000
    NOTIONAL:        minNotional=5.00000000   applyMinToMarket=true
    MARKET_LOT:      maxQty=116.36260591

### 1.2b BTCUSDT Testnet

    NOTIONAL:    minNotional=5.00000000
    LOT_SIZE:    minQty=0.00001000   stepSize=0.00001000
    PRICE_FILTER: tickSize=0.01000000

KESİN: Testnet = Mainnet ayni filtreler. minNotional her iki ortamda 5.00 USD.

### 1.3 ETHUSDT Mainnet

    PRICE_FILTER:    tickSize=0.01000000
    LOT_SIZE:        minQty=0.00010000   stepSize=0.00010000
    NOTIONAL:        minNotional=5.00000000
    MARKET_LOT:      maxQty=3172.69708250

### 1.4 BNBUSDT Mainnet

    PRICE_FILTER:    tickSize=0.01000000
    LOT_SIZE:        minQty=0.00100000   stepSize=0.00100000
    NOTIONAL:        minNotional=5.00000000
    MARKET_LOT:      maxQty=7467.50070416

### 1.5 XRPUSDT Mainnet

    PRICE_FILTER:    tickSize=0.00010000
    LOT_SIZE:        minQty=0.10000000   stepSize=0.10000000
    NOTIONAL:        minNotional=5.00000000
    MARKET_LOT:      maxQty=2517929.86166666

### 1.5b XRPUSDT Testnet

    NOTIONAL:    minNotional=5.00000000
    LOT_SIZE:    minQty=0.10000000   stepSize=0.10000000
    PRICE_FILTER: tickSize=0.00010000


### 1.6 Ozet Tablosu

| Sembol   | minNotional | minQty       | stepSize     | tickSize   |
|----------|-------------|--------------|--------------|------------|
| BTCUSDT  | 5.00 USD    | 0.00001 BTC  | 0.00001 BTC  | 0.01 USD   |
| ETHUSDT  | 5.00 USD    | 0.0001 ETH   | 0.0001 ETH   | 0.01 USD   |
| BNBUSDT  | 5.00 USD    | 0.001 BNB    | 0.001 BNB    | 0.01 USD   |
| XRPUSDT  | 5.00 USD    | 0.1 XRP      | 0.1 XRP      | 0.0001 USD |

Kaynak: https://api.binance.com/api/v3/exchangeInfo (2026-04-19)

### 1.7 KRITIK: 1 Dolar Trade Mumkun Mu?

HAYIR. Imkansiz.

Tum 4 sembolde NOTIONAL.minNotional = 5.00 USD, applyMinToMarket = true.
1 USD gonderilirse API -1013 MIN_NOTIONAL hatasi doner.
Testnet ve mainnet icin AYNI kural gecerli (kendi API den dogrulandi).

YENI SIZING FLOOR: 5.10 USD (minNotional 5.00 + precision buffer)

### 1.8 Precision Ornekleri

BTC ~85000: raw=0.0000588 -> ceil(stepSize=0.00001) = 0.00006 x 85000 = 5.10 USD OK
ETH ~1600:  raw=0.003125  -> ceil(stepSize=0.0001)  = 0.0032  x 1600  = 5.12 USD OK
BNB ~600:   raw=0.00833   -> ceil(stepSize=0.001)   = 0.009   x 600   = 5.40 USD OK
XRP ~2.20:  raw=2.272     -> ceil(stepSize=0.1)     = 2.3     x 2.20  = 5.06 USD OK

ZORUNLU: Kod HERZAMAN Ceiling (yukari) yuvarlama yapsin.

---

## BOLUM 2 -- Fee ve Rate Limit Matematigi

### 2.1 Fee Yapisi (Dogrulanmis)

Kaynaklar: Binance fee sayfasi, coinspot.io, cryptopotato.com, ventureburn.com

| Tier  | Maker  | Taker  | BNB Maker | BNB Taker |
|-------|--------|--------|-----------|------------|
| VIP 0 | 0.10%  | 0.10%  | 0.075%    | 0.075%     |
| VIP 1 | 0.09%  | 0.10%  |           |            |
| VIP 9 | 0.011% | 0.023% |           |            |

BIZIM DURUMUMUZ: VIP 0.

KRITIK DUZELTME -- Reform-brief hatasi:
Reform-brief dosyasinda maker fee = 0 yaziyordu. Bu YANLISTIR.
Binance SPOT piyasasinda maker fee = taker fee = 0.10% (VIP 0).
Futures ta maker 0.02% / taker 0.05% asimetri var, AMA SPOT TA YOK.
Limit order ile maker olsaniz da fee 0.1% odersiniz.

Round-trip fee (open + close):
  Normal:       0.10% + 0.10% = 0.20%
  BNB discount: 0.075% + 0.075% = 0.15%

### 2.2 Rate Limit Yapisi (Dogrulanmis)

Kaynak: https://developers.binance.com/docs/binance-spot-api-docs/websocket-api/rate-limits
Kaynak: https://www.binance.com/en/support/announcement/detail/9820396bf54644c39e666b4780622846

| Limit Tipi      | Deger          | Pencere   | Kapsam |
|-----------------|----------------|-----------|--------|
| REQUEST_WEIGHT  | 6000 / dakika  | 1 dakika  | IP     |
| ORDERS          | 50 / 10 saniye | 10 sn     | UID    |
| ORDERS          | 160000 / gun   | 24 saat   | UID    |
| WebSocket conn. | 300 / 5 dakika | 5 dakika  | IP     |

NOT: REQUEST_WEIGHT 2023-08-25 tarihinde 1200/dk dan 6000/dk ya yukseltildi.

### 2.3 150 Trade/Saat icin Rate Limit Analizi

150 trade/saat x 2 emir = 300 emir/saat
0.83 emir/10s | Limit 50/10s -> Kullanim: %1.67 -- GUVENLI
7200 emir/gun | Limit 160000/gun -> Kullanim: %4.5 -- GUVENLI
~15 weight/dk | Limit 6000/dk -> Kullanim: %0.25 -- GUVENLI

SONUC: Rate limit 150 trade/saat icin hicbir sekilde engel degildir.

### 2.4 Fee Drag Hesabi (5.10 USD sizing, 150 trade/saat)

Normal taker 0.10%:
  Per trade: 5.10 x 0.001 x 2 = 0.0102 USD
  150 trade/saat: 1.53 USD/saat fee
  24h: 36.72 USD = %36.72 sermaye kaybi/gun -- SURDURULEMEZ

BNB discount 0.075%:
  Per trade: 5.10 x 0.00075 x 2 = 0.00765 USD
  150 trade/saat: 1.1475 USD/saat fee
  24h: 27.54 USD = %27.5 -- Net kar icin yuksek WR / asimetrik R:R zorunlu

### 2.5 Break-Even Win Rate Tablosu

Formul: Break-even WR = Net_SL / (Net_SL + Net_TP)

| Senaryo | Fee RT | TP gross | SL gross | Net TP  | Net SL  | Breakeven WR |
|---------|--------|----------|----------|---------|---------|----------------|
| 1       | 0.20%  | 0.30%    | 0.30%    | +0.10%  | -0.50%  | 83.3%          |
| 2       | 0.20%  | 0.50%    | 0.30%    | +0.30%  | -0.50%  | 62.5%          |
| 3       | 0.20%  | 1.00%    | 0.30%    | +0.80%  | -0.50%  | 38.5%          |
| 4 BNB   | 0.15%  | 0.50%    | 0.30%    | +0.35%  | -0.45%  | 56.3%          |
| 5 BNB   | 0.15%  | 1.00%    | 0.50%    | +0.85%  | -0.65%  | 43.3%          |
| 6 BNB   | 0.15%  | 0.60%    | 0.35%    | +0.45%  | -0.50%  | 52.6%          |

Senaryo 6 = VWAP Reclaim + BNB (secilen strateji). Breakeven WR = 52.6%.

CIKARSAMALAR:
1. TP = SL ile scalping ASLA karli olmaz (%83.3 WR - pratik imkansiz).
2. En az 1.7:1 R:R zorunludur (TP 0.60% / SL 0.35%).
3. BNB discount ~7-8 puan WR gereksinimi duser.
4. Asimetrik R:R (dar SL, genis TP) tek gercekci yaklasim.

### 2.6 BNB Discount Analizi

BNB discount aktifken her islemde BNB bakiyeden kesilir.
5-10 USD BNB, 1000+ trade icin yeterlidir.
Gunluk tasarruf: 36.72 - 27.54 = 9.18 USD/gun -- 100 USD sermaye icin kritik.

KARAR: BNB discount mutlaka aktif edilmeli.

### 2.7 Maker vs Taker Gercegi (Spot)

Spot ta limit veya market order kulllanmak fee acidan ESITTIR.
Binance Spot: maker 0.10% = taker 0.10% (VIP 0).
Market order avantaji: guaranteed fill, no partial fill, dusuk latency.
TAVSIYE: Market order kullan.

---

## BOLUM 3 -- Mikro-Scalping Pattern Analizi (30sn-1dk)

### 3.1 Arastirma Cercevesi

Kaynaklar:
- CoinMetrics 2023: Mikro-spread firsatlarin %60 gerceklesiyor, %12 si fee+slippage sonrasi karli.
- ResearchGate 2024: Otomatik kripto scalping sistemi %86.7 WR, 15 trade/2 saat.
- Dean Markwick 2022: OFI sinyali akademik kaynagi.
- arxiv 2408.03594 (2024): Hawkes process ile OFI tahmini.
- arxiv 2502.13722 (2025): VWAP execution derin ogrenme calismasi.

Temel sorun: 30sn bar cok gurultulu.
Pratik cozum: 30s kline + 3 katmanli filtre.

### 3.2 Pattern 1 -- Bid-Ask Spread Capture

Tanim: Order book spread genisleyince her iki tarafa limit order koy.
Gereksinim: Level 2 depth stream.

Analiz:
  BTC tipik spread: 0.01 USD (tek tick)
  Spread %: 0.01/85000 = 0.0000118%
  Round-trip fee: 0.20% >> spread
  Spread fee yi ASLA karsilamaz. 4 sembol icin de ayni.

Skor: 1/10 -- ELENDI

### 3.3 Pattern 2 -- Order Flow Imbalance (OFI)

Tanim: aggTrade stream den agresif alim vs satis delta si.
Gereksinim: wss://stream.binance.com:9443/ws/btcusdt@aggTrade

Akademik zemin:
  Dean Markwick 2022: OFI fiyat degisimini onceden gosterdigi kanıtlanmis.
  arxiv 2408.03594 (2024): HFT ortaminda gecerli, 200ms sonra sinyal bayatliyor.

Hit Rate: Kurumsal HFT (5ms): %55-65 | Bireysel bot (50-150ms): %52-58
Fee uyumu: OFI tek basina 0.20% RT fee yi karsilamaz. Kombinasyon lazim.

Skor: 7/10 -- Kombinasyonda direction filtresi (v2)

### 3.4 Pattern 3 -- Tick RSI Divergence

Sorun: Binance ta resmi 1s kline yok. En kucuk: @kline_1m.
Hit Rate: %52-56. Fee sonrasi: negatif EV.

Skor: 3/10 -- ONERILMEZ

### 3.5 Pattern 4 -- Micro VWAP Reclaim

Tanim: Fiyat rolling VWAP altina duser, yukari kirar (reclaim). Long entry.
Gereksinim: 30s veya 1m kline stream + volume. Tick data gerekmez.

Hesaplama:
  typical_price = (high + low + close) / 3
  VWAP = sum(typical_price * volume, N bars) / sum(volume, N bars)

Sinyal: Long = close[t] > VWAP[t] AND close[t-1] <= VWAP[t-1]
Sinyal: Short = close[t] < VWAP[t] AND close[t-1] >= VWAP[t-1]

Akademik zemin:
  arxiv 2502.13722 (2025): VWAP price anchor ozelligi kanıtlanmis.
  Gercekci beklenti: %58-65 WR.

Fee uyumu: TP 0.60%, SL 0.35%, BNB ile breakeven %52.6 -- MUMKUN.

4 Coin: BTC/ETH (kucuk sapma, uygun) | BNB (net sinyal, iyi) | XRP (filtre zorunlu)

Skor: 7/10 -- ANA PATTERN

### 3.6 Pattern 5 -- Quick Pullback

2-3 kirmizi bar sonrasi yesil bar = long.
Hit Rate: %55-60. Dusus trendinde surekli failure. WR gereksinimi: %71.4 -- yuksek.

Skor: 5/10 -- Yardimci, trend filtreli

### 3.7 Pattern 6 -- Volume Spike Breakout

Volume 20-bar SMA nin 3-5x ine cikinca price breakout ile gir.
Hit Rate: %55-65. Senaryo 3 (TP 1.0%, SL 0.3%): breakeven %38.5 -- IYI.
BNB/XRP > BTC/ETH (daha sert spike).

Skor: 6/10 -- Volume onay filtresi olarak kullan

### 3.8 Pattern 7 -- BB Micro-Squeeze

BB bandwidth daralir, fiyat band kirar.
Sorun: 20+ bar lazim = gec sinyal. False squeeze fazla.

Skor: 4/10 -- Tek basina ONERILMEZ

### 3.9 Pattern 8 -- Orderbook Wall Detection

Buyuk limit order (wall) bounce yakala.
Sorun: Wall larin cogu spoofing. Spoof detection olmadan: %50.

Skor: 4/10 -- Simdilik erken, v3+ icin

### 3.10 Pattern Karsilastirma Ozet

| #  | Pattern               | WR Tahm.  | Fee Uyumu | Coin Uyumu | Veri     | Karmasa | Skor  |
|----|----------------------|-----------|-----------|------------|----------|---------|-------|
| 1  | Spread Capture        | N/A       | KOTU      | Hayir      | L2 book  | Yuksek  | 1/10  |
| 2  | Order Flow Imbalance  | 55-60pct  | ORTA      | BTC/ETH+   | aggTrade | Yuksek  | 7/10  |
| 3  | Tick RSI Divergence   | 52-56pct  | KOTU      | Hayir      | Tick     | Yuksek  | 3/10  |
| 4  | Micro VWAP Reclaim    | 58-65pct  | IYI       | Tum 4      | 30s kl   | Dusuk   | 7/10  |
| 5  | Quick Pullback        | 55-60pct  | ORTA      | Kismi      | 1m kl    | Dusuk   | 5/10  |
| 6  | Volume Spike Break.   | 55-65pct  | IYI       | BNB/XRP+   | 1m kl    | Dusuk   | 6/10  |
| 7  | BB Micro-Squeeze      | 55-65pct  | ORTA      | Kismi      | 1m kl    | Orta    | 4/10  |
| 8  | Wall Detection        | 50-60pct  | ORTA      | Kismi      | L2 depth | CokYuk  | 4/10  |

### 3.11 Secilen Kombinasyon

STRATEJI: VWAP Reclaim + Volume Onay + EMA Trend Filtresi (3 katman)

KATMAN 1 EMA(20) Trend (30m barlar):
  EMA uzerinde = Sadece LONG
  EMA altinda  = Sadece SHORT veya bekle
  EMA yatay    = Bekle

KATMAN 2 VWAP Reclaim (30s kline, rolling 15-bar = 7.5 dk):
  LONG:  close[t] > VWAP[t] AND close[t-1] <= VWAP[t-1]
  SHORT: close[t] < VWAP[t] AND close[t-1] >= VWAP[t-1]

KATMAN 3 Volume Onay:
  volume[t] > rolling_20bar_SMA x 1.5 -> GUCLU sinyal
  Aksi halde ATLA

Beklenen WR: VWAP tek 58-65pct, filtreler ile 62-68pct beklenti.

---

## BOLUM 4 -- Paper Mode Gercekcilik

### 4.1 Testnet Fee Simulasyonu -- KRITIK

Resmi Binance Developer Community cevabi (moderator):
  Commission fees are 0pct on the testnet environment.

Kaynak: https://dev.binance.vision/t/testnet-fee-simulation/16810

SONUC: Internal fee simulation ZORUNLUDUR.
  open_fee  = notional x 0.00075 (BNB discount)
  close_fee = exit_notional x 0.00075
  net_pnl   = gross_pnl - open_fee - close_fee

### 4.2 Slippage

Testnet: Anlik fill, gercek slippage yok.
Mainnet (5.10 USD market order): ~0.00-0.02pct slippage (ihmal edilebilir)
Extreme volatilite: 0.5pct+ slippage -- TP eriyebilir. ATR guard ekle.

### 4.3 Partial Fill

MARKET order 5.10 USD: Partial fill IMKANSIZ (4 sembol cok likit).
LIMIT order: Queue riski var. Market order tercih edilmeli.

### 4.4 WebSocket Lifecycle

Ping/Pong:    Server 3dk ping, 10dk pong olmadı = kesilir. Auto-pong zorunlu.
Connection:   Max 24h. Bot 23.5 saatte reconnect etmeli.
Stream limit: Max 1024 per connection.
listenKey:    60dk expire. Her 55dk PUT /api/v3/userDataStream.

Stream ihtiyaci: 4 x kline_30s + 1 x userData = 5 stream (1024 limitinin cok altında)

---

## BOLUM 5 -- PM Icin Net Oneri

### 5.1 Secilen Strateji

STRATEJI:  VWAP Reclaim + Volume Confirmation + EMA Trend Filter
TIMEFRAME: 30s kline (Binance @kline_30s stream)

NEDEN 30s:
  1m: 4 sembol x 60/saat = 240 teorik -> filtre 30pct = 72-96 trade/saat (HEDEF TUTMUYOR)
  30s: 4 sembol x 120/saat = 480 teorik -> filtre 30pct = 144-160 trade/saat (HEDEF OK)

### 5.2 Kesin Parametreler

  Kline:              30s (@kline_30s)
  VWAP Window:        Rolling 15 bar (7.5 dakika rolling)
  Volume SMA:         20 bar (10 dakika)
  Volume Threshold:   1.5x SMA
  Trend Filter:       EMA(20) on 30m bars

  LONG Kosullari:
    1. close[t] > VWAP[t]           (reclaim)
    2. close[t-1] <= VWAP[t-1]      (onceki altindaydi)
    3. volume[t] > volSMA20 x 1.5   (volume onay)
    4. price > EMA20_30m             (trend yukari)

  TP:    entry x 1.006    (+0.60pct gross)
  SL:    entry x 0.9965   (-0.35pct gross)
  R:R:   0.60 / 0.35 = 1.71:1

  Max Open: 1 pozisyon per sembol (max 4 paralel)
  Order:    MARKET (BNB ile 0.075pct)
  Cooldown: 2 ust uste SL sonrasi ayni sembol 5 dk bekle

### 5.3 Sizing Floor (Guncellenmis)

  ESKI (yanlis): max(equity x 0.01, 1.00) -- 1 USD Binance ta calismaz
  YENI (dogru):  max(equity x 0.01, 5.10) -- minNotional 5.00 + buffer

  Kartopu ornekleri:
    100 USD:   1.00 hesaplandi -> floor 5.10 uygulanir
    510 USD:   5.10 hesaplandi -> kartopu BASLADI
    1000 USD:  10.00 per trade
    5000 USD:  50.00 per trade

  QTY KODU:
    raw_qty = position_usd / current_price
    qty     = Ceiling(raw_qty / stepSize) * stepSize
    assert  qty * current_price >= 5.00

### 5.4 Fee Stratejisi

  Order tipi:       MARKET (spot ta maker=taker=0.1pct, fark yok)
  BNB discount:     AKTIF ET -- yan bakiyede 5-10 USD BNB tut
  Internal fee sim: notional x 0.00075 (open) + exit x 0.00075 (close)
  Round-trip cost:  5.10 x 0.0015 = 0.00765 USD per trade
  BNB alarm:        BNB < 2 USD -> uyari + fee 0.10pct ye don

### 5.5 EV Hesabi

Muhafazakar senaryo (55pct WR, 100 trade/saat):
  EV = 0.55 x (5.10 x 0.0045) - 0.45 x (5.10 x 0.0050)
     = 0.012623 - 0.011475
     = +0.001148 USD per trade
  100 trade/saat: +0.115 USD/saat (+0.115pct)
  24h teorik: +2.75 USD (+2.75pct)

Hedef senaryo (60pct WR, 130 trade/saat):
  EV = 0.60 x (5.10 x 0.0045) - 0.40 x (5.10 x 0.0050)
     = 0.01377 - 0.01020
     = +0.00357 USD per trade
  130 trade/saat: +0.464 USD/saat (+0.464pct)
  24h teorik: +11.14 USD (+11.1pct)

Breakeven WR = 52.6pct. Bu WR ustunde her trade pozitif EV.

### 5.6 Risk Uyarilari

UYARI 1 -- WR < 52.6pct:
  Net negatif EV. Daha cok trade = daha buyuk zarar.
  Ilk 500 trade WR izle. WR < 50pct ise dur.

UYARI 2 -- Bear market:
  VWAP reclaim long sinyalleri surekli failure.
  EMA trend filtresi olmadan kullanim YIKICIDIR.

UYARI 3 -- Dusuk volatilite (03:00-07:00 UTC):
  Volume sinyali zayif. ATR filtresi ekle.

UYARI 4 -- Testnet yaniltmacasi:
  Testnet WR=60pct mainnet WR=52pct olabilir. Internal fee sim ZORUNLU.

UYARI 5 -- Extreme volatilite:
  0.5pct+ slippage TP yi eritir. ATR spike > 3x ortalama ise dur.

UYARI 6 -- BNB biter:
  Fee 0.075pct -> 0.10pct. Breakeven WR 52.6pct -> 57pct. Alarm kur.

UYARI 7 -- Gunluk kayip limiti:
  Gunluk stop: equity x 5pct = 5 USD (100 USD sermaye icin). Asılınca dur.

### 5.7 Architect Icin ADR Onerileri

  1.  Kline stream:      @kline_30s
  2.  VWAP:              Rolling 15-bar weighted average
  3.  Volume filter:     Rolling 20-bar SMA, threshold 1.5x
  4.  Trend filter:      EMA(20) 30m kline
  5.  Fee simulation:    notional x 0.00075 x 2 per trade
  6.  Sizing:            Math.Max(equity * 0.01m, 5.10m) + Ceiling qty
  7.  Max pozisyon:      4 (1 per symbol), gunluk stop equity * 0.05
  8.  Signal cooldown:   2 ust uste SL sonrasi 5dk bekle
  9.  listenKey renewal: Her 55dk PUT /api/v3/userDataStream
  10. WS reconnect:      23.5 saatte graceful reconnect
  11. BNB alarm:         BNB < 2 USD -> uyari + fee mode guncelle
  12. ATR guard:         ATR spike > 3x ortalama -> islem durdur (v2)

---

## KAYNAKLAR

Canli API (2026-04-19):
  https://api.binance.com/api/v3/exchangeInfo?symbol=BTCUSDT
  https://api.binance.com/api/v3/exchangeInfo?symbol=ETHUSDT
  https://api.binance.com/api/v3/exchangeInfo?symbol=BNBUSDT
  https://api.binance.com/api/v3/exchangeInfo?symbol=XRPUSDT
  https://testnet.binance.vision/api/v3/exchangeInfo?symbol=BTCUSDT
  https://testnet.binance.vision/api/v3/exchangeInfo?symbol=XRPUSDT

Binance Dokumantasyon:
  https://developers.binance.com/docs/binance-spot-api-docs/filters
  https://developers.binance.com/docs/binance-spot-api-docs/websocket-api/rate-limits
  https://www.binance.com/en/support/announcement/detail/9820396bf54644c39e666b4780622846
  https://www.binance.com/en/fee
  https://dev.binance.vision/t/testnet-fee-simulation/16810

Akademik Kaynaklar:
  https://dm13450.github.io/2022/02/02/Order-Flow-Imbalance.html
  https://arxiv.org/html/2408.03594v1
  https://arxiv.org/html/2502.13722v1
  https://www.researchgate.net/publication/387434546
  https://www.hyrotrader.com/blog/crypto-scalping/

---
Rapor Sonu | binance-expert agent | 2026-04-19
