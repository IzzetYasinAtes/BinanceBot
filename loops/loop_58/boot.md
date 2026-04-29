# Loop 58 Boot — BB MeanRev 15m Daha Agresif (2026-04-29 11:12 TR)

## Pivot Sebebi
Loop 57 t120 halt: 2h, 0 emit. Volume bug fix uygulandı (commit `e17e21d`) ama BBstd 1.8 + RSI 45 hala muhafazakar → emit yok.

## Loop 58 Parametreler

| Parametre | L57 | **L58** |
|---|---|---|
| `BbStdMultiplier` | 1.8 | **1.5** (band çok dar) |
| `RsiOversoldThreshold` | 45 | **55** (genişlet) |
| `MinAtrPct` | 0.0005 | **0.0003** (sessiz coin daha az dışla) |

Korunanlar:
- `VolumeZScoreThreshold`: 0.0 (volume off, fix bypass çalışıyor)
- TpAtr 1.8, SlAtr 0.9 (R:R 2:1)
- MaxHold 120dk, Cooldown 4 bar
- 5 coin BTC/ETH/XRP/SOL/ADA

## 5 Aktif Coin
BTC, ETH, XRP, SOL, ADA. MaxOpenPositions=5.

## Boot State
| Metrik | Değer |
|---|---|
| Mode | Paper |
| StartingBalance | $500 |
| Equity | $500 |
| Active | 5 (BB MeanRev15m daha agresif) |
| API Port | 5188 |

## Beklenti
- Frekans: 0/h → **2-5/h** (BBstd 1.5 + RSI 55 → daha sık tetikleme)
- WR: %30-45 (kalite biraz düşer)
- Net günlük: BE veya hafif kar

## Halt Eşikleri
- Realized < -$1.50 → Loop 59 binance-expert
- 4+ ardışık SL → Loop 59
- t60 = 0 emit → Loop 59 (BBstd 1.5 de yetmedi → BBstd 1.3 ya da farklı strateji)
- WR < %25 (5+ trade) → Loop 59

## Sıradaki Wakeup
**ScheduleWakeup 1800s → t=30dk (11:42 TR)**

— PM 2026-04-29 Loop 58 boot
