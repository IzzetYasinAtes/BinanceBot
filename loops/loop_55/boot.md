# Loop 55 Boot — BB MeanRev BBstd 1.3 (KAR KORUNDU) (2026-04-29 06:46 TR)

## Pivot Sebebi
Loop 54 t180 KAR HALT — Realized +$0.355, ama frekans 0.33/saat çok düşük. BBstd 1.5'ten 1.3'e ek gevşetme + DB reset YOK (kar history korundu).

## Boot State (KAR KORUNDU)
| Metrik | Değer |
|---|---|
| Mode | Paper |
| StartingBalance | $500.00 (orijinal) |
| **CurrentCash** | **$500.36** ✓ (Loop 54 ETH +$0.355 kar) |
| **Equity** | **$500.36** |
| **Realized history** | **+$0.355** ✓ |
| Active | 5 (BbMeanReversion15m, yeni BBstd 1.3) |
| WR (Loop 54+) | %100 (1/1) |
| API Port | 5188 |

## Loop 55 Parametreler

| Parametre | Loop 54 | **Loop 55** |
|---|---|---|
| `BbStdMultiplier` | 1.5 | **1.3** |
| RSI Oversold | 55 | 55 |
| volZ | 0.0 | 0.0 |
| TpAtr | 1.8 | 1.8 |
| SlAtr | 0.9 | 0.9 |
| MaxHold | 120dk | 120dk |
| Cooldown | 3 bar | 3 bar |
| MinAtr | 0.0005 | 0.0005 |

BBstd 1.3 = BB band çok dar, alt-banda dokunma 2-3x daha sık → emit frekansı artar beklentisi.

## Beklenti
- Frekans: 0.33/h → **1-2/h** (BBstd 1.3 ek gevşetme)
- WR: %50-80 hedef (Loop 54 %100 idi, küçük örneklem)
- Riski: BBstd 1.3 ile false signal artabilir, SL hit oranı yükselebilir

## 5 Aktif Coin
BTC, ETH, XRP, SOL, ADA. MaxOpenPositions=5.

## Halt Eşikleri
- Realized < -$2.00 (mevcut +$0.355 - $2.00 = $1.65 buffer) → Loop 56 binance-expert
- 4+ ardışık SL → Loop 56
- 0 yeni emit (60dk içinde) → BBstd 1.3 de yetmedi → Loop 56 binance-expert

## Loop 41-54 Aggregate
| Loop | Trade | Realized |
|---|---|---|
| 41-43 | 11 | -$2.97 |
| 44-45 | 2 | +$0.011 |
| 46-48 | 13 | -$1.69 |
| 49 | 7 | -$0.576 |
| 50-53 | 0 | $0 |
| **54** | **1** | **+$0.355** ✓ |
| **Total** | **34** | **-$4.87** | %20 WR |

## Sıradaki Wakeup
**ScheduleWakeup 1800s → t=30dk (07:16 TR)**

— PM 2026-04-29 Loop 55 boot
