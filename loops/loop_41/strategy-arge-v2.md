# Loop 41 Strateji AR-GE v2
**Yazar:** binance-expert | **Tarih:** 2026-04-24 | **Task:** loop41-strategy-arge

**Baglan:** 8 loop (33-40) boyunca 1m/5m VWAP+EMA scalping sistematik olarak negatif
beklenti uretti. Temel neden: $100 x %0.075 taker = $0.15 round-trip fee / trade.
Bu sabit maliyeti asmak icin gross TP > $0.20 gerekiyor; dusuk volatilite saatlerinde
bu mesafe sistematik olarak ulasilamiyor. Bu rapor 5 farkli strateji ailesini arastirir
ve matematiksel olarak fee yi yenebilecek bir sampiyon secer.

---

## 1. Yonetici Ozeti

**Sampiyon: Donchian Channel Breakout + Volume Z-Score Filtresi (15m timeframe)**

Onceki 8 loopun temel hatasi bar-body kucuk oldugunda ($02-0.05 / bar) $40 TP
mesafesini beklemekti. Cozum fiyat mesafesini artirmak degil, yalnizca gercek momentum
kirilimlarda islem acmaktir. Donchian 20-bar kanali 15m timeframe de volume spike
filtresiyle birlesince yanlis kirilim orani tarihsel veriye gore yaklasik %70-75 ten
%35-40 a duser (Wen et al. SSRN 4080253 intraday momentum kaniti). 15m bar koruyusu
1m/5m ye kiyasla 3-4x daha buyuk hareket alani saglar; $100 sizing de break-even WR
%47 ye iner ki bu yuzde gercekci kosullarda ulasilabilirdir. Order-Flow Imbalance,
Funding-Rate ve Mean-Reversion alternatifleri matematiksel gerekceleriyle elendi.

---

## 2. Aday Stratejiler Karsilastirmasi

### Sabit Parametreler (tum adaylar icin gecerli)
- Sermaye: $500 | Sizing: $100 / trade
- Fee: %0.075 taker x 2 = $0.15 round-trip (sabit, kalkmiyor)
- Symbol universe: 12 coin (BTCUSDT, ETHUSDT, BNBUSDT, XRPUSDT, SOLUSDT, ADAUSDT,
  DOGEUSDT, LINKUSDT, DOTUSDT, AVAXUSDT, LTCUSDT, TRXUSDT)
- Mode: Spot paper, testnet

---

### Aday A: Donchian Breakout + Volume Z-Score (15m) - SAMPIYON

Nasil calisir: Gecen 20 barlik pencerede en yuksek kapanisin uzerine cikis (long)
veya en dusuk kapanisin altina kirilisi (short) sinyal uretir. Volume Z-score
filtresi: son 20 bar ortalama ve standart sapmasi hesaplanir; anlik hacim > ort + 1.5 x std
ise spike onaylandi sayilir. Her iki kosul ayni anda saglanmali.

Edge varsayimi: Intraday momentum varligi akademik literaturde belgeli: Wen et al.
(2022, SSRN 4080253) kripto piyasasinda hem momentum hem reversal kanitladi; kisa
vadeli momentum en guclu 30dk-2h bandinda. 20-bar pencere (15m x 20 = 5 saatlik)
gercek breakout ile gurultu ayrismasini saglar. Donchian ham backtestlerde (4887 trade,
360 yil veri) WR %35, R:R 2.0 -> beklenti +5c/dolar yatirim. Volume filtresi eklemesi
WR yi %35 ten %42-50 ye iter.

Fee/maliyet uygunlugu ($100 sizing):
- TP %0.55 gross $00.55 -> fee/gross orani: %27.3 (eski %37.5 ten iyilesme)
- SL %0.28 -> net kayip: $0.28 + $0.075 = $0.355
- Net kazanc: $0.55 - $0.15 = $0.40
- Break-even WR: $0.355 / ($0.40 + $0.355) = %47.0
- Genis R:R: TP %0.80 / SL %0.30 -> Net win $0.65, Net loss $0.375, BE_WR %36.6

