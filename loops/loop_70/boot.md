# Loop 70 Boot — KMS Daha Daha Gevşek Param (2026-05-01 03:36 TR)

## Pivot Sebebi
Loop 68 t180: 4 emit/180dk = 1.3/h frekans (hedef 5-15/h alti). BTC/ETH 36 değerlendirme 0 emit asimetri. ADA win +$0.11 ile yön doğru ama param yetersiz gevşek.

## Loop 70 Param

| Parametre | L68 | **L70** |
|---|---|---|
| `RsiRecoveryThreshold` | 35 | **38** (oversold pencere daha geniş) |
| `TradeCountMultiplier` | 0.8 | **0.6** (TC eşiği daha gevşek) |
| `MinAtrPct` | 0.0005 | **0.0003** (sessiz piyasa filter daha gevşek) |
| `SpreadThresholdPct` | 0.005 | **0.005** (sabit) |

Diğer aynı: RsiPeriod=14, EmaPeriod=9, TC Window=20, AtrPeriod=14, TpAtr=1.8, SlAtr=0.75, MinTp=0.005, MaxTp=0.018, MinSl=0.003, MaxSl=0.008, MaxHold=45, Cool=3.

## Boot State
| Metrik | Değer |
|---|---|
| Cash / Equity | $500 / $500 (DB reset + VirtualBalance manuel seed) |
| Active | 5 KMS (BTC/ETH/XRP/SOL/ADA) |
| Param güncellendi | DB UPDATE Strategies WHERE Name LIKE '%-KMS' (5 row) |
| Bot PID | 16124 (BinanceBot.Api.exe) |
| WS State | Connecting → Connected → Subscribing → Streaming ✓ |
| Warmup | 5 coin tamamlandı (BTC/ETH/XRP/SOL/ADA) |
| DB reset | OrderFills 6 / Orders 6 / Positions 3 / SystemEvents 223 / VBalance 1 |

## Beklenti
- Frekans: 5-15 emit/h (gevşek param)
- Hedef: en az 3 emit / 30dk
- BTC/ETH'ten en az 1 emit (RSI 38 ile recovery cross daha sık)

## Halt Eşikleri
- Realized < -$1.50 → Loop 71 binance-expert pivot (skor tabanlı evaluator)
- 5+ ardışık SL → otomatik halt
- 0 emit (60dk) → Loop 71 daha agresif (RSI 38→42, TC 0.6→0.4)
- BTC/ETH 90dk 0 emit → asimetri persistent → algoritma değişikliği

## Sıradaki Wakeup
**ScheduleWakeup 1800s → t=30dk (04:06 TR)**

— PM 2026-05-01 Loop 70 boot
