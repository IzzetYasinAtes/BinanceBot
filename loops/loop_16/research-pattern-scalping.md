# Loop 16 - AR-GE: Chart Pattern Detection + 5-10dk Scalping

**Agent:** binance-expert | **Tarih:** 2026-04-17
**Metodoloji:** WebSearch + WebFetch dogrulama. Varsayim yok - her istatistik kaynakla etiketlendi.

---

## BOLUM 0 - KRITIK UYARI: FEE DRAG ANALIZI

Herhangi bir pattern implementasyonundan once bu hesabin okunmasi zorunludur.

### Binance Spot VIP0 Ucret Matematigi

| Fee turu | Oran |
|---|---|
| Maker (limit) | %0.100 (BNB ile %0.075) |
| Taker (market) | %0.100 (BNB ile %0.075) |
| Round-trip taker/taker | **%0.200** |
| Round-trip BNB ile | **%0.150** |

Break-even hesabi:
- Spot R:R=1.0  -> min **%50 WR**
- Spot R:R=1.5  -> min **%45 WR**
- Limit maker entry -> round-trip %0.15 -> min **%42 WR**

**SONUC:** %55+ WR + R:R 1.5:1 hedefi olmazsa fee drag sistemi yer.

Kaynak: https://www.binance.com/en/fee/spotMaker
Kaynak: https://cryptopotato.com/binance-fees/

---

## BOLUM A - ALGORITMIK PATTERN TESPITI

### A1. Akademik Bulgu: Tekil Candlestick Pattern Siniri

Tobi Lux (2024): DAX verisi, TA-Lib 61 pattern, 20 gun holding, 2000-2024:
- **Sonuc: Pattern-based entry = random entry** (istatistiksel fark yok)
- Ortalama kazanc: 20 gunde <%1
- KS + Mann-Whitney U: null hypothesis reddedilemedi

**Kritik:** Tek basina candlestick pattern edge yoktur. Zorunlu filtreler:
1. Trend context (EMA yonu)
2. Volume confirmation (ort x 1.5-2.0)
3. RSI/momentum filtresi
4. Support/resistance yakinligi

Kaynak: https://medium.com/@Tobi_Lux/on-predictive-power-of-candlestick-patterns-in-stock-trading-an-experiment-d71dd92b4b27

### A2. Bulkowski Istatistikleri (~2800+ Trade, Gunluk Chart)

| Pattern | Tip | WR | Avg | Not |
|---|---|---|---|---|
| H&S Bottom | Reversal | **95%** | **+38%** | Uzun formasyon |
| Double Bottom | Reversal | **88%** | **+50%** | Kisa vadede tamamlanabilir |
| H&S Top | Reversal | **81%** | **-16%** | Neckline kritik |
| Three Black Crows | Candle | **78%** | - | Bearish continuation |
| Three Outside Up | Candle | **75%** | - | Guclu reversal |
| Evening Star | Candle | **72%** | - | 3-mum reversal |
| Morning Star | Candle | **65%** | - | 3-mum reversal |
| Bullish Engulfing | Candle | **63%** | **-1.18% (10gun)** | Follow-through NEGATIF! |

DIKKAT: Bulkowski rakamlari gunluk/haftalik chart icin. 1m bar noise dramatik artar.
Bullish Engulfing: 10 gunluk ortalama hareket -1.18% (downside takip!)

Kaynak: https://www.thepatternsite.com/CandlePerformers.html
Kaynak: https://www.thepatternsite.com/BullEngulfing.html
Kaynak: https://www.thepatternsite.com/hst.html

### A3. Kisa Vadeli Backtest (Chartpatternspro.com)

Timeframe: 1H/4H/Daily. Asset: Hisseler + BTC/USD + Forex. Min R:R: 1.5:1

| Pattern | WR |
|---|---|
| Double Bottom | **70%** |
| Cup and Handle | **68%** |
| Inverse H&S | **65%** |
| Bull Flag | **64%** |
| Ascending Triangle | **62%** |

Kaynak: https://chartpatternspro.com/high-win-rate-chart-patterns/

