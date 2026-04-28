# Loop 53 Boot — BB MeanRev 15m Daha Gevşek (2026-04-29 01:59 TR)

## Pivot Sebebi
HybridMomentum1m yapısal hata kanıtlandı (3 config 0 emit). binance-expert teşhis: AND koşullarında "BB lower altında (aşağı baskı)" + "EMA9>EMA21 (yukarı momentum)" çakışıyor — eş zamanlı sağlanması nadir, testnet'te hiç yok.

Karar: **BB MeanRev 15m** (kanıtlanmış evaluator) + filtre Loop 49'dan daha gevşek.

## Loop 49 → Loop 53 Parametre Değişiklikleri

| Parametre | Loop 49 | **Loop 53** | Etki |
|---|---|---|---|
| `BbStdMultiplier` | 2.0 | **1.8** | Alt band daha sık kırılır |
| `RsiOversoldThreshold` | 38 | **45** | Daha geniş oversold pencere |
| `VolumeZScoreThreshold` | 0.5 | **0.3** | Daha az hacim teyidi |
| TpAtrMultiplier | 1.8 | 1.8 (korundu) | R:R 2:1 |
| SlAtrMultiplier | 0.9 | 0.9 (korundu) | — |
| MaxHoldMinutes | 120 | 120 (korundu) | — |
| MinAtrPct | 0.0005 | 0.0005 (korundu) | sessiz coin filtre |
| CooldownBarsAfterSignal | 3 | 3 (korundu) | 45dk per coin |

R:R = 2:1, BE WR ~%33.3. Loop 49 WR %43 olduğundan teorik pozitif.

## Beklenti (binance-expert)
- Frekans: günde 5-15 sinyal (Loop 49 günlük 7'den 2-3x artış)
- 5 coin × 0.5-1.5 sinyal/coin/gün
- WR: %35-45 (kalite biraz düşer)
- Net günlük: BE ile +$1 arası

## 5 Aktif Coin
BTC, ETH, XRP, SOL, ADA. Tüm HybridMomentum1m + EmaScalper1m + DonchianBO + AtrSwing Activate=false.

MaxOpenPositions=5 (Loop 50'den korundu).

## Boot State
| Metrik | Değer |
|---|---|
| Mode | Paper |
| StartingBalance | $500 |
| Equity | $500 |
| Active | 5 (BbMeanReversion15m gevşek) |
| API Port | 5188 |

## Halt Eşikleri (binance-expert revizyon)
- Realized < -$2.00 → halt (genişletildi, volZ 0.3 daha çok pozisyon)
- 4+ ardışık SL → halt
- t60 = 0 sinyal → BB MeanRev de çalışmıyor → farklı pivot
- t120 = 3+ sinyal şart (frekans doğrulama)

## Loop 41-52 Aggregate
| Loop | Strateji | Trade | Realized |
|---|---|---|---|
| 41-43 | Donchian | 11 | -$2.97 |
| 44-45 | BB MeanRev sıkı/orta | 2 | +$0.011 |
| 46-48 | EmaScalper | 12 | -$1.69 |
| 49 | BB MeanRev gevşek | 7 | -$0.576 |
| 50-52 | HybridMomentum | 0 | $0 (mimari hata) |
| **Total** | — | **32** | **-$5.23** |

## Sıradaki Wakeup
**ScheduleWakeup 1800s → t=30dk (02:29 TR)**

— PM 2026-04-29 Loop 53 boot
