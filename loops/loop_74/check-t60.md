# Loop 74 — Check t=60dk (2026-05-01 12:35 TR) — RsiCeiling 50 Katı, 60'a Geri + backend-dev BG

## Sonuç: 0 Emit (RsiCeiling 50 katı), 60'a Geri + Loop 75 Background Hazırlık

MinScore 4 düzeltildikten sonra (Loop 74 t30) yine 0 emit. RsiCeiling 50 piyasa rejiminde RSI Zone tetiklemiyor. Loop 73'te RsiCeiling 60 ile 22 emit/90dk vardı → 60'a geri alındı.

**Paralel: backend-dev background → break-even SL implement (Loop 75 quick-win core).**

## Sayım (60dk)
| Metrik | Değer |
|---|---|
| **SignalEmitted** | **0** ⚠️ (RsiCeiling 50 katı) |
| SignalSkipped | 65 |
| OrderPlaced | 0 |
| RiskAlert | 0 |
| Realized | $0 |

## Hızlı Düzeltme #2 (Loop 74.6)

| Parametre | L74.5 | **L74.6** |
|---|---|---|
| `RsiNeutralCeiling` | 50 | **60** (Loop 73 emit-friendly) |
| `MinScoreThreshold` | 4 | 4 (sabit) |
| `TpAtrMultiplier` | 1.5 | 1.5 (sabit) |
| `SlAtrMultiplier` | 0.60 | 0.60 (sabit) |
| `MaxHoldMinutes` | 35 | 35 (sabit) |

→ Loop 73 + bazı ortak iyileştirmeler (TP biraz geniş, SL biraz gevşek, MaxHold biraz uzun). Emit beklenir.

## Loop 75 Background (backend-dev paralel iş)
**Task**: Break-even SL move implement (binance-expert spec):
- Trigger: `markPrice >= entry × (1 + 0.0010)` (+%0.10 UPnl)
- Action: `stopPrice = entry × (1 + 0.0002)` (+%0.02 garanti, fee karşılar)
- Idempotent: 1 pozisyona 1 kez
- Position entity yeni field: `BreakEvenAppliedAt`
- EF migration + KMS evaluator parametre + service hook + tests

**Beklenen**: Loop 73 t90 4/4 pozisyon +$0.08-0.14 UPnl idi → BE move tetiklenirdi → timestop loss yerine +%0.02 küçük kar veya TP hit.

Tahmini iş süresi: 1-1.5h. Hazır olunca Loop 75 boot edilir (bot kill + migration apply + DB UPDATE + restart).

## Karar
| Şart | Aksiyon |
|---|---|
| 0 emit + RsiCeiling 50 katı | RsiCeiling 60'a geri alındı ✓ |
| backend-dev BE SL implement BG | Loop 75 hazırlık ✓ |
| Loop 74 devam | t90 wakeup |

## t90 Beklenti (13:05 TR)
- RsiCeiling 60 ile emit gelmeli (Loop 73 = 22 emit/90dk pattern)
- Yeni timestop pattern olursa Loop 75 BE move ile fix
- backend-dev iş bitmiş olabilir → Loop 75 erken boot

## Halt Eşikleri
- Realized < -$0.30 → Loop 75 BE SL ZORUNLU (background iş bitmemişse hızlandır)
- Circuit breaker tekrar tripped → API reset (PascalCase!)
- Tüm trade timestop yine (4. defa) → Loop 75 BE SL deploy KESIN

## Sıradaki Wakeup
**ScheduleWakeup 1800s → t=90dk (13:05 TR)**

— PM 2026-05-01 Loop 74 check-t60 (RsiCeiling 60 + BG dev)
