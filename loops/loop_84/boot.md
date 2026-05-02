# Loop 84 Boot — Hard-Gate Skip Devre Dışı (2026-05-02 18:50 TR)

## Pivot Sebebi
Loop 83 t60: 0 emit 1h → RequiredScore 5→4 düşürüldü. Loop 83 t90: hâlâ 0 emit (1.5h+). Sebep: composer hard-gate fail (volume_surge_gate veya spread_guard_gate) skor toplama bile başlamadan skip ediyor.

## Loop 84 Değişiklik
**Composer'da hard-gate fail SKIP DEVRE DIŞI**:
- `WeightedScorePatternComposer.cs` — `if (hardGateFails.Count > 0) skip` bloğu kaldırıldı
- Hard-gate detector'lar zaten DefaultWeight=0 (skor toplamına 0 katkı)
- Yeni davranış: Hard-gate fail edebilir ama kompozit skor diğer pattern'lerden gelir
- 2 test silindi (Compose_HardGateFail_VolumeSurge_SkipsWithReason + SpreadGuard)

### Diğer Paramlar Sabit
- BreakEven.OffsetPct: 0.0020 (Loop 83)
- TrailingStop.TrailPct: 0.0050 (Loop 83)
- RequiredScore: 4 (Loop 83 değişimi korundu)

## Beklenti
- Volume düşük gece saatlerinde de pattern stack threshold ≥4 sağlayabilir
- Sahte breakout riski tolere edilir (küçük loss < 0 emit)
- Memory Golden Rule #12: 5 coin + sürekli işlem + kartopu

## Trade-off
- ✗ Sahte breakout/whipsaw artışı (fake volume / wide spread durumlarda)
- ✓ Sürekli işlem (frekans hedefi)
- ✓ Loop 83 BE-stop pozitif tasarımı test fırsatı

## Boot State
| Metrik | Değer |
|---|---|
| Bot PID | 15732 |
| Port | 5188 |
| Build | 0/0 ✓ |
| Tests | 320/320 PASS (2 hard-gate test silindi) |
| 5 Pattern Strateji | Active |
| CB | Healthy (Counter 0/4) |
| RequiredScore | 4 (Loop 83'ten) |

## L80/L81/L82/L83/L84 Değişiklik Stack
| Loop | Değişiklik |
|------|-----------|
| L80 | ADX gate + BBR vol surge + counter fix |
| L81 | Pattern-based scalping (10 pattern + 2 hard-gate + 1 soft) |
| L82 | Trailing 0.0015→0.0025, BE Trigger 0.0010→0.0020, MinSL 0.006→0.004 |
| L83 | BE Offset 0.001→0.002, Trail 0.0025→0.0050 + RequiredScore 5→4 |
| **L84** | **Composer hard-gate skip devre dışı** (sadece skor-tabanlı) |

## Halt Eşikleri
- Realized < -$1.50 → halt + Loop 85
- 3+ ardışık küçük loss → BE/Trail spec yanlış
- 0 emit 2h yine → daha radikal değişiklik (RequiredScore 3 veya pattern weight tune)

## Sıradaki Wakeup
**ScheduleWakeup 1800s → t=30dk (19:23 TR)**

— PM 2026-05-02 Loop 84 boot (composer hard-gate skip kaldırıldı, sadece skor-tabanlı emit, frekans öncelik)
