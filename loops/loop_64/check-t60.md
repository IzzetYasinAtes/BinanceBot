# Loop 64 — Check t=60dk (2026-04-30 13:37 TR)

## Durum: 1h, 0 Emit (BB MeanRev tipik)

| Metrik | t30 | t60 | Δ |
|---|---|---|---|
| Cash / Equity | $500 / $500 | $500 / $500 | 0 |
| Realized | $0 | $0 | 0 |
| Open / Closed Pos | 0 / 0 | 0 / 0 | 0 |
| **SignalEmitted** | 0 | 0 | 0 |
| SignalSkipped | 144 | 282 | +138 (5/dk normal) |
| **RiskAlert** | 0 | 0 ✓ | 0 |

## Halt Kriter
| Kriter | Durum | Verdict |
|---|---|---|
| Realized < -$1.50 | $0 | ✓ |
| 3+ ardışık SL | 0 | ✓ |
| RiskAlert ≥ 1 | 0 | ✓ |
| Zombi pozisyon | 0 açık | ✓ |

**HALT YOK + Bot Sağlıklı.**

## Yorum
1h 0 emit. EMA200 trend filter 5 coin'in hepsini downtrend skip ediyor olmalı. Loop 49 örneği: ilk emit 2h sonra geldi.

## Karar
**Loop 64 DEVAM** ✓ t120 KESIN karar penceresi.

## Sıradaki Wakeup
**ScheduleWakeup 1800s → t=90dk (14:07 TR)**

— PM 2026-04-30 Loop 64 t=60
