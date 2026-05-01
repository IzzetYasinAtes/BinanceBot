# Loop 76 — Check t=60dk (2026-05-01 16:41 TR) — MinScore 4 Sonra Hala 0 Emit

## Sonuç: 0 Emit / 65 skip — RsiCeiling 60 da Katı, 70'e Geri

MinScore 4 düzeltmesinden sonra (t30) bot 30dk daha geçti — yine 0 emit. RsiCeiling 60 + RSI overbought rejimde RSI Zone tetiklenmiyor. KMS skip log Debug seviyesinde (görünmez), ama hipotez: RSI > 60 son saatlerde kalıcı.

**Hızlı düzeltme #2**: RsiCeiling 60→70 (RSI Zone genis pencere).

## Sayım (60dk)
| Metrik | Değer |
|---|---|
| **SignalEmitted** | **0** ⚠️ |
| SignalSkipped | 65 |
| OrderPlaced | 0 |
| RiskAlert | 0 |
| Realized | $0 |

## Param History (Loop 76)
| Aşama | MinScore | RsiCeiling | Sonuç |
|---|---|---|---|
| Boot | 5 | 60 | 0 emit (t30) |
| t30 düzeltme | 4 | 60 | 0 emit (t60) |
| **t60 düzeltme #2** | **4** | **70** | bekliyoruz |

→ Asıl sorun pazar koşulu (RSI 60+ rejimde stable). Loop 73'te aynı paramlar emit veriyordu (RSI 40-60 oscillation pencere).

## Karar
| Şart | Aksiyon |
|---|---|
| 0 emit + RsiCeiling 60 katı | **RsiCeiling 60→70 düzeltildi ✓** |
| Trailing module aktif | ✓ Henüz test edilemedi (emit yok) |

## t90 Beklenti (17:11 TR)
- RsiCeiling 70 ile yeni emit gelmeli
- Pozisyon açıldıktan sonra BE applied → trailing aktif → TRAILING-EXIT log
- Realized iyileşmesi başlamalı

## Halt Eşikleri
- Realized < -$0.50 (Loop 76) → Loop 77 EMA200+BBW (entry kalitesi)
- t90 hala 0 emit → MinScore 4→3 (daha permisif)
- 5+ ardışık SL → CB reset

## Sıradaki Wakeup
**ScheduleWakeup 1800s → t=90dk (17:11 TR)**

— PM 2026-05-01 Loop 76 check-t60 (RsiCeiling 70 düzeltme)
