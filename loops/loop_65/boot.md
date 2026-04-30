# Loop 65 Boot — BB MeanRev v2 + EMA200 + Daha Gevşek (2026-04-30 14:42 TR)

## Pivot Sebebi
Loop 64 t120 halt: 2h 0 emit (BBstd 2.0 + RSI 35 sıkı + EMA200 trend filter downtrend skip).

## Loop 65 Param (Daha Gevşek)

| Parametre | L64 | **L65** |
|---|---|---|
| `BbStdMultiplier` | 2.0 | **1.8** (band daha dar) |
| `RsiOversoldThreshold` | 35 | **40** (daha geniş) |
| `VolumeZScoreThreshold` | 0.3 | **0.1** (neredeyse off) |
| `MinAtrPct` | 0.0005 | **0.0003** |
| EMA200 trend filter | KORU ✓ | **KORU ✓** (anti-disaster) |
| Diğer | aynı | aynı |

5 coin (BTC/ETH/XRP/SOL/ADA), MaxOpenPos=3, MaxConsecLosses=3.

## Boot State
| Metrik | Değer |
|---|---|
| Cash / Equity | $500 / $500 (DB reset) |
| Active | 5 BB MeanRev15m (yeni param DB UPDATE ile) |
| API Port | 5188 |

**Önemli teknik not:** Strategies seed mantığı "INSERT IF NOT EXISTS" — appsettings güncellendi ama DB'de mevcut strategy'ler eski param ile kaldı. Manuel UPDATE Strategies SET ParametersJson... ile fix edildi.

## Halt Eşikleri
- Realized < -$1.50 → Loop 66 binance-expert
- 3+ ardışık SL → otomatik halt
- RiskAlert ≥ 1 → DB reset + Loop 66
- 2h 0 emit → daha gevşek (BBstd 1.5, RSI 45) Loop 66

## Sıradaki Wakeup
**ScheduleWakeup 1800s → t=30dk (15:12 TR)**

— PM 2026-04-30 Loop 65 boot