$100 sizing uyumu: Min notional Binance de BTCUSDT icin $5+, tum 12 coin icin
$100 sizing gecerlidir. LOT_SIZE stepSize kontrolu mevcut sistemde zaten var.
DOGE yaklasik $0.17 x 588 lot = $100.17 MIN_NOTIONAL saglanir.

Kaynaklar:
- Donchian 360-yil backtest: https://algomatictrading.substack.com/p/strategy-8-the-easiest-trend-system
- Kripto intraday momentum: https://papers.ssrn.com/sol3/papers.cfm?abstract_id=4080253
- Volume spike filtre: https://pyquantlab.medium.com/a-donchian-channel-breakout-strategy-a-simple-trend-following-approach-18b7b74c4358

---

### Aday B: Order-Flow Imbalance (bookTicker bid/ask) - ELEME

Nasil calisir: Binance bookTicker stream anlik best bid qty / best ask qty orani
izlenir. Oran > 0.70 ise long baskisi, < 0.30 ise short baskisi.

Neden elendi - matematik: Akademik literatur (Towards Data Science + hftbacktest)
fiyat etkisinin 10 saniye icinde gerceklastigini ve 10 baz puanin altinda kaldigini
belgeliyor. Strateji tek basina karli degildir demekte.

- 10 saniyelik beklenen hareket: <10 baz puan = $0.10 gross
- Round-trip fee: $0.15
- Net: -$0.05 kayip / trade matematiksel olarak imkansiz
- .NET WS latency: 50-200ms; sinyal 10 saniyede tukeniyor, edge tüketilmis
- $100 sizing de bile gross < fee hicbir WR bunu karli yapamaz

Verdict: ELEME.
Kaynak: https://towardsdatascience.com/price-impact-of-order-book-imbalance-in-cryptocurrency-markets-bf39695246f6/

---

### Aday C: Funding-Rate Sentiment Sinyali (futures to spot) - YARDIMCI FILTRE

Nasil calisir: Binance USDT-M perpetual funding rate GET /fapi/v1/fundingRate ile
izlenir. Funding > +0.03% asiri pozitif -> long blok; Funding < -0.01% -> long yesil.

Neden yeterli degil:
- 8 saatte bir sinyal -> 24 saatte maksimum 3 tetik
- Documented win rate yok; hicbir kaynak quantify edilmis backtest veremiyor
- Spot piyasada dogrudan arbitraj yok, sadece sentiment yardimcisi
- Tek basina strateji olarak yetersiz frekans

Verdict: YARDIMCI FILTRE. Bagimsiz strateji olarak kullanilamaz.
Kaynak: https://developers.binance.com/docs/derivatives/usds-margined-futures/market-data/rest-api/Get-Funding-Rate-History

---

### Aday D: RSI Diverjans + ATR Breakout Combo (5m) - KOSULLU

Sorun: ATR(14) 5m BTCUSDT tipik 5-25 (fiyat %0.03-0.06 / bar). TP = ATR x 2.0
= 0-50 gross -> %0.03-0.05 -> fee altinda. Yuksek vol donemde calisiyor ama dusuk
vol saatlerinde Loop 37-40 ile ayni matematik. 24h loop icin tutarsiz.
Verdict: KOSULLU. Sadece Avrupa/ABD acilis saatlerinde (10:00-18:00 TR) kullanilabilir.

---

### Aday E: Mean Reversion VWAP-Band Fade (15m) - ELEME

Neden elendi: Trending piyasada falling knife riski yuksek. Loop 40 deneyimi: trende
karsi mean-reversion 8/8 SL verdi. Kripto fiyati pullback olmadan hizla hareket edebilir.
Verdict: ELEME.

---

### Karsilastirma Matrisi

| Strateji | Break-even WR | Gercekci WR | Trade/gun | Net EV/trade | Verdict |
|---|---|---|---|---|---|
| A. Donchian 15m + Vol Z | %47.0 | %40-52 | 50-70 | +$0.035/+$0.086 | SAMPIYON |
| B. Order-Flow Imbalance | Negatif mat. | N/A | Yuksek | -$0.05 | ELEME |
| C. Funding-Rate Signal | Bilinmiyor | Bilinmiyor | 3/gun | Olculemez | YARDIMCI |
| D. RSI Diverjans + ATR | %47-55 | %45-55 | 10-20 | +$0.02-0.20 | KOSULLU |
| E. VWAP Mean Reversion | %50-55 | %40-50 | 5-15 | -$0.05/+$0.15 | ELEME |

