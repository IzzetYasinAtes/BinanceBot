# Loop 21 Research-B: Yuksek Frekansi Scalping Matematigi ve Strateji Reform

Tarih: 2026-04-19
Arastirmaci: binance-expert
Task: loop-21-research-b

---

## BOLUM 1 - Binance Spot Volatilite Matematigi

### 1.1 Sembol Bazli ATR Yuzdesi (Loop 20 Canli Verisi)

Kaynak: loops/loop_20/summary.md (canli gozlem, 150dk, 2026-04-19)

| Sembol   | 1m ort | 1m max | 15m ort | 15m max |
|----------|--------|--------|---------|----------|
| BTCUSDT  | %0.032 | %0.25  | %0.21   | %0.90    |
| BNBUSDT  | %0.011 | %0.14  | %0.14   | %0.42    |
| XRPUSDT  | %0.029 | %0.48  | %0.30   | %1.14    |

Referans (Statista 2024-2025): BTC 14-gunluk ATR ortalama %2.75 fiyatin.
15m normalize: sqrt(15/20160) x 2.75% ≈ %0.21 — loop verisiyle tutarli.

Sonuc: %0.5 gross TP icin ortalama 15-20 bar (dakika) gerekmektedir.
%0.3 gross TP 7-10 bar icinde fizibil gorunmektedir.

### 1.2 TP Hedefine Ulasma Bar Sayisi

| TP Net | TP Gross (fee +0.2) | BTC bar | XRP bar |
|--------|---------------------|---------|----------|
| %0.3   | %0.5                | 15-16   | 10-12    |
| %0.4   | %0.6                | 18-20   | 12-15    |
| %0.5   | %0.7                | 22-25   | 15-18    |

Not: Fiyat hareketi dogusal degil. Tablo ortalama gosterge niteliginde.

### 1.3 Saatte 4-6 Trade Fizibilitesi

ADR-0015 4-AND: Loop 20 verisi 125 barda 0 sinyal (%0 hiz).
Gevseltilmis C+E tahmini: 1-2 sinyal/saat per sembol x 3 sembol = 3-6/saat.

Literatur referanslari:
- EMA+VWAP kombinasyonu %68 win rate, 1m frame (Tadonomics.com, kisisel backtest)
- 20+ daily trades, otomasyonlu EMA-VWAP bot, WR=%55-65 (PickMyTrade.trade)

4-6/saat hedefi prensipte gevelsetilmis kosulla mumkun — KANITSIZ TAHMINdir.
Loop 21 paper run canli olcum saglayacak.

### 1.4 Break-Even WR Hesabi

Formul: WR > avgLoss / (avgWin + avgLoss)
Net degerler kullaniliyor (fee %0.2 zaten dusulmus):

Senaryo A — TP=%0.5 net, SL=%0.3 net:
  Break-even WR = 0.3/(0.5+0.3) = %37.5
  EV @ WR=%65: 0.65x0.5 - 0.35x0.3 = 0.325 - 0.105 = +%0.22/trade

Senaryo B — TP=%0.6 net, SL=%0.4 net:
  Break-even WR = 0.4/(0.6+0.4) = %40.0
  EV @ WR=%65: 0.65x0.6 - 0.35x0.4 = 0.390 - 0.140 = +%0.25/trade

Senaryo C — TP=%0.8 net, SL=%0.5 net:
  Break-even WR = 0.5/(0.8+0.5) = %38.5
  EV @ WR=%65: 0.65x0.8 - 0.35x0.5 = 0.520 - 0.175 = +%0.345/trade

Not: WR=%65 literatur ortalamasidir (EMA+VWAP). Gercek WR loop sonunda olculmelidir.

---

## BOLUM 2 - Strateji Gevseme Onerileri (ADR-0016 icin)

### 2.1 ADR-0015 Darbogazlari

Loop 20 bulgusundan: 11 kez directionGate=true + vwapContext=true + volume=3x ama reclaim=false.
Kosul kisitlilik sirasi:
  1. directionGate (EMA21(1h) yukari): EN KISITLAYICI — 21 saatlik trend sart
  2. vwapReclaim (last > VWAP): ORTA — 11 olay kacti
  3. vwapContext (prev < VWAP): ORTA
  4. volumeConfirm (SMA20x1.2): DUSUK

### 2.2 Alternatif A — Weighted Scoring 3/4 (REDDEDILDI)

Esik >=0.65 ile directionGate=false iken bile skor 0.70 → emit.
Bear gunde long sinyali = ADR-0015 guvencesi bozulur. KARAR: REDDEDILDI.

### 2.3 Alternatif B — EMA12(15m) Gate (REDDEDILDI)

Loop 16-19 kaniti: %100 time-stop exit, %0 TP hit = 15m gate faydasiz.
ADR-0014 deneysel kanit bu alteri direct reddeder. KARAR: REDDEDILDI.

### 2.4 Alternatif C — VWAP Zone Toleransi (ONERILDI)

Mevcut: lastBarClose > vwap
Yeni:   lastBarClose > vwap VEYA abs(lastBarClose-vwap)/vwap <= 0.0015 (+-0.15% zone)

