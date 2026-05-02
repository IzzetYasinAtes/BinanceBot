# Loop 81 - binance-expert Spec: Pattern-Based Scalping Pivot

Tarih: 2026-05-02 | Agent: binance-expert | Task: loop81-pivot

---

## BOLUM 1: Post-Mortem (L71-L80 Analiz)

| Metrik | Deger |
|---|---|
| Toplam closed | ~60 pozisyon (L71-L80) |
| Win | ~15 (%25 WR) |
| Loss | ~45 |
| Realized PnL | -9.66 dolar (-1.93%) |

DB dogrudan sorgulanamadi (sqlcmd ODBC driver yok). Halt dosyalarindan reconstruct edildi.

### Loop-Bazli Yorunge

| Loop | WR | PnL | Teshis |
|---|---|---|---|
| L71 | ~%62 | +0.85 | KMS ilk deploy, BTC trending basarili |
| L72 | ~%35 | -0.54 | altcoin SL |
| L73 | ~%30 | -0.39 | ADX eklendi |
| L74 | ~%25 | -0.98 | ATR TP/SL, yuksek kayip |
| L75 | %21 | -0.69 | BE move |
| L76 | ~%30 | -0.61 | Trailing stop |
| L77 | %42->%25 | -2.25 | BBW=0 emit 4 ardasik SL CB tripped |
| L78 | %0 | -0.92 | BBW hard-gate, hepsi loss |
| L79 | %28.6 | -2.19 | BBR false breakdown |
| L80 | dusuk | -0.518 | 270dk 0 emit ADX gate kati |

### Win Paterni

L71-L80 kazanan kaliplari (halt dosyalarindan reconstruct):
- BTC trending KMS: EMA200 ustu, BBW>0.008, ADX>20 hepsi ayni anda
- L75 BTC TP 3 trade +0.249 dolar
- L79 BTC 10545 +0.063, 10547 +0.042
- BBR: 0/2 basari (false breakdown + timestop)

KRITIK: Win icin BTC + trending market + TUM filtrelerin ayni anda uygun olmasi gerekiyor. Nadir.

### Loss Paterni

Kategori A - Altcoin SL (~%55):
  XRP/ADA surekli SL. Range-bound, trending sayiliyor ama degil.
  L79: XRP 10544 BBR -0.281, XRP 10546 -0.139, XRP 10549 -0.160

Kategori B - Trend Reversal big loss (~%25):
  L77 t120: 4 ardasik SL -1.49 (ADA-0.37, XRP-0.37, SOL-0.38, BTC-0.37)
  5 coin ayni anda ayni bar emit = synchronize reversal tuzagi.

Kategori C - Timestop slow-bleed (~%15): -0.04 ile -0.14, 30-45dk kapanis.
Kategori D - BBR false breakdown (~%5): RSI rising yetmedi, asagi devam.

### KMS vs BBR WR

| Strateji | WR | Avg Win | Avg Loss |
|---|---|---|---|
| KMS | ~%27 | ~+0.055 | ~-0.170 |
| BBR | ~%19 | ~+0.025 | ~-0.200 |

KMS BTC: ~%38 WR. KMS XRP/ADA: ~%15 WR (kotu).

### Coin Bazinda WR

| Coin | WR | Problem |
|---|---|---|
| BTC | ~%40 | Tek karli coin |
| ETH | ~%35 | BTC benzeri |
| SOL | ~%25 | Range-bound |
| XRP | ~%15 | Surekli SL |
| ADA | ~%12 | En kotu |


---

## BOLUM 2: Pattern-Based Scalping Spec

Mimari: MultiPatternEvaluator — tek evaluator, 7 pattern, skor sistemi.
KMS + BBR KALDIRILIR, yerine MultiPatternEvaluator gelir.

### Timeframe Karari: 5m

5m kline weight=2 (1m=5x daha fazla). Testnet WS 5m stream mevcut.
1m: gürültü fazla, false signal yüksek. 5m: daha temiz pattern.
Emit frekans hedefi: saatte 30+ emit, 5 coin x 6 bar/h = 30 bar/h.

### Scoring Sistemi

Her pattern bagimsiz skor uretir (0 = pass, 1-5 arasi).
Ayni barda birden fazla pattern tetiklenebilir: skorlar TOPLANIR.
MinEmitScore: toplam skor >= 3 ise emit (parametre, ayarlanabilir).

