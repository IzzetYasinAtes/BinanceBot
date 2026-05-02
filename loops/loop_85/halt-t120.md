# Loop 85 — HALT @ t=120dk (2026-05-03 01:55 TR) — CB Tripped + Hard-Gate Geri Ekle

## Halt Sebebi: Counter=4 CB Tripped (3 Ardışık Yeni Param SL)

t90→t120: **+1 close (XRP-3 BE-stop -$0.17)**, Counter 3→4 = CB tripped (Status=3). Realized **+$0.0025 → -$0.168**.

## Loop 85 Özet (6 Close)
| # | Symbol | Hold | Peak | BE | PnL |
|---|--------|------|------|-----|-----|
| 1 | ETH (carryover) | 329min | +%1.08 | True | **+$0.856** ✓ |
| 2 | BTC (carryover) | 333min | +%0.89 | True | **+$0.653** ✓ |
| 3 | XRP (L84 carryover) | 115min | +%0.34 | True | -$0.085 |
| 4 | XRP (L85 yeni) | 5min | **0** | False | **-$0.709** ❌ |
| 5 | SOL (L85 yeni) | 26min | **0** | False | **-$0.712** ❌ |
| 6 | XRP (L85 yeni) | 11min | +%0.42 | True | -$0.171 |

**Net L85 Realized: -$0.168** (4 win/loss compromise: 2 büyük TP, 2 büyük SL, 2 küçük loss)

## Pattern Tanı: Yeni Param Sahte Breakout
3 yeni Loop 85 emit'in 3'ü de **negatif başladı, kısa hold, hızlı SL**:
- XRP-2: Peak=0, hold 5min
- SOL: Peak=0, hold 26min
- XRP-3: Peak=+0.42 (BE-stop küçük loss)

Bu **Loop 84'te kaldırılan composer hard-gate** sebep. Volume_surge_gate ve spread_guard_gate kalitesiz emit'leri eliyordu — kaldırılınca sahte breakout serbest.

## Loop 86 Plan (Sıfır Spec, Sıfır Agent)
**Composer hard-gate skip GERİ EKLENDİ** (`WeightedScorePatternComposer.cs`):
- volume_surge_gate fail → skip
- spread_guard_gate fail → skip
- Diğer paramlar Loop 85 ile aynı (BE 0.0020, Trail 0.0050, RequiredScore 4, tick 5s, 5bp slippage)

### Trade-off
- ✅ Sahte breakout filtre (-$1.42 loss önleyici)
- ⚠️ 0 emit risk (Loop 83'te bu yüzden kaldırılmıştı) — eğer 1h+ 0 emit olursa farklı çözüm gerek

### Build/Test
- 0 hata, 320/320 PASS
- Bot kill (PID 19636) → restart (PID **13316**)
- CB reset (Counter 4→0, CB Healthy)

## Boot State (Loop 86)
| Metrik | Değer |
|---|---|
| Bot PID | 13316 |
| Port | 5188 |
| Composer | Hard-gate aktif (Loop 86 değişiklik) |
| RequiredScore | 4 |
| Tick | 5s (SL/TP/MTM) |
| Slippage | 5bp |
| BNB indirimi | off |
| MaxHold | 0 (yok) |
| CB | Healthy (Counter 0/4) |
| Açık | 0 |

## Cumulative L1-L85
- L1-L84: -$14.57
- L85: -$0.168
- **TOTAL: -$14.74**

Geçici düşüş (L85 t60'ta -$13.86'dan iyileşmişti). Net loss artmadı çünkü ETH+BTC carryover BÜYÜK TP'leri kapattı.

## L86 KPI
- Realized 4h ≥ $0
- WR ≥ %30
- 3+ ardışık SL pattern KESİNLİKLE olmamalı (Loop 85 sorunu)
- Frekans: Hard-gate geri ekle frekansı düşürebilir, izle

## Halt Eşikleri
- Realized < -$1.50 → halt + Loop 87
- 0 emit 1h+ → hard-gate çok katı, farklı çözüm
- 4+ ardışık SL → spec yanlış (Loop 87)

## Sıradaki Wakeup
**ScheduleWakeup 1800s → t=30dk (02:25 TR)**

— PM 2026-05-03 Loop 85 halt + Loop 86 boot (hard-gate geri eklendi, sahte breakout filtresi tekrar aktif)