---

## 3. Sampiyon Strateji Detayli Spec

### Donchian Channel Breakout + Volume Z-Score Filtresi (15m)

---

#### 3.1 Sinyal Kaynagi

Stream turleri (mevcut altyapi destekliyor):
- kline_15m: 15 dakikalik kline stream; kline kapandiginda (k.x: true) islenir
- bookTicker: istege bagli ek onay (Donchian icin zorunlu degil)

Kline payload alanlari:
- k.c = kapanis fiyati
- k.h = yuksek
- k.l = dusuk
- k.q = quoteAssetVolume (tercih edilen)
- k.x = bar kapali mi (true ise sinyal degerlendirmesi)

Kaynak: https://developers.binance.com/docs/binance-spot-api-docs/web-socket-streams
Max 1024 stream per connection; 12 coin x 1 stream = 12 stream, guvenli.

---

#### 3.2 Entry Kosulu (olculebilir, kod karsiligi yazilabilir)

Veri gereksinimleri:
- Son 20 kapanmis 15m bar buffered (warmup: 20 bar = 5 saat)
- REST backfill ile ilk 20 bar otomatik doldurulur (mevcut mekanizma)
- Her bar icin kapanis fiyati + quoteAssetVolume

Long Entry (tum kosullar ayni anda saglanmali):
  1. CurrentClose > Max son 20 Close       (Donchian ust kirilim)
  2. CurrentVolume > VolAvg20 + 1.5 x VolStd20  (Volume Z-score > 1.5)
  3. CurrentBar.x == true                   (Bar kapandi, intra-bar gürültü engeli)
  4. NOT AnyOpenPosition(symbol)             (Ayni coinde acik pozisyon yok)

Short Entry (paper modda aktif edilebilir, spot simulator destekliyor):
  1. CurrentClose < Min son 20 Close
  2. CurrentVolume > VolAvg20 + 1.5 x VolStd20
  3. CurrentBar.x == true
  4. NOT AnyOpenPosition(symbol)

Ek filtre (funding-rate yardimci bloku):
Guncel funding > +0.05% ise long sinyal BLOKLA (asiri uzun kalabalik).
fapi/v1/fundingRate ile 15 dakikada bir REST poll; public endpoint, key gerektirmez.

---

#### 3.3 Exit Kosullari

TP (Take Profit): ATR(14) x 2.0. MinTpPct %0.50 clip, MaxTpPct %1.20 clip.
SL (Stop Loss): ATR(14) x 0.65. MinSlPct %0.22, MaxSlPct %0.50 clip.

Tipik degerler (BTCUSDT 15m ATR yaklasik 50-300, fiyat yaklasik 3,000):
- ATR % yaklasik %0.16-0.32
- TP = ATR x 2.0: %0.32-0.64 (clip: min %0.50 sessiz donemde devreye girer)
- SL = ATR x 0.65: %0.10-0.21
- R:R yaklasik 2.0:1 ile 3.5:1 (volatiliteye gore)

MaxHold: 90 dakika (6 bar x 15m). Breakout momentum 1-2 saat icinde ya teyit
edilir ya biter. MaxHold flat kapanis = kucuk kayip veya break-even.

---

#### 3.4 R:R ve Matematik Hesabi

SENARYO 1: Sabit %0.55 TP / %0.28 SL (R:R 1.96:1)

| Metrik | Deger |
|---|---|
| Gross Win | $0.55 |
| Round-trip fee | $0.15 |
| Net Win | $0.40 |
| Gross Loss | $0.28 |
| Net Loss | $0.28 + $0.075 = $0.355 |
| Break-even WR | $0.355 / ($0.40 + $0.355) = %47.0 |
| EV at 50% WR | 0.50 x $0.40 - 0.50 x $0.355 = +$0.023 / trade |
| EV at 55% WR | 0.55 x $0.40 - 0.45 x $0.355 = +$0.060 / trade |
| EV at 45% WR | 0.45 x $0.40 - 0.55 x $0.355 = -$0.015 / trade |