### P1: EMA-Squeeze-Break (Skor: 3)

**Mantik:** Fiyat EMA21 ile EMA50 arasinda sikismis, BBW dusuk (range). Kirilma ani yuksek volum ile gelir.

**Tespit Kurallari:**
- BBW(20) < 0.0025 (sikisma, BBW hard-gate tersine)
- EMA(21) > EMA(50) VE son bar close > EMA(21) (yukari kirilma)
- Volume son bar > VolumeSma(20) * 1.3
- ADX(14) > 15 VE ADX(14) < 35 (cok zayif veya cok guclu trend degil)
- RSI(14) > 45 (momentum onaylamiyor ise skip)

**Entry:** Close bar kapanisinda Market order
**SL:** Low[son bar] - ATR(14)*0.5 (min %0.3, max %0.7)
**TP:** Entry + ATR(14)*2.0 (R:R 1:2 hedefe gore)
**MaxHold:** 4 bar (20dk)
**Trailing:** BE+ATR*0.3 uzerine gecince aktif
**Skor:** 3

### P2: VWAP-Bounce (Skor: 2)

**Mantik:** Fiyat VWAP altina dustukten sonra toparlar. Intraday destek olarak VWAP kritik.

**Tespit Kurallari:**
- bars[-2].close < VWAP[-2] (onceki bar VWAP altinda)
- bars[-1].close > VWAP[-1] (son bar VWAP ustune cikti)
- 40 < RSI(14) < 65 (asiri bolgede degil)
- Volume[-1] > VolumeSma(20) * 1.3
- EMA(21) yukari egimli (EMA21[-1] > EMA21[-5])

**Entry:** Close bar kapanisinda
**SL:** Low[-1] - ATR(14)*0.3 (dar SL, VWAP bounce tight)
**TP:** Entry + ATR(14)*1.5
**MaxHold:** 3 bar (15dk)
**Trailing:** Yok (kisa hedef)
**Skor:** 2

### P3: Inside-Bar-Breakout (Skor: 3-4)

**Mantik:** Daralan range (inside bar) kirilinca momentum guclu. Trend yone gore.

**Tespit Kurallari:**
- bars[-1].high < bars[-2].high VE bars[-1].low > bars[-2].low (inside bar)
- bars[0] (son tamamlanan bar) close > bars[-2].high (yukari kirilma)
  VEYA close < bars[-2].low (asagi kirilma, short -- SPOT icin yukari only)
- Volume[-1] > VolumeSma(20) * 1.5
- ADX(14) > 20 (trend varsa kirilma daha guvenilir = Skor 4)
- ADX(14) <= 20 ise Skor 3

**Entry:** Kirilma barinda
**SL:** bars[-1].low - ATR*0.5 (inside bar low altinda)
**TP:** Entry + (bars[-2].high - bars[-2].low) * 2.0 (range genisligi x2)
**MaxHold:** 5 bar (25dk)
**Trailing:** TP*0.5 gecince aktif, %0.3 trailing
**Skor:** ADX>20 ise 4, aksi 3

### P4: RSI-Oversold-Recovery (Skor: 2)

**Mantik:** RSI oversold (<35) sonrasi 2 ardasik yukari donus. BBR pattern benzeri ama daha genis.

**Tespit Kurallari:**
- RSI[-2] < 35 (oversold bolgesi)
- RSI[-1] > RSI[-2] VE RSI[0] > RSI[-1] (2 ardasik yukselis)
- RSI[0] > 38 (recovery onaylandiktan sonra)
- Close[0] > Close[-1] (fiyat da yukseliyor)
- Volume[0] > VolumeSma(20) * 1.2
- EMA200 ustu (asagi trend override riski)

**Entry:** Recovery onaylandi
**SL:** Low[-2] - ATR*0.3 (oversold dip altinda)
**TP:** EMA(21) seviyesi (dogal direnç) VEYA Entry + ATR*1.5
**MaxHold:** 4 bar
**Skor:** 2

### P5: Volume-Spike-Donchian-Break (Skor: 4)

**Mantik:** Cok yuksek volum ile Donchian kanalinin ustunu kirma. Guclu momentum sinyali.

**Tespit Kurallari:**
- Volume[-1] > VolumeSma(20) * 2.5 (cok yuksek)
- Close[-1] > DonchianHigh(20)[-2] (Donchian ustunu kiran bar)
  NOT: DonchianHigh(20) = max(high, 20 bar)
