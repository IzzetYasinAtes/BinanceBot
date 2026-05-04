# Loop 101 Boot — MTF Strict + Status=3 + Pos Sizing

Tarih: 2026-05-04 06:26 UTC | Bot port 5188

## Loop 100 → 101 Geçiş

Loop 100 t160 halt: realized -$1.26 (eşik marj $0.24). 3 close (1W/2L %33), R:R 1:16. Tüm Long pos Peak entry'nin altında → BE arm asla olmadı (downtrend pazarda Long açma toxic).

## Tune (PM Doğrudan)

### Tune #1: MTF Threshold 0.005 → 0.001 (Loop 91 strict değer) ✓
- `PatternCompositeEvaluator.cs:118` Edit yapıldı, build 0 hata
- Long skip eşiği daraltıldı: pazar slope -%0.1'den daha negatifse Long skip
- Downtrend Long emit'i öler — pos sadece uptrend açılır

### Korunur (Loop 95-100 fix'leri)
- **Status=3 (Active)** kritik (Loop 100 5h+ silent bug fix)
- WeightOverrides 7 Short=0 (Long-only emit)
- TriggerPct 0.002 (BE arm)
- TrailPct 0.003 (winning pencere)
- RiskPerTradePct 0.01 (pos sizing)
- MaxOpenPositions 3
- RequiredScore 2
- AdxMultiplier 1.0, Cooldown 1

## Boot State

- Bot ayakta, port 5188
- Wallet $500, 0 pos, AllocatedMargin $0
- ResetCount 17, deleted 5 pos + 12 orders + 152 events
- Counter=0, CB=Healthy
- **Strategies Active=3** ✓ (Loop 95-99'da Draft=1 yapmıştım, Loop 101'de doğru)
- 5 coin × 10 Long detector (7 Short ağırlık 0)
- MTF strict %0.1 (downtrend filter)

## Hipotez

Loop 101 hedef:
- Pazar uptrend ise Long emit (MTF strict pas geçirir)
- Pazar downtrend ise 0 emit (MTF skip)
- Pos açıldığında peak entry üstüne çıkma ihtimali yüksek (uptrend selektif)
- BE arm + trailing %0.3 lock

İlk pozitif loop hedefi (20 loop sonra).

## Cumulative

20 loop -$22.8, 0 pozitif loop.

## Sonraki

ScheduleWakeup t30.