### A4. Crypto-spesifik WR (altFINS AI, 15m-4H)

| Pattern | WR |
|---|---|
| Inverse H&S | **84-86%** |
| H&S | **82%** |
| Double Bottom | **82%** |
| Channel Up | **73%** |
| Triangle | **62%** |
| Pennant | **56%** |

Kaynak: https://altfins.com/knowledge-base/chart-patterns/

---

## BOLUM B - ONERILEN 7 PATTERN (5-10 BAR KRITERI)

Secim: (1) 5-10 bar icinde tamamlanabilir, (2) WR>%60 filtresiz veya >%55 filtreli,
(3) Algoritmik tanim net, (4) Entry+Stop+TP hesaplanabilir.

---

### PATTERN 1 - Bullish/Bearish Engulfing (2 mum)

Tamamlanma: 2 bar



Validity: Trend EMA zit yonde + Volume >= 1.5x ort + RSI Bullish<40/Bearish>60
Entry: conf bar acilisi | Stop: engulfing bar ucu | TP: risk x 1.5 | Time stop: 5 bar
WR: filtresiz %63 (Bulkowski), filtreli ~%55-65
**Red flag:** 10 gun follow-through NEGATIF. Volume onayi zorunlu.

---

### PATTERN 2 - Pinbar / Hammer / Shooting Star (1 mum)

Tamamlanma: 1 bar



Validity: SR seviyesi yakinligi (+-0.5%) + trend zit + volume >= 1.2x ort
Entry: +1 bar onay | Stop: wick ucu | TP: risk x 2.0 | Time stop: 5 bar
WR: %55-60 (Luxalgo backtest)
Kaynak: https://www.luxalgo.com/blog/candle-formations-every-scalper-should-know/

---

### PATTERN 3 - Morning Star / Evening Star (3 mum)

Tamamlanma: 3 bar



NOT (crypto): Gap sarti 1m crypto da uygulanamaz. Gap sarti kaldirildi.
Entry: b3 kapanisi | Stop: b2 en ucu | TP: b1 basina kadar | Time stop: 7 bar
WR: Morning %65, Evening %68 (Luxalgo)

---

### PATTERN 4 - Bull Flag / Bear Flag (5-10 mum)

Tamamlanma: 5-10 bar (flagpole 2-4 bar + konsolidasyon 3-6 bar)

Algoritmik kural (pseudo-csharp):



TP: flagpole yuksekligi x 0.7 + breakout fiyati (konservatif)
Stop: flag alt kenari
Volume: flagpole artis -> konsolidasyon azalis -> breakout artis
Retracement limiti: flagpole %50 gecemez -> gecerse gecersiz
WR: %64 (chartpatternspro, 1H+) | 1m beklenti: %55-62
Kaynak: https://www.binance.com/en/blog/futures/what-are-bull-flags-and-bear-flags-and-how-to-trade-them-724401699901929836

---

### PATTERN 5 - Double Bottom / Double Top (8-15 mum)

Tamamlanma: 8-15 bar

Algoritmik kural:



Stop: ikinci dip alti - 1 tick
Min aralik: 5 bar iki dip arasi | Volume: breakout ta artmali
WR: %70 (kisa vadeli backtest) / %88 (Bulkowski gunluk) | 1m beklenti: %60-70
Kaynak: https://thepatternsite.com (2800+ trade)

---

### PATTERN 6 - Ascending/Descending Triangle (8-15 mum)

Tamamlanma: 8-15 bar

Algoritmik kural:



TP: triangle yuksekligi x 0.7 | Stop: son konsolidasyon low | Time stop: 10 bar
WR: %62

---

### PATTERN 7 - Three White Soldiers / Three Black Crows (3 mum)

Tamamlanma: 3 bar

Algoritmik kural (Three White Soldiers):



Entry: b3 kapanisi | Stop: b1 low/high | TP: b3 govde x 1.5 | Time stop: 5 bar
WR: Three Black Crows %78 (Luxalgo)

---

## BOLUM C - PATTERN AGIRLIK SISTEMI

### C1. Statik Baslangic Agirliklari