- ADX(14) > 15
- RSI(14) > 50
- BBW > 0.004 (cok dar range degil)

**Entry:** Kirilma barinda
**SL:** DonchianHigh[-2] - ATR*0.3 (eski direnec artik destek)
**TP:** Entry + ATR*2.5 (guclu momentum, genis hedef)
**MaxHold:** 6 bar (30dk)
**Trailing:** TP*0.4 gecince %0.4 trailing
**Skor:** 4

### P6: Higher-Low EMA-Touch (Skor: 2)

**Mantik:** Uptrend icinde higher low yapan fiyat EMA21 dokunusunda alim firsat.

**Tespit Kurallari:**
- Son 3 bar: Low[-2] > Low[-4] VE Low[0] > Low[-2] (higher lows)
- Close[0] >= EMA21[0] * 0.998 VE Close[0] <= EMA21[0] * 1.002 (EMA dokunusu)
- EMA21 > EMA50 (uptrend onaylandirmasi)
- ADX(14) > 20
- RSI(14) > 45 VE RSI(14) < 65

**Entry:** EMA dokunusu barinda
**SL:** Low[0] - ATR*0.4
**TP:** Entry + ATR*1.8
**MaxHold:** 4 bar
**Skor:** 2

### P7: MACD-Zero-Cross (Skor: 2)

**Mantik:** MACD line (EMA12 - EMA26) sifir cizgisini yukari kirar. Trend onaylamasi.

**Yeni Indicator Gerekli:** Indicators.Macd(bars, fast=12, slow=26)
  - macdLine = EMA(close, fast) - EMA(close, slow)
  - Min bar: 26 + warm-up = 52 bar (mevcut 200 bar yeterli)

**Tespit Kurallari:**
- MACD[-1] < 0 VE MACD[0] > 0 (sifir cizgisi yukari gecis)
- Gecis arpi buyuklugu > 0.00005 * price (kucuk crosslar filtrele)
- Volume[0] > VolumeSma(20) * 1.2
- RSI(14) > 45
- EMA200 ustu

**Entry:** Cross barinda
**SL:** Low[-1] - ATR*0.5
**TP:** Entry + ATR*2.0
**MaxHold:** 5 bar
**Skor:** 2

### Pattern Skor Ozeti

| Pattern | Skor | Kullanilan Indicator | Yeni Indicator? |
|---|---|---|---|
| P1 EMA-Squeeze-Break | 3 | EMA21, EMA50, BBW, Volume, ADX, RSI | Hayir |
| P2 VWAP-Bounce | 2 | VWAP, RSI, Volume, EMA21 | Hayir |
| P3 Inside-Bar-Break | 3-4 | Volume, ADX | Hayir |
| P4 RSI-Oversold-Recovery | 2 | RSI, Volume, EMA200 | Hayir |
| P5 Volume-Spike-Donchian | 4 | Volume, Donchian, ADX, RSI, BBW | Hayir |
| P6 Higher-Low EMA-Touch | 2 | EMA21, EMA50, ADX, RSI | Hayir |
| P7 MACD-Zero-Cross | 2 | EMA12, EMA26 (MACD line) | EVET: Macd() |

MinEmitScore = 3 (parametre). Maks teorik skor bir barda: 4+4+3 = 11.

### ADX Problemi Cozumu

L80 temel problemi: KMS ADX<20 skip + BBR ADX>=25 skip = ADX 20-25 arasi hic emit yok.
MultiPatternEvaluator bu sorunu otomatik cozer:
- Her pattern kendi ADX kuralindan sorumlu (P1: 15-35, P3: >20 bonus, vb.)
- Hicbir global ADX gate yok
- ADX 20-25 araliginda P1, P2, P4 hala emit edebilir

---

## BOLUM 3: R:R Analizi ve Fee Etkisi

### Binance Spot Testnet Fee
Testnet default: 0 komisyon (sanal). Kod simulate etmiyorsa fee=0.
Mainnet standart: maker %0.1, taker %0.1. Toplam round-trip %0.2.

### R:R Matematik

| R:R | Breakeven WR | Gereken Gercek WR |
|---|---|---|
| 1:1.0 | %50 | %55 (guvenlik marjli) |
| 1:1.5 | %40 | %45 |
| 1:2.0 | %33.3 | %38 |
| 1:2.5 | %28.5 | %33 |

