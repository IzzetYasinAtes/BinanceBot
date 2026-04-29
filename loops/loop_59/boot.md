# Loop 59 Boot — BB MeanRev v2 + EMA200 Trend Filtresi (BTC-only) (2026-04-29 17:23 TR)

## Pivot Sebebi
Loop 58 t480 DISASTER: 8 ardışık SL → realized -$3.95 → RiskAlert + tüm 5 strateji DEACTIVATED. 13:30 UTC'de 5 coin eşzamanlı falling knife.

binance-expert teşhis (3 katmanlı hata):
1. **RSI 55 oversold DEĞİL** (gerçek oversold <30, 55 nötr)
2. **BBstd 1.5 band çok dar** (her 15-25dk normal volatilite "extreme" tetikliyordu)
3. **5 coin korelasyon riski** (BTC/ETH/SOL/ADA/XRP korelasyon >0.85 → tek riski 5x büyütüyor)

## Loop 59 Çözüm: 3 Katman Sıkılaştırma + Yeni Filtre

### Backend Değişiklik (commit `e5fb921`)
- `MarketIndicatorService` 15m buffer 80 → **200 bar** (EMA200 için)
- `BbMeanReversionIndicatorSnapshot` yeni alan: `Ema200_15m`
- `BbMeanReversionEvaluator` yeni AND koşul: `currentClose > Ema200_15m` (uptrend filter)
- 298/298 test pass (296 önceki + 2 yeni EMA200 testi)

### Parametre Değişiklikleri

| Parametre | Loop 58 | **Loop 59** | Gerekçe |
|---|---|---|---|
| `BbStdMultiplier` | 1.5 | **2.2** | Gerçek extreme dip (haftada 1-2 kez) |
| `RsiOversoldThreshold` | 55 | **30** | Teknik oversold tanımı |
| `VolumeZScoreThreshold` | 0.0 | **0.5** | Volume spike teyidi geri |
| **YENİ: EMA200 trend** | yok | **`close > Ema200_15m`** | Sadece uptrend long |
| `TpAtrMultiplier` | 1.8 | **2.5** | Geniş TP (sıkı sinyal kalitesi destekler) |
| `SlAtrMultiplier` | 0.9 | **0.7** | Dar SL (hızlı çıkış) |
| `MaxHoldMinutes` | 120 | **90** | Trend filtreli bounce hızlı |
| `CooldownBarsAfterSignal` | 4 | **8** | 120dk cooldown |
| Coin | BTC+ETH+XRP+SOL+ADA | **Sadece BTC** | Korelasyon riski yok |
| `MaxOpenPositions` | 5 | **1** | Tek pozisyon |

R:R = 2.5/0.7 = **3.57:1**, BE WR ~%22.

## Boot State
| Metrik | Değer |
|---|---|
| Mode | Paper |
| StartingBalance | $500 |
| Equity | $500 |
| Active | **1 (BTC-BbMeanRev15m-v2)** |
| MaxOpenPositions | **1** |
| API Port | 5188 |

## Beklenti (binance-expert)
- Frekans: günde 1-3 sinyal (sıkı filtreler)
- Uptrend filtresi → downtrend'de 0 emit (doğal halt = sermaye koruma)
- WR: >%25 (BBstd 2.2 + RSI 30 = kaliteli sinyal)
- Net günlük: BE ile +$0.50 arası

## Halt Eşikleri (sıkı, sermaye koruma)
- Realized < **-$0.80** → halt (testnet günlük max %0.16 kayıp)
- 3+ ardışık SL → halt (trend filter çalışmıyorsa rejim değişti)
- t120 = 0 emit → **iyi işaret** (downtrend rejimi, doğal halt)
- WR < %20 (5+ trade) → halt

## Loop 41-58 Aggregate (DISASTER sonrası)
| Loop | Trade | Realized |
|---|---|---|
| 41-43 | 11 | -$2.97 |
| 44-45 | 2 | +$0.011 |
| 46-48 | 12 | -$1.69 |
| 49 | 7 | -$0.576 |
| 50-53 | 0 | $0 |
| 54-55 | 1 | +$0.355 |
| 56 | 5 | -$0.97 |
| 57 | 0 | $0 |
| **58 (DISASTER)** | **9** | **-$3.95** |
| **Total** | **47** | **-$9.79** | %15 WR |

## Sıradaki Wakeup
**ScheduleWakeup 1800s → t=30dk (17:53 TR)**

Erken kontrol:
- EMA200 hesaplama warmup tamam mı (200 bar 15m = 50h backfill)
- 0 emit beklenen (kalite öncelikli, frekans düşük)

— PM 2026-04-29 Loop 59 boot
