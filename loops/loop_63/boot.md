# Loop 63 Boot — Loop 62 ZOMBI BUG sonrası temiz reset (2026-04-30 11:26 TR)

## Pivot Sebebi
Loop 62 t60: 4 zombi pozisyon 1h22m+ açık (TimeStop bug) + 0 yeni order (RiskAlert iç state). 2 restart fayda etmedi → **DB tam reset** zorunlu.

**Bug rapor:** `loops/loop_62/halt-t60-zombie-bug.md` (backend-dev için issue dokümante edildi).

## Loop 63 = Loop 62 Aynı Param (sadece DB reset)

| Parametre | Değer |
|---|---|
| Coin | 5 (BTC, ETH, XRP, SOL, ADA) |
| `RsiLowerBand` | 35 |
| `RsiUpperBand` | 70 |
| `VolumeMultiplier` | 0.5 |
| `TpAtrMultiplier` | **1.2** (Loop 62'den) |
| `SlAtrMultiplier` | **0.5** |
| `MinTpPct` | **0.003** (%0.30) |
| `MaxTpPct` | **0.008** |
| `MaxHoldMinutes` | 10 |
| `CooldownBarsAfterSignal` | 3 |
| `MinAtrPct` | 0.0002 |
| MaxOpenPositions | 5 |
| MaxConsecutiveLosses | **5** (RiskAlert eşiği aynı) |

R:R = 1.2/0.5 = **2.4:1**, BE WR **%40**.

## Loop 41-62 Aggregate Final
| Loop | Trade | Realized |
|---|---|---|
| 41-58 | 47 | -$9.79 |
| 59-60 | 0 | $0 (16h sermaye koruma) |
| 61 | 10 | -$0.566 |
| 62 | 0 yeni | $0 (zombi bug) |
| **Total** | **57** | **-$10.36** | %18 WR |

## Boot State
| Metrik | Değer |
|---|---|
| Mode | Paper |
| Cash / Equity | $500 / $500 (DB reset) |
| Active | 5 EmaScalper1m |
| API Port | 5188 |

## Dikkat (Bug Sonrası)
- RiskAlert tetiklenirse Loop 64 boot zorunlu (zombi tekrarı önle)
- Backend-dev issue: RiskAlert reset endpoint gerekli (gelecek iş)
- Şimdilik manual workaround: DB reset her RiskAlert'te

## Halt Eşikleri (sıkı, zombi bug önle)
- Realized < -$1.50 → Loop 64 binance-expert (DAHA SIKI eşik, zombi bug riski)
- 4+ ardışık SL → Loop 64 (5 yerine 4, daha erken reset)
- WR < %30 (10+ trade) → Loop 64
- RiskAlert tetiklenirse → otomatik DB reset + Loop 64 boot

## Sıradaki Wakeup
**ScheduleWakeup 1800s → t=30dk (11:56 TR)**

— PM 2026-04-30 Loop 63 boot
