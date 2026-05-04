# Loop 102 Boot — BE TriggerPct 0.001 (Pazar Volatilitesi Eşiği)

Tarih: 2026-05-04 09:02 UTC | Bot port 5188

## Loop 101 → 102 Geçiş

Loop 101 t150 halt: BE eşiği +0.20% asla aşılmadı (21 loop boyunca peak max +0.18%). Pos açıldıktan sonra peak +0.087% ile +0.18% arası kalıyor → trailing locked profit yok → win küçük (avg $0.03), loss SL hit (avg -$0.63).

## Tune (PM Doğrudan)

- `appsettings.json:56` BreakEven.TriggerPct 0.0020 → **0.0010** ✓
- `appsettings.json:57` BreakEven.OffsetPct 0.0020 → korunuyor (0.0020 — SL move sonrası ofset kalıcı)
- 5 strateji ParametersJson BeMoveTriggerPct **0.001**, BeMoveOffsetPct **0.001** ✓ (replace_all)

## Korunur (Loop 95-101 fix'leri)
- **Status=3 (Active)** kritik ✓
- WeightOverrides 7 Short=0 (Long-only emit)
- TrailPct 0.003 (winning trade pencere)
- MTF threshold 0.001m strict (downtrend filter)
- RiskPerTradePct 0.01 (pos sizing)
- MaxOpenPositions 3
- RequiredScore 2

## Boot State

- Bot ayakta, port 5188
- Wallet $500, 0 pos
- ResetCount 18, force-closed 3 + deleted 6 + 9 orders + 201 events
- Counter=0, CB=Healthy
- **Strategies Active=3** ✓

## Hipotez

Loop 102 BE arm matematik:
- Peak +0.10% (Loop 101'de gözlemlenen aralık) → BE arm
- SL move: entry × 1.002 (OffsetPct=0.002)
- Trailing %0.3: peak × 0.997 → exit
- Net winning: +0.07% gross - 2× fee = +0.05% net (~$0.05/pos)

Eğer peak +0.18% yakalarsa (en iyi senaryo): trailing exit +%0.15 - 2× fee = +%0.13 net = +$0.13/pos.

**İlk pozitif loop hedefi (21 loop sonra)**.

## Cumulative

21 loop -$23.4, 0 pozitif loop.

## Sonraki

ScheduleWakeup t30.
