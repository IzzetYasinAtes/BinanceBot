# Loop 97 Boot — TriggerPct %0.20 (Loop 91 değer) + RS 2

Tarih: 2026-05-04 21:57 UTC | Bot port 5188

## Loop 96 → 97 Geçiş (Hızlı Tune)

Loop 96 t90 halt: realized -$1.30 (eşik marj $0.20), BE eşiği ulaşılamadı (BTC peak +%0.28 max, TriggerPct=%0.30 → BE NEVER ARMED).

**Fix** (commit `ca893f0`):
1. `appsettings.json:56` TriggerPct 0.003 → **0.002** (Loop 91 değer, BE arm olabilsin)
2. DB UPDATE: 5 strateji ParametersJson RequiredScore 3 → **2** (frekans artır)

## Korunur (Loop 95+96 fix'leri)
- WeightOverrides 7 Short detector = 0 ✓
- TrailPct 0.003 ✓ (winning trade pencere)
- MTF threshold 0.002m ✓ (gevşek doğru yön)
- MaxOpenPositions 5 ✓
- OffsetPct 0.002 ✓

## Boot State

- Bot ayakta, Wallet $500, 0 pos
- ResetCount 11, force-closed 4 (Loop 96'dan kalan açık pos), deleted 6, 117 SystemEvents silindi
- CB Healthy

## Hipotez Test

Loop 91 BE-stop spec MATEMATIKSEL doğru (Memory'de geçti):
- BTC peak +%0.20 → BE arm SL=entry*1.002
- Trailing %0.3 (Loop 95 tune'u korunur, Loop 91'de %0.5 idi — daha geniş TrailPct 0.005)
- Beklenti: BE arm sonrası trailing locked profit (+$0.05 ile +$0.20 arası kar koruma)

Hipotez doğrulanırsa: Loop 96 BTC senaryosu tekrarlasa bile bu sefer kar realize olur (Loop 96'da kayboldu çünkü BE arm olmadı).

Frekans: RS=2 ile daha çok pos açılışı, MaxOpen=5 dolu olur, sirkülasyon hızlanır.

## KPI / Halt Eşikleri

- Halt: realizedPnl < -$1.50
- 0 emit > 1h → pivot
- Frekans hedef: 30+/h

## Sonraki

ScheduleWakeup t30.

## Cumulative 17 Loop

-$19.50, 0 pozitif loop. Loop 97 ilk gerçek BE+trailing test.