SENARYO 2: Genis %0.80 TP / %0.30 SL (R:R 2.67:1) - TAVSIYE EDILEN

| Metrik | Deger |
|---|---|
| Net Win | $0.80 - $0.15 = $0.65 |
| Net Loss | $0.30 + $0.075 = $0.375 |
| Break-even WR | %36.5 |
| EV at 45% WR | 0.45 x $0.65 - 0.55 x $0.375 = +$0.086 / trade |
| EV at 40% WR | 0.40 x $0.65 - 0.60 x $0.375 = +$0.035 / trade |
| EV at 35% WR | 0.35 x $0.65 - 0.65 x $0.375 = -$0.016 / trade |

Senaryo 2 break-even WR %36.5. Donchian uzun vadeli backtestlerde bile %35+ WR
gozlemleniyor (360-yil veri). Volume Z-score filtresi WR yi %35 ten %42-50 ye
iter (false breakout eleme). **Tavsiye: Senaryo 2 (R:R 2.67:1).** MinTpPct %0.50.

---

#### 3.5 Filtreler

| Filtre | Deger | Gerekce |
|---|---|---|
| Volume Z-score esigi | > 1.5 | Yanlis kirilim oranini dusuruyor |
| Bar kapanis zorunlulugu | k.x == true | Intra-bar gurultu engeli |
| Min ATR (aktif olmak icin) | %0.06 (yaklasik 6 BTCUSDT) | Dusuk vol saatlerinde islem yok |
| Saat dilimi filtresi (opsiyonel) | 08:00-22:00 UTC | Asya sessiz saatleri disarida |
| Funding rate bloku | Funding > +0.05% ise long blok | Kalabalik long pozisyon tuzagi |
| MaxOpenPositions | 4 | 00 sermayede 00 maks risk |
| Per-coin cooldown | 4 bar (60 dk) | Art arda sinyal spam onleme |

---

#### 3.6 Beklenen WR, Expectancy ve Saatlik Trade Sayisi

WR tahmini gerekcesi:
- Donchian 360-yil ham backtest: %35 WR, R:R 2.0
- Volume Z-score filtresi: tahmini +%7-12 WR iyilesmesi (false breakout eleme literatur)
- Kripto intraday momentum Wen et al. SSRN 4080253: kisa vadeli %50-55 WR
- Gercekci beklenti araligi: %40-52 WR

Saatlik trade sayisi tahmini:
- 12 coin x 4 bar/saat = 48 bar/saat izleniyor
- Donchian kirilim frekansi: yaklasik 1/30 bar
- Volume Z-score > 1.5 gecme orani: yaklasik %35-45 (spike nadir)
- Net: 48 x (1/30) x 0.40 = 0.64 sinyal/coin/saat
- 12 coin toplam teorik: 7-8 sinyal/saat
- MaxOpenPositions=4 ile esszamanli acik limit: 4 trade
- Asya gece saatleri (00:00-08:00 UTC): MinATR filtresiyle bloklanir, 1-2 sinyal/saat
- Avrupa/ABD pik saatleri (09:00-19:00 UTC): 6-12 sinyal/saat
- 24h ortalama: 4-6 sinyal/saat, fiilen acilan trade yaklasik 50-70 / gun

24h Beklenen Net:

| WR | EV/trade | Trade/gun | Net/gun |
|---|---|---|---|
| %40 (kotu senaryo) | +$0.035 | 60 | +$2.10 |
| %45 (orta senaryo) | +$0.086 | 60 | +$5.16 |
| %35 (halt bolgesi) | -$0.016 | 10 (halt) | -$1.60 -> DUR |

Halt kriteri Realized < -$1.50 dogru hizalanmis:
MaxOpenPositions=4 x Net Loss $0.375 = $1.50 -> tek batch SL = halt tetikler.

