# Loop 96 Boot — MTF Threshold Doğru Yön

Tarih: 2026-05-03 20:19 UTC | Bot port 5188

## Loop 95 → 96 Geçiş (Tek Satır Fix)

Loop 95 t60'ta frekans 3/h düştü (Loop 94'te 44/h). Sebep: PM spec'inde MTF threshold tune yön hatası (`0.001 → 0.0005` SIKIŞTIRDI, gevşetmedi).

**Mathematik**: `mtfThreshold = ema * X`. Long skip if `slope < -threshold`. X küçük → küçük slope bile skip → hassas → frekans donar.

**Fix** (commit `828ff5a`): `PatternCompositeEvaluator.cs:118` `0.0005m → 0.002m` (Loop 91'in 2x gevşek).

## Loop 95 Tune'ları Korunur

- WeightOverrides 7 Short detector = 0 ✓ (Long-only emit)
- TrailPct 0.003 ✓
- TriggerPct 0.003 ✓
- RequiredScore 3 ✓ (DB)
- MaxOpenPositions 5 ✓

## Boot State

- Bot ayakta, Wallet $500, 0 pos
- ResetCount: 10 (force-closed 2 + deleted 2 + 74 SystemEvents silindi)
- Build 0 hata 0 uyarı

## Beklenti (t30)

- Frekans Loop 94 seviyesine geri (~30+/h)
- Long-only emit (Short detector ağırlık 0)
- 5 coin'den emit (XRP/SOL/ETH dahil — Loop 95'te sadece BTC+ADA emit'i vardı)
- Pos açıldıkça TrailPct 0.003 ve TriggerPct 0.003 yansır

## KPI / Halt Eşikleri

- Halt: realizedPnl < -$1.50
- 0 emit > 1h → pivot
- Frekans hedefi: saatte 30+ trade

## Sonraki

ScheduleWakeup t30.
