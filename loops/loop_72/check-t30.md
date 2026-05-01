# Loop 72 — Check t=30dk (2026-05-01 07:39 TR)

## Sonuç: 0 emit / 30dk (Loop 71 ile aynı pattern, t60 bekle)

Param tune (MinScore 3, RsiCeiling 60, MaxTp 0.025) ilk 30dk: 0 emit. Loop 71'de de t30=0 idi → t60'ta 2 emit geldi. Pattern bekleniyor.

## Sayım (~30dk)
| Metrik | Değer |
|---|---|
| **SignalEmitted** | **0** |
| **SignalSkipped** | **30** (5 coin × 6 bar) |
| RiskAlert | **0** ✓ |
| Open Positions | **0** (zombi YOK ✓) |
| Realized | $0 (Loop 72 sonrası) |
| Cash carry-over | $500.85 ✓ |

## Bot Health
- PID 17624 ✓
- WS Streaming ✓
- 5 KMS Active (Status=3)
- Zombi YOK — bot restart sonrası temiz state ✓

## Loop Karşılaştırma
| Loop | t30 emit | t60 emit |
|---|---|---|
| L71 | 0 | 2 (XRP+ADA) |
| **L72** | **0** | ? |

→ Pattern aynı, t60 bekleniyor.

## Karar
| Şart | Aksiyon |
|---|---|
| 0 emit / 30dk | **Loop 72 devam, t60 bekle** |
| Zombi YOK | ✓ Loop 73 fix beklemede |
| RiskAlert = 0 | ✓ |

## t60 (08:09 TR) Beklenti
- ≥2 emit (param permisif daha çok)
- BTC/ETH'ten emit (asimetri çözümü devam)
- Realized > $0 (TP win)

## Halt Eşikleri
- Realized < -$0.50 → Loop 73 binance-expert
- 5+ ardışık SL → halt
- t60 yine 0 emit → Loop 73 daha permisif (MinScore 3 → 2 risk)
- Zombi tekrar → Loop 73 backend-dev PaperFill state machine fix

## Sıradaki Wakeup
**ScheduleWakeup 1800s → t=60dk (08:09 TR)**

— PM 2026-05-01 Loop 72 check-t30