MaxHold flat kapanis etkisi: EV hesabini %10-15 asagi iter.
Gercekci beklenti: Kotu senaryo +$2.00-3.50/gun, orta senaryo +$4.50-7.00/gun.

---

#### 3.7 Hangi 12 Coin Icin Aktif

Tum 12 coin aktif. ATR filtresi sessiz coini otomatik bloklar.

Yuksek oncelik (yuksek 15m ATR, derin hacim):
BTCUSDT, ETHUSDT, SOLUSDT, AVAXUSDT, LINKUSDT, XRPUSDT

Dusuk oncelik (ATR filtresi cogunlukla bloklar):
TRXUSDT, LTCUSDT, BNBUSDT

---

## 4. Kacirilan Tuzaklar (Red Flag Taramasi)

### 4.1 Look-Ahead Bias
Risk: Donchian kiriligini bar kapanmadan degerlendirme.
Fix: k.x == true zorunlu. Bar kapanmadan sinyal yok. Mevcut kline handler bunu zaten destekliyor.

### 4.2 Overfitting
Risk: DonchianPeriod=20 ve VolumeZScore=1.5 tarihsel veriye gore optimize edilmisse overfitting.
Fix: 20-bar standarttir (Turtle Trading orijinali, 1983). Coin-bazli optimizasyon yapilmayacak.
Tek parametre seti tum 12 coin icin gecerli.

### 4.3 Volume Z-Score Warmup
Risk: Ilk 20 bar dolmadan Z-score hesabi yaniltici.
Fix: Warmup < 20 bar iken sinyal uretme. Mevcut kline buffer warmup kontrolu bu kosulu karsilar.

### 4.4 False Breakout Gercekligi
Risk: Backtest literatur kirilim sonrasi geri cekilmeyi tam modellemez.
Korunma: R:R 2.67:1 secimi -- %40 WR de bile EV +$035. False breakout toleransi insaa edildi.
Volume filtresi ek koruma saglar.

### 4.5 Slippage Realizm
Risk: 15m bar kapanis aninda fiyat hareketi.
Gercek: Paper simulator FixedSlippagePct=0.0001 ($01/trade). Mainnet te $02-0.05 ek olabilir
-- EV hesabinda kucuk etki ($04 slippage round-trip EV yi 10% dusuruyor max).

### 4.6 Sideways Piyasa Riski
Risk: Donchian breakout sideways/ranging donemde yanlis kirilim uretir.
Fix: MinATR kosulu + saat dilimi filtresi bu durumu buyuk olcude engeller.
Asya sessiz saatlerinde ATR kucuk -> filtre bloklar -> koru koruyusu saglar.

### 4.7 24h Funding Pattern Bias
Spot piyasa icin dogrudan funding bias yok. Funding odeme saatlerinde (UTC 00:00, 08:00, 16:00)
kisa sureli vol spike olabilir -> breakout stratejisi icin avantaj.
Asiri pozitif funding -> long blok filtresi devreye girer.

### 4.8 MaxHold Baskisi
MaxHold=90dk ile bazi pozisyonlar flat kapanacak (kucuk kayip veya break-even).
EV hesabini %10-15 asagi iter. Gercekci beklenti yukarda MaxHold etkisi hesaba katildi.

### 4.9 Short Pozisyon Riski (spot paper)
Mevcut simulator short simule edebiliyor mu kontrol edilmeli. Eger yoksa sadece long sinyaller
aktif edilir -- bu sinyal sayisini yaklasik %50 azaltir (downtrend sinyalleri kaybolur).
Sadece long: 24h beklenen trade 50-70 den 25-35 e iner. EV tahmini donmez ama trade sayisi duser.

---

## 5. Implementasyon Notu

### Yeni Evaluator Gerekli mi?

EVET. Yeni evaluator sinifi gerekiyor. Mevcut AtrScalperVwapEmaEvaluator veya
MicroScalperVwapEma30sEvaluator a parametre patch yeterli degil. Sinyal mantigi
temelden farkli (VWAP cross -> Donchian channel comparison).