Mevcut bot: WR %25 → R:R 1:2 bile yetmez. Demek ki hem WR hem de R:R iyilestirmek lazim.

**Hedef:**
- WR: %45-55 (scalping literatur normu)
- R:R: 1:1.5 ortalama (breakeven %40, biz %45+ hedef)
- Bunu saglayan: Pattern confirmation filtreler kaliteli entry uretir

### ATR-Bazli SL/TP (testnet 500 USDT)

BTC ~95000, ATR(14) 5m ~ 100-200 USDT:
  SL: ATR*0.5 = 50-100 USDT (qty=0.001 BTC → risk /usr/bin/bash.05-/usr/bin/bash.10 per trade)
  TP: ATR*1.5 = 150-300 USDT (kazanc /usr/bin/bash.15-/usr/bin/bash.30)

XRP ~0.6, ATR(14) 5m ~ 0.003-0.008:
  SL: ATR*0.5 = 0.0015-0.004 → qty=100 XRP → risk /usr/bin/bash.15-/usr/bin/bash.40
  TP: ATR*1.5 = 0.0045-0.012 → kazanc /usr/bin/bash.45-.20

KRITIK: XRP/ADA ATR SL genellikle fazla genis. MinSLPct ve MaxSLPct limiti zorunlu.
Oneri: MaxSLPct=0.006 (BTC uyumlu), XRP icin bu %1 civarinda.

---

## BOLUM 4: Risk Profili Onerileri

### Mevcut Durumun Sorunu
- MaxOpenPositions=5 → 5 coin ayni anda SL = -$1.49 (L77)
- MaxConsecutiveLosses=5 → CB cok gec devreye giriyor
- CooldownBarsAfterSignal=3 → 15dk bekleyis, emit frekansini azaltiyor

### Oneri: L81 Risk Profili

| Parametre | Mevcut | Oneri | Gerekcesi |
|---|---|---|---|
| MaxOpenPositions | 5 | 3 | Synchronize reversal riski azalir |
| MaxConsecutiveLosses | 5 | 4 | CB daha erken, daha az zarar |
| CooldownBarsAfterSignal | 3 | 2 | 10dk bekleyis (15dk yerine), frekans artar |
| MaxSlPct | 0.008 | 0.006 | XRP/ADA genis SL kisitla |
| MinEmitScore | - | 3 | Kalite filtresi |
| PerTradeRiskPct | yok | 0.003 | Pozisyon basina max risk %0.3 |

### Circuit Breaker Mantigi

MaxConsecutiveLosses=4 ile:
- 4 ardasik SL → CB Trip
- CooldownBars=10 bar (50dk) bekleme
- Reset: RiskProfileSeeder startup hook (L80 zaten implement edildi)

### PerTradeRisk Pozisyon Boyutlandirma

500 USDT sermaye, PerTradeRiskPct=0.003 → max risk per trade = 1.50 USDT
Qty = 1.50 / SL_distance
Ornek: BTC SL=100 USDT → qty=0.015 BTC
Ornek: XRP SL=0.006 USDT → qty=250 XRP (250*0.006=1.50)

NOT: Bu simdilik appsettings parametresi, backend-dev implementation gerekir.

### Trailing Stop Aktivasyon

Mevcut: KmsMomentumEvaluator score>=4 ise aktif (TrailingStopEnabled).
Oneri: MultiPatternEvaluator toplam skor >= 5 ise trailing aktif.
Trailing mesafe: ATR*0.4 (cok dar olursa erken cikiyor)

---

## BOLUM 5: Emit Frekans Tahmini

### Hesaplama

5 coin x 12 bar/h (5m timeframe) = 60 bar/h toplam.

Pattern tetiklenme orani tahmini (konservative):
- P1 EMA-Squeeze: ~%5 bar → 3 emit/h
- P2 VWAP-Bounce: ~%8 bar → 4.8 emit/h
- P3 Inside-Bar: ~%6 bar → 3.6 emit/h
- P4 RSI-Recovery: ~%5 bar → 3 emit/h
- P5 Volume-Spike: ~%3 bar → 1.8 emit/h
- P6 Higher-Low: ~%4 bar → 2.4 emit/h
- P7 MACD-Cross: ~%4 bar → 2.4 emit/h

Toplam raw emit: ~21 emit/h
MinEmitScore=3 filtresi (~%60 gecti): ~12-14 emit/h
MaxOpenPositions=3 kisitlari (-20%): ~10-12 emit/h
CooldownBarsAfterSignal=2: ~8-10 emit/h

