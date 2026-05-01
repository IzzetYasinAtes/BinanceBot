# Loop 70 — Check t=30dk (2026-05-01 04:10 TR)

## Sonuç: 0 Emit (Loop 68 ile aynı pattern), t60 KESIN bekle

KMS daha daha gevşek param (RSI 38, TC 0.6, MinAtr 0.0003, Spread 0.005) ilk 35dk: 0 emit, 35 skip. Loop 68'de de t30=0 idi → t60 bekle.

## Sayım (~35dk)
| Metrik | Değer |
|---|---|
| **SignalEmitted** | **0** |
| **SignalSkipped** | **35** (5 coin × 7 bar) |
| OrderPlaced | 0 |
| OrderFilled | 0 |
| RiskAlert | **0** ✓ |
| PositionOpened | 0 |
| PositionClosed | 0 |
| Realized | $0 |
| Open Positions | 0 |

## Bot Health
- PID 16124 (BinanceBot.Api.exe) ✓
- WS Streaming ✓ (warmup tamam)
- 5 KMS Active ✓ (Status=3)
- DB temiz baseline (Loop 70 boot 00:35 UTC sonrası)

## Loop 68 vs Loop 70 (Aynı timepoint)
| Metrik | L68 t30 | L70 t30 |
|---|---|---|
| SignalEmitted | 0 | **0** |
| SignalSkipped | 30 | **35** |
| Param RSI/TC | 35 / 0.8 | **38 / 0.6** |

→ Param gevşek olmasına rağmen ilk 30dk emit gelmedi. Loop 68'de t60'a ulaşınca 2 emit gelmişti — KMS RSI cross gate inherently nadir tetikleniyor.

## Karar
| Şart | Aksiyon |
|---|---|
| 0 emit / 30dk | **Loop 70 devam, t60 KESIN bekle** |
| RiskAlert = 0 | ✓ |
| Realized = $0 | ✓ |

## t60 KESIN (04:36 TR)
- ≥1 emit → Loop 70 devam, t90 wakeup
- 0 emit → **Loop 71 KESIN PIVOT** (binance-expert: skor tabanlı evaluator + RSI continuous yerine cross)

## Halt Eşikleri
- Realized < -$1.50 → Loop 71 binance-expert pivot
- 5+ ardışık SL → halt
- 0 emit (60dk) → Loop 71 binance-expert (param tune yetmiyor)

## Sıradaki Wakeup
**ScheduleWakeup 1800s → t=60dk (04:40 TR)**

— PM 2026-05-01 Loop 70 check-t30