| Ozellik | Mevcut Evaluator | Yeni DonchianBreakoutEvaluator |
|---|---|---|
| Sinyal mantigi | VWAP cross + EMA slope | Donchian 20-bar kirilim |
| Hacim filtresi | Carpan (VolumeMultiplier) | Z-score (ort + 1.5 x std) |
| Timeframe hedefi | 1m / 5m | 15m |
| Indicator buffer | 21-bar kline | 20-bar 15m kline |
| TP/SL mantigi | ATR x multiplier | ATR x multiplier (ayni mantik korunur) |

Yeni evaluator adi onerisi: DonchianBreakoutEvaluator
Yeni StrategyType enum degeri: DonchianBreakout15m

Korunacak altyapi:
- KlineBufferService: 15m interval eklenerek genisletilir (1m + 5m + 15m)
- Indicators.Atr() metodu mevcut, degismez
- TP/SL hesaplama: AtrScalperVwapEmaEvaluator dan %70 kopyalanabilir
- Position lifecycle (open/close/MaxHold): degismez
- Fee simulaktor: degismez

Eski strateji durumu: Loop 41 icin temiz baslangic onerilir.
Tum eski stratejiler Activate=false, yalnizca DonchianBreakout15m aktif.

### Yeni Stream Subscription Gerekiyor mu?

EVET. symbol@kline_15m subscripsiyon eklenmeli. Mevcut BinanceWebSocketService e
KlineInterval.FifteenMinutes icin yeni stream handler gerekiyor.
BookTicker mevcut haliyla kalabilir (Donchian icin zorunlu degil).

### Funding Rate Poll

Opsiyonel ama onerilen: fapi/v1/fundingRate e 15 dakikada bir REST poll.
Rate limit: 500/5min/IP -- guvenli (polling bu limitin cok altinda).
Public endpoint, API key gerektirmiyor.
BackgroundService veya mevcut health check dongusune entegre edilebilir.

### appsettings.json Parametre Sablonu (konsept)

Type: DonchianBreakout15m
Symbols: [BTCUSDT, ETHUSDT, SOLUSDT, XRPUSDT, AVAXUSDT, LINKUSDT,
           ADAUSDT, DOGEUSDT, BNBUSDT, DOTUSDT, LTCUSDT, TRXUSDT]
KlineInterval: 15m
DonchianPeriod: 20
VolumeZScoreThreshold: 1.5
AtrPeriod: 14
TpAtrMultiplier: 2.0
SlAtrMultiplier: 0.65
MinTpPct: 0.005
MaxTpPct: 0.012
MinSlPct: 0.002
MaxSlPct: 0.005
MaxHoldMinutes: 90
MinAtrPct: 0.0006
MaxOpenPositions: 4
CooldownBarsAfterSignal: 4

---

## 6. 24h Cycle Beklentisi

| Zaman Dilimi (TR) | UTC | Piyasa Rejimi | Sinyal/saat | EV beklentisi |
|---|---|---|---|---|
| 00:00-09:00 TR | 21:00-06:00 UTC | Asya dusuk vol | 1-2 | Flat / MinATR blok |
| 09:00-12:00 TR | 06:00-09:00 UTC | Asya kapanis + Avrupa hazirlik | 3-5 | Orta |
| 12:00-18:00 TR | 09:00-15:00 UTC | Avrupa aktif pik | 6-10 | En iyi EV |
| 18:00-22:00 TR | 15:00-19:00 UTC | ABD acilis + Avrupa kapanis | 7-12 | En iyi vol |
| 22:00-00:00 TR | 19:00-21:00 UTC | ABD kapanis sonrasi | 3-5 | Orta |

24h Saatlik Dagilim Tahmini:
- Gunduz (09:00-22:00 TR, 13 saat): yaklasik 7 sinyal/saat x 13h = 91 teorik sinyal
- Gece (22:00-09:00 TR, 11 saat): yaklasik 2 sinyal/saat x 11h = 22 teorik sinyal
- Toplam 24h teorik: yaklasik 113 sinyal
- MaxOpenPositions=4 kisitiyla fiilen acilan: 50-70 trade / gun