**Gercekci hedef: saatte 8-12 emit (gunluk ~200-300 emit)**

Bu mevcut 1.5 emit/h den 5-8x iyilesme.
Hedef 30+/h ZOR: MaxOpenPositions=3 ile MaxHold=3-5 bar = max 3 ayni anda.

### Frekans Artirma Mekanizmalari

1. MinEmitScore=3 → 2 dusurmek: 2x frekans (ama quality dusar)
2. CooldownBarsAfterSignal=1: 5dk bekleme
3. Coin sayisi 5→8: +%60 frekans (BTC, ETH, SOL, XRP, ADA, BNB, MATIC, AVAX)
4. Multiple positions per coin: CooldownBarsAfterSignal=0 (risk!)

**L81 baslangic:** MinEmitScore=3, Cooldown=2. Frekans dusukse L82 icin agressive.

---

## BOLUM 6: KPI ve Basari Kriterleri

| KPI | L81 Hedef | Halt Tetigi |
|---|---|---|
| WR | >=45% | <30% (>10 trade sonra) |
| Emit/h | >=8 | <3 (60dk+) |
| Realized PnL (4h) | >= +$0.50 | < -$2.00 |
| MaxConsecutiveLoss | <=3 | 5 ardasik SL |
| AvgRR | >=1.4 | <1.0 |

### Loop 81 Basari Tanimi

DONE: MultiPatternEvaluator deploy edildi, testnet 4h calisti,
  WR >=%40, Emit >8/h, Realized PnL pozitif veya -$1.00 icerisinde.

FAIL: Realized < -$2.00 VEYA 60dk boyunca <3 emit/h.

### Monitoring Metrikleri

Her t=30dk kontrol:
- SignalEmitted, SignalSkipped, OrderFilled, PositionClosed sayilari
- Realized PnL
- Win/Loss breakdown (son 10 trade)
- Pattern bazinda emit dagilimi (hangi pattern calistiyor)

Pattern bazinda log: evaluator emit logunda PatternName="P1-EMA-Squeeze" gibi tag.

---

## BOLUM 7: Kirmizi Bayraklar (Red Flags)

### 1. Synchronize Multi-Coin Reversal
**Risk:** P5 Volume-Spike + P3 Inside-Bar ayni anda 5 coin tetikler = -$1.49 L77 tekrari.
**Onlem:** MaxOpenPositions=3 + CooldownBarsAfterSignal=2.
**Ekstra:** Ayni pattern ayni bar 3+ coin tetiklerse, skor rank et, en yuksek 3 al.

### 2. MACD Lag Problemi (P7)
**Risk:** MACD zero-cross 5m barda gec sinyal = entry geride kaliyor.
**Onlem:** P7 skor dusuk (2) tutuldu. Tek basina emit etmez (MinEmitScore=3), destek sinyali.

### 3. Donchian Breakout False Signal (P5)
**Risk:** Volume 2.5x ama manipule: pump+dump. Entry sonrasi aninda reversal.
**Onlem:** Volume kotu tarihsel: VolumeStdev yuksekse (spike normal degil) filtrele.
**Kural Ekle:** VolumeStdev(20) < VolumeSma(20)*3 (cok yuksek stdev = manipulasyon riski).

### 4. XRP/ADA Genis Spread
**Risk:** XRP/ADA spread yuksek, ATR bazli SL genellikle fazla.
**Onlem:** MaxSLPct=0.006 cap. Bu XRP/ADA icin tight SL = daha cok timestop.
**Alternatif:** XRP/ADA icin sadece P2 (VWAP-Bounce) ve P4 (RSI-Recovery) aktif.

### 5. Bollinger Band Squeeze Sonrasi Yonum Belirsizligi (P1)
**Risk:** Sikisma kirilinca yon belli degil. Sadece yukari kirilma aliniyor (spot long only).
**Onlem:** EMA21 > EMA50 AND close > EMA21 zorunlu (yon teyidi).

### 6. Higher-Low Pattern Gürültü (P6)
**Risk:** 3 bar higher-low cok kisa, normal gürültü.
**Onlem:** Skor=2 = destek sinyali, tek basina emit etmez.