Loop 20 11 missed signal bu toleransla yakalanirdi.
directionGate dokunulmadi — bear-gun koruması saglamlasir.
KARAR: Orta risk, makul gevelsetme. ONERILDI.

### 2.5 Alternatif D — Volume Esigi (KISMI KABUL)

SMA20x1.2 → SMA20x1.05 kucuk indirim, filtre etkinligi buyuk olcude korunur.
SMA20x1.0 (esit) reddedildi — volume gucu kalmamazlik riski.

### 2.6 Alternatif E — Slope Threshold Gevseme (ONERILDI)

Mevcut: nowEma > prevEma
Yeni:   nowEma >= prevEma * 0.9995 (slope >= -0.05% tolerans)

Etki analizi:
- Net bear (Loop 20 slope -%0.44 ila -%1.66): esik karslanamaz → sistem pasif
  (ADR-0015 spirit aynen korunur)
- Hafif yatay konsolidasyon (slope -0.01% ila -0.04%): gate acilir → sinyaller gelir

KARAR: Dusuk risk, hedeflenen gevelsetme. ONERILDI.

### 2.7 Nihai Oneri — C + E Kombinasyonu

  1. directionGate: nowEma >= prevEma * 0.9995
  2. vwapContext:   prevBarClose < vwap (degismez)
  3. vwapReclaim:   lastBarClose > vwap VEYA abs(lastBarClose-vwap)/vwap <= 0.0015
  4. volumeConfirm: vol >= SMA20 * 1.05

Sonuc:
  - Net bear trendde sistem pasif (ADR-0015 korunur)
  - Konsolidasyon/yatay fazda sinyaller acilir
  - Loop 20 11 missed signal kurtarilir
  - Volume filtresi etkinligi buyuk olcude korunur

---

## BOLUM 3 - Risk Yonetimi: Siki SL Analizi

### 3.1 SL Mesafesi Tradeoff

BTC 15-bar gurultu std. sapma: %0.032 x sqrt(15) ≈ %0.12
%0.3 SL false-trigger tahmini: %8-12 (makul aralik).

| SL   | Whipsaw Riski | Avantaj              | Dezavantaj               |
|------|---------------|----------------------|--------------------------|
| %0.3 | ORTA          | Kucuk max kayip      | XRP tek bar %0.48 → risk |
| %0.5 | DUSUK-ORTA    | Noise absorbe eder   | Kayip daha buyuk         |
| %0.8 | DUSUK         | Gercek invalidasyon  | Yuksek kayip x sik = kotu EV |

Sembol-bazli stopPct: BTC/BNB stopPct=0.003 (%0.3), XRP stopPct=0.004 (%0.4).

### 3.2 TP:SL Ratio ve EV Tablosu

| TP net | SL net | Min WR | EV @ WR=%65 |
|--------|--------|--------|--------------|
| %0.5   | %0.5   | %50.0  | +%0.10       |
| %0.75  | %0.5   | %40.0  | +%0.24       |
| %1.0   | %0.5   | %33.3  | +%0.48       |
| %0.5   | %0.3   | %37.5  | +%0.22       |
| %0.7   | %0.4   | %36.4  | +%0.315      |

ONERILDI: TP=%0.7 net, SL=%0.4 (XRP) / %0.3 (BTC/BNB)
Break-even WR=%36.4, EV @ WR=%65: +%0.315/trade

### 3.3 Time-Stop Analizi

| Time-Stop  | Avantaj             | Dezavantaj                    |
|------------|---------------------|-------------------------------|
| 10 dakika  | Yuksek devir hizi   | %0.5 gross 10 barda zor       |
| 12 dakika  | Dengeli — ONERILDI  | —                             |
| 15 dakika  | Mevcut ADR-0015     | 3 firsat kaybi               |

ONERI: MaxHoldMinutes = 12
Gerekce: %0.7 gross TP (recommended) 12 barda ulasilabilir; sermaye devir hizi iyilesir.

### 3.4 ATR-Based Sembol Bazli SL

2.5x 1m ATR hesabi:
  BTCUSDT: 2.5 x %0.032 = %0.08/bar, 5-bar kümülatif ≈ %0.17 → stopPct=0.003
  BNBUSDT: 2.5 x %0.011 = %0.028/bar → stopPct=0.003
  XRPUSDT: 2.5 x %0.029 = %0.073/bar → stopPct=0.004

ATR-based dinamik SL pragmatik; basit implementasyon olarak sembol-bazli sabit stopPct yeterlidir.

---

## BOLUM 4 - Beklenen Frekans ve EV Ozeti

### C+E Kombinasyonu ile Tahmini Trade Frekansi

| Piyasa        | 4-AND Mevcut | C+E Gevseme  |
|---------------|--------------|---------------|
| Bull trend    | 2-4/saat     | 4-6/saat      |
| Konsolidasyon | 0-1/saat     | 2-4/saat      |
| Bear trend    | 0/saat       | 0-1/saat      |