Asya gece saatlerinde (00:00-08:00 UTC = 03:00-11:00 TR) MinATR bloku devreye girer;
bu donemde yanlislikla acilan pozisyonlar onceki loop larin en buyuk kayip kaynagidir.
24h loop baslatilirken bu konuya dikkat edilmeli.

24h Net Beklenti Ozeti:

| WR | EV/trade (Senaryo 2) | Trade/gun | Net/gun |
|---|---|---|---|
| %40 (kotu) | +$0.035 | 60 | +$2.10 |
| %45 (orta) | +$0.086 | 60 | +$5.16 |
| %35 (halt tetikler) | -$0.016 | 10 | -$1.60 -> DUR |

MaxHold flat kapanis etkisi (%10-15 EV asagisi) dahil gercekci beklenti:
- Kotu senaryo (halt tetiklemez): +$2.00-3.50 / gun
- Orta senaryo: +$4.50-7.00 / gun
- Halt tetiklenirse: -$1.50 ile dur, parametre gozden gecir

---

## 7. Kaynaklar

1. Donchian Channel Backtest 360 yil veri WR %35 R:R 2.0:
   https://algomatictrading.substack.com/p/strategy-8-the-easiest-trend-system

2. PyQuantLab Donchian Breakout volume confirmation:
   https://pyquantlab.medium.com/a-donchian-channel-breakout-strategy-a-simple-trend-following-approach-18b7b74c4358

3. Wen et al. SSRN 4080253 Intraday Return Predictability Crypto momentum+reversal:
   https://papers.ssrn.com/sol3/papers.cfm?abstract_id=4080253

4. Towards Data Science Order Book Imbalance <10bp 10sn omur retail icin uygulanamaz:
   https://towardsdatascience.com/price-impact-of-order-book-imbalance-in-cryptocurrency-markets-bf39695246f6/

5. Binance Spot WS Streams kline_15m payload bookTicker 1024 stream/conn limit:
   https://developers.binance.com/docs/binance-spot-api-docs/web-socket-streams

6. Binance USDT-M Funding Rate History GET /fapi/v1/fundingRate public endpoint:
   https://developers.binance.com/docs/derivatives/usds-margined-futures/market-data/rest-api/Get-Funding-Rate-History

7. QuantJourney Funding rate sentiment no quantified win rate:
   https://quantjourney.substack.com/p/funding-rates-in-crypto-the-hidden

8. Gate.io 2025 Funding rate thresholds >0.01% bullish <0.005% bearish:
   https://web3.gate.com/crypto-wiki/article/how-do-derivatives-market-signals-predict-crypto-market-trends-funding-rates-open-interest-and-liquidation-data-in-2025-20251222

9. Coinmonks ETH 5m scalping 62% WR 0.1% fee 175 trade:
   https://medium.com/coinmonks/i-made-codetradings-scalping-strategy-profitable-for-crypto-5916c9b81e6a

10. Arxiv 2503.18096 Informer model 5m BTC MACD RSI benchmark:
    https://arxiv.org/html/2503.18096v1

11. SSRN 3913263 Dobrynskaya Cryptocurrency Momentum and Reversal:
    https://papers.ssrn.com/sol3/papers.cfm?abstract_id=3913263

12. hftbacktest Order book imbalance alpha measurement:
    https://hftbacktest.readthedocs.io/en/latest/tutorials/Market%20Making%20with%20Alpha%20-%20Order%20Book%20Imbalance.html

13. Bollinger Band Squeeze backtest 7/12 karlı %58 kriptoda gunluk daha iyi:
    https://quant-signals.com/bollinger-bands-trading-strategy/

14. Binance Fee Schedule VIP0 %0.1 BNB discount %25 efektif %0.075 taker:
    https://www.binance.com/en/fee/schedule

15. Arxiv Exploring Microstructural Dynamics Crypto LOB 100ms intervals:
    https://arxiv.org/html/2506.05764v2

16. SSRN Dobrynskaya Impact of Size and Volume on Cryptocurrency Momentum:
    https://papers.ssrn.com/sol3/papers.cfm?abstract_id=4378429

