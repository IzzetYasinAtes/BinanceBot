# Loop 67 — Check t=30dk (2026-04-30 23:58 TR)

## Durum: 30dk, 0 Emit (5m bar erken)

| Metrik | Boot | t30 |
|---|---|---|
| Cash / Equity | $500 / $500 | $500 / $500 |
| Realized | $0 | $0 |
| Open / Closed Pos | 0 / 0 | 0 / 0 |
| **SignalEmitted** | 0 | 0 |
| SignalSkipped | 0 | 1552 (yüksek — her tick eval) |
| **RiskAlert** | 0 | 0 ✓ |

## Halt Kriter
| Kriter | Durum | Verdict |
|---|---|---|
| Realized < -$1.50 | $0 | ✓ |
| 5+ ardışık SL | 0 | ✓ |
| RiskAlert ≥ 1 | 0 | ✓ |

**HALT YOK.**

## Yorum
30dk = 5m bar × 6 değerlendirme. RSI recovery koşulu (RSI prev<32 + curr>32) çok nadir tetiklenir, 6 bar normal. SignalSkipped 1552 yüksek — KMS evaluator her tick (her kline event) çalışıyor olabilir, BarClosed kontrolü skip kategorisinde sayıyor.

## Karar
**Loop 67 DEVAM** ✓ ScheduleWakeup t60.

## Sıradaki Wakeup
**ScheduleWakeup 1800s → t=60dk (00:28 TR ertesi gün)**

t60 KESIN: 0 emit ise param sıkılaştır → Loop 68 (TradeCountMul 1.1→1.3, RsiRecoveryThreshold 32→34).

— PM 2026-04-30 Loop 67 t=30