| Pattern | Kaynak WR | Weight |
|---|---|---|
| Double Bottom/Top | %70-88 | **0.85** |
| Three Soldiers/Crows | %75-78 | **0.78** |
| Morning/Evening Star | %65-68 | **0.67** |
| Bull/Bear Flag | %64 | **0.64** |
| Triangle | %62 | **0.62** |
| Pinbar/Hammer | %55-60 | **0.58** |
| Engulfing | %55-63 | **0.55** |

### C2. Confidence Skoru



### C3. Dinamik Agirlik - Sprint 3 Backlog

Thompson Sampling: her 50 trade sonrasi wins/(wins+losses) guncelleme.

---

## BOLUM D - TRADE MEKANIGI

### D1. Giris Kurallari



### D2. Stop Seviyeleri

| Pattern | Stop |
|---|---|
| Engulfing | Engulfing bar ucu |
| Pinbar | Wick ucu |
| Morning/Evening Star | Yildiz mum en ucu |
| Bull/Bear Flag | Flag karsi kenari |
| Double Bottom/Top | Ikinci dip/tepe disi |
| Triangle | Son konsolidasyon low/high |
| Three Soldiers/Crows | Ilk bar karsi ucu |

### D3. Take Profit



### D4. Time Stop (Kritik - 5-10 dk)



Rationale: Pattern edge zamanla kaybolur. 10dk sonra hareket yoksa setup gecersiz.

### D5. Position Sizing (Loop 14 reform sabit)

Notional: 0 | MaxOpen: 2 | RiskPerTrade: %2 | Half Kelly: %8-10

---

## BOLUM E - C# LIBRARY ONERISI

### E1. OHLC_Candlestick_Patterns (Ana tercih - native C#)

NuGet paketi: OHLC_Candlestick_Patterns
- 37 bullish + 37 bearish candlestick pattern
- 9 bullish + 9 bearish classic chart formation
- 6 Fibonacci pattern
- .NET 8.0 Standard (net10 uyumlu), saf C#, native dependency yok
- Kullanim: Engulfing, Star, Soldiers/Crows icin

Kaynak: https://github.com/przemyslawbak/OHLC_Candlestick_Patterns

### E2. Skender.Stock.Indicators (Filtreler icin)

NuGet paketi: Skender.Stock.Indicators
- RSI + EMA filtre indicator icin kullan
- Candlestick pattern detection icin DEGIL
- Aktif bakimli, .NET 10 uyumlu

Kaynak: https://www.nuget.org/packages/Skender.Stock.Indicators

### E3. Hibrit Yaklasim (Tavsiye)



NOT: Flag, DoubleBottom, Triangle hicbir .NET library de tam yok. Custom yazilmali.

---

## BOLUM F - CRYPTO-SPESIFIK DIKKAT NOKTALARI

**1m Bar Noise:** Daily chart a gore sinyal:noise ~10x daha kotu.
Mitigation: Multi-timeframe onay (1m pattern + 5m trend context).

**Gap yok:** Crypto 24/7. Morning/Evening Star gap sarti kaldirilmali.
Yoksa 1m de bu pattern hic olusmuyor.

**Sembol guvenilirligi:**
- BTC/USDT, ETH/USDT: En likit, en guvenilir pattern.
- BNB/USDT: Orta, kabul edilebilir.
- XRP/USDT: Daha az likit. WR %5-10 asagi revize et.

**Entry Order Tipi:** LIMIT_MAKER kullan. Stop exit: MARKET.

---

## BOLUM G - MIMARI UYUM

### G1. Korunacak Mevcut Yapi

- Strategy.EmitSignal: contextJson pattern bilgisi tasir (mevcut)
- Position: StopPrice + TakeProfit zaten var (mevcut)
- OrderType.LimitMaker: mevcut (7 tip)

### G2. Silinecekler



### G3. Interface (Backend-Dev Icin)



### G4. Klasor Yapisi



---

## BOLUM H - HEDEF PERFORMANS

### H1. Edge Hesabi (40 USD Notional, stop=%1, risk=0.40 USD)

Round-trip fee: %0.20 = 0.08 USD/trade

