# Loop 67 Boot — KOMPLE YENİ ALGORİTMA: KlineMomentumSpread5m (KMS) (2026-04-30 23:26 TR)

## Pivot Sebebi (Kullanıcı Direktifi)
Loop 41-66 boyunca 7 farklı strateji denendi (Donchian, BB MeanRev, EmaScalper, HybridMomentum, BB+EMA200), HEPSİ başarısız. ~$11 net loss, 67 trade %14 WR.

Kullanıcı direktifi: "komple yeni algoritma + sık karlı + 5 coin koru + eski kod temizle".

## Yeni Algoritma: KMS (binance-expert tasarımı)

**Mantık:** 5m bar kapanışında — RSI oversold recovery + EMA9 pozitif slope + TradeCount surge + BookTicker spread kontrol → mikro long entry. Counter-trend recovery (downtrend "dip dönüşü", BB MeanRev gibi düşen bıçak DEĞİL).

**Giriş AND koşulları (5):**
1. **RSI Recovery:** `Rsi(5m, 14) > 32 AND Rsi_prev < 32` (oversold çıkış)
2. **EMA9 Slope:** `Ema9_now > Ema9_prev`
3. **TradeCount Surge:** `currentTC > avgTC20 × 1.1` (testnet için 1.1, mainnet 1.5)
4. **BookTicker Spread:** `(Ask-Bid)/Ask < 0.0015` (testnet, mainnet %0.05)
5. **MinAtrPct:** `atr14_5m / close >= 0.0005`

**Çıkış geometrisi:**
- TP: `entry × (1 + clamp(atr × 1.8 / entry, 0.005, 0.018))` → %0.50-1.80
- SL: `entry × (1 - clamp(atr × 0.75 / entry, 0.003, 0.008))` → %0.30-0.80
- R:R = 1.67:1, BE WR (fee dahil) %57.5
- MaxHold: 45dk
- Cooldown: 3 bar = 15dk

## Yenilik
- **BookTicker realtime spread filter** (mevcut altyapı ilk kez kullanılıyor)
- **TradeCount surge** (volume yerine işlem sayısı — whale spike yerine gerçek katılım)
- **RSI Recovery** (oversold ENTRY değil, oversold ÇIKIŞ — düşen bıçak yok)
- **Tek timeframe (5m)** — multi-TF AND çakışma yok

## Backend-Dev İş Sonucu (commit `5ca1258`)

**Silinen (19 dosya):**
- 7 evaluator (VwapEma, MicroScalper, AtrScalper, Donchian, BbMeanRev, EmaScalper, HybridMomentum)
- 6 snapshot record
- 6 test dosyası

**Yeni (8 dosya):**
- IBookTickerReader + BookTickerCache + Worker
- KmsMomentumSnapshot + KmsMomentumEvaluator
- EF Migration (Loop67KmsReset, DML reset)
- 6 unit test

**Test:** 227/227 pass, 0 build error/warning.

## 5 Aktif Coin
BTC, ETH, XRP, SOL, ADA. Sembol listesi 12→5.

## Boot State
| Metrik | Değer |
|---|---|
| Mode | Paper |
| Cash / Equity | $500 / $500 |
| Active | 5 (BTC/ETH/XRP/SOL/ADA KMS) |
| MaxOpenPositions | 5 |
| API Port | 5188 |

## Beklenti (binance-expert)
- Frekans: 8-20 sinyal/h (5 coin × RSI recovery oranı)
- WR hedef: %58-62 (RSI recovery literatür)
- Net trade: +$0.004 (orta, marjinal pozitif)
- Net trade: +$0.036 (iyimser %62 WR)
- Günlük: +$0.24 ile +$2.16 arası

## Halt Eşikleri
- Realized < -$1.50 → Loop 68 binance-expert
- 5+ ardışık SL → otomatik halt (RiskProfile)
- 0 emit > 2h → param sıkılaştır (TradeCountMul 1.1→1.3)
- WR < %40 (10+ trade) → Loop 68

## Yeni Altın Kural #13 Eklendi
"Deprecated kod / yorum YASAK — yeni algoritma yazılınca eski tamamen silinir." Bu loop'ta 19 dosya silindi, prensibin ilk uygulaması.

## Sıradaki Wakeup
**ScheduleWakeup 1800s → t=30dk (23:56 TR)**

— PM 2026-04-30 Loop 67 boot
