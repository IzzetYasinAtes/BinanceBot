# Loop 99 Spec — Filter Relax (Pazar Yatay Bile Emit Gelsin)

Tarih: 2026-05-04 | Author: PM | Status: Boot ready

## Bağlam

Loop 98 1h35m 0 emit (pazar yatay, low volatility). Bot her şey doğru, sadece detector eşikleri tetiklenmiyor.

Detay: `loops/loop_98/halt-t60.md`

## Tune (PM Doğrudan)

### Tune #1: RS 2 → 1
- `appsettings.json` Strategies.Seed[].ParametersJson 5 strateji ✓
- DB UPDATE: `UPDATE Strategies SET ParametersJson = JSON_MODIFY(...,'$.RequiredScore', 1) WHERE Type=3`
- 1 detector tetiklense bile emit (en agresif)

### Tune #2: AdxOutsideRegimeMultiplier 0.7 → 1.0
- appsettings + DB UPDATE
- Adx filter etki yok (score'u düşürmesin)

### Tune #3: CooldownBarsAfterSignal 2 → 1
- appsettings + DB UPDATE
- Emit sonrası bekleme yarıya (frekans için)

### Tune #4: MTF Threshold 0.002 → 0.005
- `PatternCompositeEvaluator.cs:118` Edit ✓
- Slope skip eşik %0.5 (2.5x daha gevşek)

## Korunur (Loop 98 sizing tune'ları)
- RiskPerTradePct 0.01 (pos sizing)
- MaxOpenPositions 3
- TriggerPct 0.002 (BE arm)
- TrailPct 0.003
- WeightOverrides 7 Short=0 (Long-only)

## Hipotez

Loop 99 hedef:
- Pazar yatay olsa bile emit gelsin (RS=1, MTF gevşek, Cooldown=1)
- Pos sizing test ÇALIŞSIN (Loop 98'den taşınan)
- 3 ardışık SL hit olsa -$1 toplam (eşik AŞILMAZ)

## Cumulative

19 loop -$21.5, 0 pozitif loop. Loop 99 = filter relax + pos sizing kombo testi.

## Sonraki

Backend-dev VEYA PM doğrudan: appsettings + DB update + bot restart + boot.md.