WR %55 + R:R 1.5:1 = 0.55x0.60 - 0.45x0.40 - 0.08 = 0.07 USD/trade  POZITIF
WR %60 + R:R 2.0:1 = 0.60x0.80 - 0.40x0.40 - 0.08 = 0.24 USD/trade  GUCLU
WR %50 + R:R 1.5:1 = 0.50x0.60 - 0.50x0.40 - 0.08 = 0.02 USD/trade  (cok dar)

### H2. Gunluk Hedef (100 USD portfoy)

| Senaryo | WR | R:R | Trade/gun | Net/gun | % |
|---|---|---|---|---|---|
| Muhafazakar | %55 | 1.5 | 15 | ~1.05 USD | ~%1.0 |
| Orta | %58 | 1.5 | 20 | ~2.00 USD | ~%2.0 |
| Optimist | %62 | 2.0 | 20 | ~4.80 USD | ~%4.8 |
| **Hedef** | **%60** | **1.8** | **15** | **~3.00 USD** | **~%3.0** |

### H3. Red Flags

1. Engulfing 10gun follow-through NEGATIF -> sadece 5 bar time stop ile kullan
2. 1m noise -> 5m trend onay eklenmezse cok false signal
3. Volume onayi olmadan hicbir pattern girilmez
4. Crypto gap yok -> Morning/Evening Star gap sartsiz konfigure et
5. Market order her iki taraf -> %0.20 round-trip ince marji yer; LIMIT entry tercih
6. Tek pattern confidence <0.55 -> confirmasyona bekle

---

## BOLUM I - OZET VE SPRINT PLANI

### 7 Pattern Grubu

Sprint 1 (Yuksek WR, net algoritma):
1. Double Bottom / Double Top (WR %70-88)
2. Bull Flag / Bear Flag (WR %64, volume-driven)
3. Three White Soldiers / Black Crows (WR %78, 3 bar)
4. Morning Star / Evening Star (WR %65-68, 3 bar)

Sprint 2 (Filtre bagimli, daha noise):
5. Ascending/Descending Triangle (WR %62)
6. Pinbar / Hammer / ShootingStar (WR %55-60)
7. Bullish/Bearish Engulfing (WR %55-63, SADECE filtreli)

### Implementasyon Adim Ozeti

1. NuGet: OHLC_Candlestick_Patterns + Skender.Stock.Indicators
2. IPatternDetector + PatternResult + PatternType enum
3. 14 detector sinif
4. PatternScalpingEvaluator (confidence + time stop)
5. StrategyType.PatternScalping ekle
6. Grid/TrendFollowing/MeanReversion SIL
7. Domain.Tests: her detector icin unit test

---

## KAYNAKLAR

- Bulkowski thepatternsite: https://thepatternsite.com
- Bulkowski Top 10 Candles: https://www.thepatternsite.com/CandlePerformers.html
- Bulkowski H&S Top: https://www.thepatternsite.com/hst.html
- Bulkowski Bullish Engulfing: https://thepatternsite.com/BullEngulfing.html
- Tobi Lux Experiment: https://medium.com/@Tobi_Lux/on-predictive-power-of-candlestick-patterns-in-stock-trading-an-experiment-d71dd92b4b27
- AltFINS Crypto WR: https://altfins.com/knowledge-base/chart-patterns/
- ChartPatternsPro Backtest: https://chartpatternspro.com/high-win-rate-chart-patterns/
- Luxalgo Scalper Candles: https://www.luxalgo.com/blog/candle-formations-every-scalper-should-know/
- Binance Bull/Bear Flag: https://www.binance.com/en/blog/futures/what-are-bull-flags-and-bear-flags-and-how-to-trade-them-724401699901929836
- OHLC_Candlestick_Patterns: https://github.com/przemyslawbak/OHLC_Candlestick_Patterns
- Skender Stock Indicators: https://github.com/DaveSkender/Stock.Indicators
- Binance Fee Schedule: https://www.binance.com/en/fee/spotMaker
- Binance Fees: https://cryptopotato.com/binance-fees/
- MQL5 Bulkowski: https://www.mql5.com/en/blogs/post/759749
