# Loop 68 Boot — KMS Daha Gevşek Param (2026-05-01 00:31 TR)

## Pivot Sebebi
Loop 67 t60: 0 emit (RSI recovery 32 + TradeCount 1.1× + Spread 0.0015 sıkı kombinasyon).

## Loop 68 Param

| Parametre | L67 | **L68** |
|---|---|---|
| `RsiRecoveryThreshold` | 32 | **35** (oversold pencere genişler) |
| `TradeCountMultiplier` | 1.1 | **0.8** (TradeCount eşiği gevşek) |
| `SpreadThresholdPct` | 0.0015 | **0.005** (testnet spread daha gevşek) |

Diğer aynı: RsiPeriod=14, EmaPeriod=9, TradeCountWindow=20, AtrPeriod=14, TpAtr=1.8, SlAtr=0.75, MinTp=0.005, MaxTp=0.018, MinSl=0.003, MaxSl=0.008, MaxHold=45, MinAtr=0.0005, Cool=3.

## Boot State
| Metrik | Değer |
|---|---|
| Cash / Equity | $500 / $500 (DB reset) |
| Active | 5 KMS (BTC/ETH/XRP/SOL/ADA) |
| Param güncellendi | DB UPDATE Strategies WHERE Name LIKE '%-KMS' (5 row) |

## Beklenti
- Frekans: 5-15 emit/h (gevşek param)
- Hedef: en az 2 emit / 30dk

## Halt Eşikleri
- Realized < -$1.50 → Loop 69 binance-expert
- 5+ ardışık SL → otomatik halt
- 0 emit (60dk) → Loop 69 daha agresif (RSI 35→40, TradeCountMul 0.8→0.5)

## Sıradaki Wakeup
**ScheduleWakeup 1800s → t=30dk (01:01 TR)**

— PM 2026-05-01 Loop 68 boot
