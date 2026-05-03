# Loop 89 Boot — Hard-Gate Skip TEKRAR Kaldırıldı (MTF + RSI Sahte Breakout Filtre) (2026-05-03 06:33 TR)

## Pivot Sebebi
Loop 88 1h 0 emit (MTF yumuşatma yetmedi). 3 gate kombinesi (hard-gate + MTF + RSI) çok katı. **Hard-gate skip tekrar kaldırıldı** (Loop 84 davranışı), AMA bu sefer Loop 87+88 ek filtreleri (MTF gate + RSI cap) sahte breakout'u eler.

## Loop 89 Değişiklik (1 satır kod)
`WeightedScorePatternComposer.cs`:
```csharp
// Eski (L86-L88): hardGateFails > 0 → skip
// Yeni (L89): _ = hardGateFails (skip yok, sadece tracking)
```

## Filtre Stack (Loop 89)
| Filtre | Durum | Sebep |
|--------|-------|-------|
| Hard-gate (volume_surge + spread_guard) | **OFF** (Loop 89) | 1h 0 emit dilemma |
| MTF gate (15m EMA slope < -%0.1) | ON | Sahte breakout filtre |
| RSI cap (RSI > 85) | ON | Aşırı alım filtre |
| RequiredScore 3 | ON | Skor eşiği (Loop 86) |
| BE.OffsetPct 0.0020 | ON | BE-stop pozitif spec (Loop 83) |
| Trail.TrailPct 0.0050 | ON | Trailing buffer (Loop 83) |

## Trade-off
- **Loop 84**: hard-gate OFF + MTF/RSI YOK = sahte breakout (3 ardışık SL)
- **Loop 89**: hard-gate OFF + MTF/RSI **AKTİF** = sahte breakout filtresi var

Beklenti: Loop 84 frekansı (14 emit/h) + Loop 87 kalitesi (MTF+RSI eler).

## Boot State
| Metrik | Değer |
|---|---|
| Bot PID | 19024 |
| Port | 5188 |
| Build | 0/0 ✓ |
| Hard-gate | OFF (Loop 89) |
| MTF Gate | ON (-%0.1 strict) |
| RSI Cap | 85 |
| RequiredScore | 3 |
| Diğer | Loop 87/88 sabit |
| CB | Healthy (Counter 0/4) |
| Açık | 0 |

## L80→L89 Stack
| Loop | Hard-gate | MTF | RSI | Score | Sonuç |
|------|-----------|-----|-----|-------|-------|
| L80-83 | ON | - | - | 5 | 0 emit |
| L84 | OFF | - | - | 5 | 14 emit/h sahte breakout |
| L85 | OFF | - | - | 4 | 11 emit, 3 ardışık SL |
| L86 | ON | - | - | 4→3 | 0→2 emit (sahte) |
| L87 | ON | ON | 75 | 3 | 0 emit (1.5h) |
| L88 | ON | yumuşak | 85 | 3 | 0 emit (1h) |
| **L89** | **OFF** | **yumuşak** | **85** | **3** | **TEST** |

## Cumulative L1-L88: -$17.04

## L89 KPI
- Realized 4h ≥ $0
- Frekans: 4-8 emit/h hedef
- WR ≥ %30 (MTF + RSI sahte breakout'u eleyince)

## Halt Eşikleri
- Realized < -$1.50 → halt + Loop 90
- 3 ardışık küçük loss (sahte breakout dönüş) → MTF/RSI yetmiyor
- 0 emit 1h → composer mantığı yanlış

## Sıradaki Wakeup
**ScheduleWakeup 1500s → t=30dk (06:58 TR)**

— PM 2026-05-03 Loop 89 boot (hard-gate OFF + MTF/RSI ON kombinasyonu, dilemma çözümü)