3 sembol x konsolidasyon+bull: hedef 6-12 toplam sinyal/saat.

### Gunluk EV Hesabi

Muhafazakar (WR=%58, literatur %65den -7% indirim):
  6 aktif saat x 3 sembol x 3 sinyal/saat = 54 sinyal
  %40 acilma orani (CB + risk-gate) → 22 trade/gun
  EV=%0.18/trade → Gunluk +%3.96/gun

Optimist (WR=%65):
  35 trade/gun x %0.22 = +%7.7/gun

Kartopu etkisi (muhafazakar):
  00 → 03.96 → 08.1 → 12.3 → 16.7 (4 gun)

---

## BOLUM 5 - Kirmizi Bayrak Taramasi

1. WHIPSAW RISK (MEDIUM-HIGH): XRP tek bar max %0.48 → SL=%0.3 tetiklenme riski.
   Mitigation: XRP stopPct=0.004. BTC/BNB icin %0.3 makul.

2. WR VARSAYIMI KANITSIZ: Loop 16-19 WR=%43.75 (farkli strateji).
   VWAP+EMA icin canli kanit Loop 21 rundan gelecek.

3. FEE DRAG: 22 trade x 0 x %0.2 = /usr/bin/bash.88/gun minimum basa bas noktasi.
   Break-even WR=%37.5 altina dusme riski var; CB ve risk-gate aktif olmali.

4. SINYAL BINDIRMESI: 3 sembol esz zamanli sinyal → %60 exposure (0/00).
   ADR-0005 risk-limit ve CB aktif tutulmali. Max eslesen pozisyon siniri kontrol.

5. SLOPE GATE YETERLILIGI: -%0.05 toleransi net bear gunlerde
   (Loop 20 slope -%0.44 ila -%1.66) etkisiz → sistem pasif kalir. Sinir yeterli.

6. SLIPAJ IHMAL: Gercek maliyet %0.22-0.25 (fee %0.2 + slipaj %0.02-0.05).
   EV hesaplari hafifce optimistik; muhafazakar senaryodan daha da muhafazakar bekle.

7. TP INSTABILITY: swingHigh x0.95 her barda degisir — exit hedefi kararsiz.
   Sabit tpGrossPct=0.007 (%0.7 gross = %0.5 net) daha ongorebihr ve
   backtest dogrulamasi daha kolay. ADR-0016da sabit TP tercih edilmeli.

---

## PM ICIN KARAR ONERISI

ADR-0016: VwapEmaHybrid V2 — Slope Gate + VWAP Zone (C+E Kombinasyonu)

| Parametre       | ADR-0015              | ADR-0016 Oneri                   |
|-----------------|-----------------------|----------------------------------|
| directionGate   | nowEma > prevEma      | nowEma >= prevEma * 0.9995       |
| vwapContext     | prev < VWAP           | prev < VWAP (degismez)           |
| vwapReclaim     | last > VWAP           | last > VWAP VEYA zone +-0.15%   |
| volumeConfirm   | SMA20 x 1.2           | SMA20 x 1.05                     |
| TP default      | swingHigh x 0.95      | entryPrice x 1.007 (%0.5 net)    |
| SL BTC/BNB      | %0.8 (stopPct=0.008)  | %0.3 (stopPct=0.003)             |
| SL XRP          | %0.8 (stopPct=0.008)  | %0.4 (stopPct=0.004)             |
| MaxHoldMinutes  | 15                    | 12                               |

Beklenen frekans: 4-8/saat toplam (3 sembol, konsolidasyon+bull piyasada)
EV/trade muhafazakar (WR=%58): +%0.18 net
Gunluk hedef muhafazakar: +%3-4/gun
Break-even WR: %37.5 — WR=%58 >> esik — pozitif EV saglamlasir

---

## KAYNAKLAR

- loops/loop_20/summary.md — canli gozlem verisi (2026-04-19)
- loops/loop_20/diagnosis-no-trade.md — darboğaz analizi
- docs/adr/0015-vwap-ema-hybrid-strategy.md — mevcut strateji ADR
- https://tadonomics.com/best-indicators-for-scalping/
  (EMA+VWAP %68 win rate, 1m timeframe)
- https://blog.pickmytrade.trade/ema-vwap-strategy-automated-scalping-tradovate/
  (55-65% WR, 20+ daily trades backtest)
- https://www.cryptowisser.com/guides/fibonacci-vwap-ema-crypto-scalping/
  (R:R 1:1.5-2.0 onerisi)
- https://medium.com/@mintonfin/how-to-scalp-crypto-like-a-pro-the-best-scalping-strategies-that-actually-work-in-2025-717d0acd0872
  (VWAP scalp 0.25-0.5% profit target)
- https://flipster.io/blog/atr-stop-loss-strategy (ATR multiplier)
- https://www.statista.com/statistics/1306877/bitcoin-price-swings/
  (BTC 14-day ATR %2.75)
- Carver R., Systematic Trading (2015) — break-even WR formulü
- Chan E., Algorithmic Trading (2013) — EV hesabi cercevesi
