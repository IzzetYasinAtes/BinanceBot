# Strategy Pivot Analysis — BinanceBot
**Date:** 2026-05-03
**Agent:** binance-expert
**Decision seq:** 223

---

## 1. Neden Mevcut Strateji Calısmiyor

| Parametre | Deger |
|---|---|
| Taker fee (USDT-M, Tier 0) | %0.05 |
| Round-trip fee (2 leg) | %0.10 |
| Testnet peak volatility | ~%0.10 |
| Net expectancy | 0 veya negatif |

43 loop, 0 pozitif loop. Gross profit <= fee. Strateji ailesi degistirilmeli.
Kaynak: https://www.binance.com/en/support/faq/detail/360033544231

## 2. Fee Yapisi
| Tier | Maker | Taker |
|---|---|---|
| Tier 0 | %0.02 | %0.05 |
| VIP 9 | %0.000 | %0.017 |
| BNB indirim | +%10 | |

### Kod Degisiklikleri
1. StrategyEvaluator = yeni SwingTradingEvaluator (4h kline, MTF)
2. SignalGenerator = EMA crossover + volume + RSI
3. OrderManager = STOP_MARKET + LIMIT_MAKER cifti (OCO benzeri)
4. PositionManager = trailing stop + time-exit
5. RiskManager = max 3 concurrent + %1.5/trade limit
6. Eski 5dk pattern evaluator tamamen silinecek (deprecated kod yasak)

## 8. Binance Futures 2024-2025 Degisiklikler
- 2024: 30+ yeni perpetual eklendi
- 2025 Q1: 40+ yeni perpetual
- Max leverage: BTC 125x, yeni tokenlar 20-75x
- Reduce-only order -1008 muafiyeti: likidite krizinde kapama korunuyor
- RSA PKCS#8 signature destegi eklendi
- priceMatch parametresi STOP/TAKE_PROFIT icin eklendi
- Testnet: https://testnet.binancefuture.com
Kaynak: https://developers.binance.com/docs/derivatives/usds-margined-futures/general-info

## 9. Open-Source Bot Referans
| Bot | Strateji | Not |
|---|---|---|
| Freqtrade | Trend, mean-reversion, DCA, ML | 25k+ star, FreqAI |
| Hummingbot | Market making, arb | DEX+CEX, 50+ connector |
| Jesse | Swing, breakout | Detayli backtest engine |

https://github.com/freqtrade/freqtrade-strategies

## Sonuc
Tavsiye: Swing Trading 4h MTF
Fee toleransi: %0.10 round-trip, %2+ hedef = sorunsuz
Beklenti: %40-55 win rate, 1:2 R:R = +0.3R/trade expectancy
Gelistirme: 2-3 sprint
Once: Eski 5dk ScalpingEvaluator tamamen silinecek
## 3. Strateji Karsilastirma
| Kriter | Swing 4h | Grid | Funding Arb | Breakout |
|---|---|---|---|---|
| Win rate | %45-65 | %60-80 | %90+ | %30-45 |
| Risk Reward | 1:2-1:3 | 1:0.5-1 | 1:0.1-0.3 | 1:2.5-1:4 |
| Hedef move | %2-5 | %0.3-1 | %0.01-0.1 | %3-8 |
| Fee etkisi | Minimal | Kritik | Kademeli | Minimal |
| Sermaye | 200+ | 500+ | 300+ | 300+ |
| Mimari uyum | Yuksek | Orta | Dusuk | Yuksek |
| Aylik beklenti | %3-8 | %5-11 | %1.6-3.2 | %2-10 |
| Max drawdown | Orta | Yuksek | Dusuk | Yuksek |

## 4. TAVSIYE: Swing Trading 4h MTF

1. Fee matematigi cozuluyor. %2 move - %0.10 fee = %1.9 net. %5 move = %4.9 net.
2. Mevcut mimariye %80 uyumlu. Kline stream var, order tipleri ayni.
3. 5 coin paralel korunuyor. Gunde 10-25 trade potansiyeli.
4. Long+Short: 4h swing hem bull hem bear momentuma girilebilir.
5. Test edilebilir: Freqtrade 2 yil OHLCV backtest.

### Red Flag Listesi
| Risk | Aksiyon |
|---|---|
| Overnight funding fee | SL funding maliyetini asmali |
| Slipaj XRP/ADA | LIMIT entry +%0.05 buffer |
| False breakout | Volume confirmation zorunlu |
| Trend reversal trapping | Hard SL max %1.5 sermaye |
| Testnet likidite farki | Mainnet geciste param re-tune |

### Funding Rate Etki
Binance USDT-M interval: 8 saatte bir
Tipik BTC funding: 0.01%/8h
4h pozisyon beklenen funding: 0 veya 1 cycle = 0.01%
%2 hedef tradede etki: 0.5%  (kabul edilebilir)
Red flag: Funding 0.1%+/8h ise o coinde pozisyon acma.

## 5. Grid Neden Simdi Degil
Trend piyasada zarar uretir. 2024 BTC +%120 trend = grid zarar.
Grid engine ayri mimari = 2-3 sprint extra.
Sonuc: Sonraki sprint ek strateji, pivot olarak uygun degil.

## 6. Funding Arb Neden Simdi Degil
Dual account gerekli: spot BTC + futures short.
Mevcut bot yalnizca futures. Spot integration = mimari kirilim.
00 ile yilda ~5 = ayda . Hedef alti.
Sonuc: Ayri microservice olabilir, mevcut bot icin degil.

## 7. Implementation Hint — Backend-Dev

Entry Logic:
1. Her 4h bar kapanisinda 5 coin icin sinyal tara
2. EMA(20) > EMA(50) AND volume > SMA_volume(20) x 1.5 = Long setup
3. RSI(14) 40-65 araliginda (momentum basliyor)
4. ATR(14) x 1.5 = SL mesafesi (bar low alti)
5. TP: entry + ATR x 3 (R:R = 1:2)
6. Max acik pozisyon: 3 ayni anda
7. Max risk per trade: %1.5 sermaye (= .50 / 00)

Exit Logic:
1. SL: STOP_MARKET GTC, entry aninda tetikle
2. TP: LIMIT GTC, entry aninda tetikle
3. Trailing stop: ATR dinamik, move %1+ = BE stop
4. Time-exit: 2 x 4h bar gecti + %0.5 karda ise kapat

### Parametreler (Baslangic)
| Param | Deger |
|---|---|
| Timeframe | 4h |
| Confirmation TF | 1h |
| EMA Short | 20 |
| EMA Long | 50 |
| RSI Period | 14 |
| ATR Period | 14 |
| SL multiplier | 1.5x ATR |
| TP multiplier | 3x ATR |
| Max concurrent | 3 |
| Risk per trade | %1.5 sermaye |
| Volume filter | 1.5x SMA(20) |