### 7. VWAP Gün Ici Sifirlanma
**Risk:** VWAP her gün baslangicinda sifirlanir. Gün basi barlarda VWAP unreliable.
**Onlem:** VWAP hesabinda min 20 bar warmup (mevcut VWAP implementation kontrol et).
**Binance VWAP:** Kline data open time baz alinarak hesaplaniyor. Gün icinde birikmeli.

---

## BOLUM 8: Backend-Dev Aksiyon Listesi

### Oncelik 1 (Zorunlu — Loop 81 Calismiyor)

**AKSIYON-1: Indicators.Macd() Yeni Method**
  - Input: List<KlineBar> bars, int fast=12, int slow=26
  - Output: double (son bar MACD line degeri)
  - Algoritma: EMA(close, fast) - EMA(close, slow)
  - Dosya: src/Infrastructure/Strategies/Evaluators/Indicators.cs

**AKSIYON-2: MultiPatternEvaluator Yeni Sinif**
  - src/Infrastructure/Strategies/Evaluators/MultiPatternEvaluator.cs
  - StrategyType: MultiPattern (Domain/Strategies/StrategyEnums.cs yeni enum)
  - Parameters: MinEmitScore(3), MaxOpenPositions(3), MaxConsecutiveLosses(4),
    CooldownBarsAfterSignal(2), MaxSlPct(0.006), TrailingScoreThreshold(5)
  - 7 pattern metodu: EvaluateP1..P7, her biri skor donduruyor
  - Ana evaluate: toplam skor >= MinEmitScore ise emit
  - PatternName log tag: hangi pattern(ler) tetikledi

**AKSIYON-3: KMS + BBR Evaluator Kaldir**
  - KmsMomentumEvaluator.cs KALDIR (deprecated)
  - BbReversalEvaluator.cs KALDIR
  - KmsMomentumSnapshot.cs KALDIR
  - BbReversalSnapshot.cs KALDIR
  - Ilgili testler KALDIR
  - appsettings.json: 10 eski seed KALDIR, 5 MultiPattern seed EKLE

**AKSIYON-4: appsettings.json 5 MultiPattern Seed**
  - BTC/ETH/SOL/XRP/ADA icin 5 MultiPattern strateji
  - Her biri ayni parametreler (baslangicta uniform)
  - StrategyType = MultiPattern

### Oncelik 2 (Tavsiye)

**AKSIYON-5: VWAP Warmup Kontrolu**
  - MarketIndicatorService VWAP hesabinda min 20 bar sart mi?
  - Eksikse: bars.Count < 20 ise P2 skip

**AKSIYON-6: Pattern Tag Log**
  - Emit loguna PatternFlags: hangi pattern(ler) tetikledi
  - Ornekler: PatternFlags = P1|P3 (hem EMA-Squeeze hem Inside-Bar)

**AKSIYON-7: PerTradeRiskPct Pozisyon Boyutlandirma**
  - Simdilik 500 USDT sabit qty hesabi mevcut
  - PerTradeRiskPct=0.003 → risk-based qty
  - Backend-dev karar verir (L82 scope olabilir)

### Oncelik 3 (Backlog)

**AKSIYON-8: Coin Genisleme (8 coin)**
  - L81 basariliysa: BNB, AVAX ekle
  - MinEmitScore ayarlanmadan once L81 metrikleri bekle

---

## Kaynaklar

- Binance Spot API kline endpoint (weight=2): https://binance-docs.github.io/apidocs/spot/en/#kline-candlestick-data
- Binance WebSocket kline stream: https://binance-docs.github.io/apidocs/spot/en/#kline-candlestick-streams
- Binance filters (LOT_SIZE, PRICE_FILTER): https://binance-docs.github.io/apidocs/spot/en/#filters
- Binance rate limits (weight): https://binance-docs.github.io/apidocs/spot/en/#limits
- Binance testnet fees: https://testnet.binance.vision (optional commission simulation March 2026)
- Inside Bar pattern reference: standardized candlestick theory (Nison, Japanese Candlestick Charting)
- VWAP calculation: Volume Weighted Average Price, cumulative intraday
- Donchian Channel: Richard Donchian, turtle trading system
- MACD: Gerald Appel 1970s, EMA(12) - EMA(26)
- R:R breakeven math: WR = 1 / (1 + R:R)
- ADX Wilder smoothing: J. Welles Wilder, New Concepts in Technical Trading Systems (1978)

---

*binance-expert agent | 2026-05-02 | Loop 81 Pivot Spec Tamamlandi*

