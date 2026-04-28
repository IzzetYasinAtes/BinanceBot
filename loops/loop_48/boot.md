# Loop 48 Boot — EmaScalper1m Orta Yol Parametre (2026-04-28 12:13 TR)

## Pivot Sebebi
Loop 47 t60 halt: 2 sinyal/60dk = 2/h frekans (hedef 8-12/h). Filtre güçlendirme aşırı oldu — 5 parametre birden sıkıldı, AND kombine olasılığı çöktü.

Loop 46 (gevşek, 19/h, %27 WR) ve Loop 47 (sıkı, 2/h) deneyimi → orta yol.

## Orta Yol Parametre

| Parametre | Loop 46 (gevşek) | Loop 47 (sıkı) | **Loop 48 (orta)** |
|---|---|---|---|
| `RsiLowerBand` | 40 | 45 | **42** |
| `RsiUpperBand` | 65 | 60 | **63** |
| `VolumeMultiplier` | 0.8 | 1.2 | **1.0** |
| `MinAtrPct` | 0.0003 | 0.0005 | **0.0004** |
| `MaxHoldMinutes` | 8 | 12 | **10** |
| `TpAtrMultiplier` | 1.5 | 1.2 | **1.2** ← Loop 47 mantığı korundu |

Diğer aynı: KlineInterval=1m, EmaFast=9, EmaSlow=21, RsiPeriod=14, VolumeWindow=20, AtrPeriod=14, SlAtrMultiplier=0.8, R:R 1.5:1, Cooldown=2 bar, MinTpPct=0.003, MaxTpPct=0.008, MinSlPct=0.002, MaxSlPct=0.005

12 coin: BTC, ETH, BNB, XRP, SOL, ADA, DOGE, LINK, DOT, AVAX, LTC, TRX

## Beklenti
- Frekans: 8-15/h (Loop 46 19/h ve Loop 47 2/h ortası)
- WR: %35-45 (kalite-frekans dengeli)
- BE WR: %40 (R:R 1.5:1)

## Boot State
| Metrik | Değer |
|---|---|
| Mode | Paper |
| StartingBalance | $500.0000 |
| CurrentCash | $500.0000 |
| Equity | $500.0000 |
| Active Strategies | 12 (EmaScalper1m orta) |
| API Port | 5188 |
| Branch | development |

## Halt Eşikleri
- Realized < -$1.50 → Loop 49 (binance-expert tetikle: alternatif strateji)
- 5+ ardışık SL/TimeStop → Loop 49 (binance-expert)
- Signals 0-3 (60dk) → Loop 49 (filtre yine sıkı, gevşet veya pivot)
- WR < %25 (5+ trade) → Loop 49

## Sıradaki Wakeup
**ScheduleWakeup 1800s → t=30dk (12:43 TR)**

— PM 2026-04-28 Loop 48 boot
