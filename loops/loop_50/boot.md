# Loop 50 Boot — HybridMomentum1m (15m BB + 1m EMA Hibrit) (2026-04-28 23:43 TR)

## Pivot Sebebi
Kullanıcı net direktif: "saatte 150 işlem dedim, gerçek mainnet'e en yakın hali, saat ayrımı yok, kar+frekans birlikte". binance-expert komple AR-GE:

**Matematiksel gerçek:** 150/h fee+spread+slippage matematiği WR %80+ gerektirir → **imkansız**. Pragmatik 8-15/h, %40+ WR mainnet-realistic.

**Çözüm:** İki strateji birleşimi (kalite + frekans):
- BB MeanRev 15m (kalite kapısı, %43 WR ama 1.2/h)
- EmaScalper1m (frekans, %27 WR ama 19/h)
- → **HybridMomentum1m** = 15m BB lower entry kapısı + 1m EMA crossover trigger

## Strateji — HybridMomentum1m

**Giriş AND koşulları (7):**
1. 15m: `currentClose < bbLower(20, 2.0)` — bearish dip yakalama
2. 15m: `rsi14 < 40 AND rsi14_curr > rsi14_prev` — momentum yukarı dönüyor
3. 1m: `ema9 > ema21` (crossover OR sustained)
4. 1m: `volume_curr > volumeMa20 × 1.2` — hacim onayı
5. 1m: `atrPct ≥ 0.0003` — piyasa aktif
6. 15m: `BarClosed == true`
7. Cooldown: 3 bar (3dk per coin)

**Çıkış geometrisi (15m ATR-based):**
- TP: `entry × (1 + clamp(atr14_15m × 1.5 / entry, 0.004, 0.010))` → %0.40-1.00
- SL: `entry × (1 - clamp(atr14_15m × 0.8 / entry, 0.002, 0.004))` → %0.20-0.40
- R:R = 1.875:1, BE WR ~%34.8
- MaxHold: 30dk
- Direction: LONG only

## 5 Aktif Coin (en likit)
BTC, ETH, SOL, XRP, ADA. Diğer 7 coin (BNB, DOGE, LINK, DOT, AVAX, LTC, TRX) Activate=false.

**MaxOpenPositions: 3 → 5** (sermaye 5 coin'e dağılır, $100/each).

## Beklenti (binance-expert)
- Frekans: 8-15 trade/h (hibrit kalite + frekans)
- WR: %40+ hedef
- Net/h paper: +$0.30 (orta senaryo)
- Net/24h: ~+$7.20

## Boot State
| Metrik | Değer |
|---|---|
| Mode | Paper |
| StartingBalance | $500.0000 |
| Equity | $500.0000 |
| Active | 5 (HybridMomentum1m) |
| MaxOpenPositions | **5** (yeni) |
| API Port | 5188 |
| Branch | development |

## Implementasyon
- backend-dev: HybridMomentum1mEvaluator + snapshot + indicator service + DI + appsettings
- 295 test pass (284 önceki + 11 yeni)
- 0 build error/warning
- StrategyType `HybridMomentum1m = 7`

## Halt Eşikleri
- Realized < -$1.50 → Loop 51 binance-expert
- 5+ ardışık SL → Loop 51
- 0 sinyal 2h → filtre gevşet (volMul 1.2→1.0, RSI 40→45)
- WR < %25 (10+ trade) → Loop 51

## Loop 41-49 Aggregate (Final)
| Loop | Trade | Realized | WR |
|---|---|---|---|
| 41-43 | 11 | -$2.97 | %0 |
| 44-45 | 2 | +$0.011 | %50 |
| 46-48 | 13 | -$1.69 | %23 |
| 49 | 7 | -$0.576 | %43 |
| **Total** | **33** | **-$5.23** | %18 |

## Yeni Altın Kural #11
"Saat dilimi/seans ayrımı YOK." Loop raporlarında saat-yorumu, parametre saat-bağımlı değişiklik YASAK. Crypto 24/7 uniform.

## Sıradaki Wakeup
**ScheduleWakeup 1800s → t=30dk (00:13 TR ertesi gün)**

15m+1m hibrit strateji — 30dk içinde ilk sinyal beklenir.

— PM 2026-04-28 Loop 50 boot
