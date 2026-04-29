# Loop 54 Boot — BB MeanRev 15m Volume OFF + Maks Gevşek (2026-04-29 04:08 TR)

## Pivot Sebebi
Loop 53: 2h, 0 SignalEmitted, 640 SignalSkipped. Daha gevşek paramların etkisi olmadı (Loop 49'a göre BBstd 1.8, RSI 45, volZ 0.3 → 0 emit). Piyasa "sıkışmış band" rejimi.

## Loop 54 — Volume Filter OFF + Maksimum Gevşeme

| Parametre | L49 | L53 | **L54** |
|---|---|---|---|
| `BbStdMultiplier` | 2.0 | 1.8 | **1.5** |
| `RsiOversoldThreshold` | 38 | 45 | **55** |
| `VolumeZScoreThreshold` | 0.5 | 0.3 | **0.0** |
| TpAtrMultiplier | 1.8 | 1.8 | 1.8 |
| SlAtrMultiplier | 0.9 | 0.9 | 0.9 |
| MaxHold | 120 | 120 | 120 |
| Cooldown | 3 bar | 3 bar | 3 bar |

`VolumeZScoreThreshold=0.0` → `volZ > 0.0` koşulu pratik olarak tüm pozitif volZ'leri kabul eder (volume filter OFF).

## Beklenti
- Bu ayar 5 koşuldan 4'ünü neredeyse serbest bırakır
- Eğer hala 0 emit kalırsa **strateji konseptinde başka sorun var** (snapshot null, log kontrolü)
- Beklenen: 30dk-1h içinde 1+ emit

## 5 Aktif Coin
BTC, ETH, XRP, SOL, ADA. MaxOpenPositions=5.

## Boot State
| Metrik | Değer |
|---|---|
| Mode | Paper |
| StartingBalance | $500 |
| Equity | $500 |
| Active | 5 (BbMeanReversion15m volZ off) |
| API Port | 5188 |

## Halt Eşikleri
- Realized < -$2.00 → Loop 55 binance-expert
- 4+ ardışık SL → Loop 55
- 0 emit (60dk içinde) → log debug + binance-expert (snapshot bug ihtimali)
- WR < %25 (5+ trade) → Loop 55

## Sıradaki Wakeup
**ScheduleWakeup 1800s → t=30dk (04:38 TR)**

— PM 2026-04-29 Loop 54 boot
