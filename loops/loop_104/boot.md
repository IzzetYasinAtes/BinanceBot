# Loop 104 Boot — DB BE 0.001 Fix (Seeder Preserve Bug)

Tarih: 2026-05-04 18:03 UTC | Bot port 5188

## Loop 103 → 104 Geçiş

Loop 103 sonu (4h+ pasif): 4 close 0W (avg loss -$0.13 küçük), realized -$0.51. Sonra 4+ saat 0 emit (pazar yatay).

**Tespit**: Loop 102'de appsettings'te BeMoveTriggerPct 0.002 → 0.001 yaptım, AMA StrategySeeder mevcut DB değerini PRESERVE ediyor (override etmiyor). DB'de hala 0.002 kalmış. Pos açılırken peak +0.20% asla aşılmıyor.

**Fix**: SQL UPDATE ile DB ParametersJson BeMoveTriggerPct + BeMoveOffsetPct 0.002 → **0.001** (5 strateji).

## Mevcut Tüm Parametre Setı (Loop 104)

DB ParametersJson Strategy 901:
- RequiredScore: 1 (en agresif frekans)
- BeMoveTriggerPct: **0.001** (=+0.10% BE arm) ✓ FİX
- BeMoveOffsetPct: **0.001** (=+0.10% SL move) ✓ FİX
- AdxOutsideRegimeMultiplier: 1.0 (Adx no-op)
- CooldownBarsAfterSignal: 1
- WeightOverrides: 7 Short=0 (Long-only)
- RsiMaxEmit: 75

RiskProfile:
- RiskPerTradePct: 0.01
- MaxOpenPositions: 3

Kod-içi:
- MTF threshold: 0.002 (denge)
- TrailPct: 0.003
- TriggerPct: 0.001 (BreakEvenOptions appsettings global)

## Boot State
- Bot ayakta, port 5188
- Wallet $500, 0 pos
- ResetCount 20, deleted 4 pos + 9 orders + 231 events
- Counter=0, CB=Healthy, Strategies Active=3 ✓

## Hipotez

Loop 104: BE TriggerPct ETKİN 0.001 (DB de fix). Peak +0.10% civarında BE arm + trailing %0.3 → küçük locked profit. Loop 103'te peak'ler hep entry yakını, BE arm asla olmadı (çünkü DB hala 0.002 idi).

**İlk pozitif loop hedefi (24 loop sonra)**.

## Cumulative

24 loop -$24.5, 0 pozitif loop.

## Sonraki

ScheduleWakeup t30.
