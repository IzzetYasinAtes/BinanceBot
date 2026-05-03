# Loop 98 Spec — Pos Başı Risk Yarıya + Short Emit Geri

Tarih: 2026-05-04 | Author: PM | Status: Boot ready

## Bağlam

Loop 97 t60 halt: realized -$1.998 (eşik -$1.50 AŞILDI). 3 ardışık SL hit 1dk içinde (XRP+BTC+ADA Long, pazar flash crash). Long-only mod kırılgan.

Pattern: Pazar bir yönde keskin hareket ettiğinde mod ne olursa olsun (Long+Short veya Long-only), pos başı risk büyükse catastrophic loss. **Çözüm: pos başı risk yarıya.**

Detay: `loops/loop_97/halt-t60.md`

## Tune (PM Doğrudan)

### Tune #1: RiskPerTradePct 0.02 → 0.01
- DB UPDATE: `UPDATE RiskProfiles SET RiskPerTradePct = 0.01`
- Etki: Pos qty yarıya, SL hit loss yarıya
- 3 ardışık SL: -$2 → -$1 (halt eşik -$1.50 AŞILMAZ)

### Tune #2: WeightOverrides Revoke (Short emit geri)
- DB UPDATE: `UPDATE Strategies SET ParametersJson = JSON_MODIFY(ParametersJson, '$.WeightOverrides', NULL) WHERE Type = 3`
- 5 strateji için
- Etki: Composer hard-coded weight (10 Long + 7 Short detector hepsi aktif)
- Pazar yönü: Long+Short balanced emit → hedge

### Tune #3: MaxOpenPositions 5 → 3 (geri)
- DB UPDATE: `UPDATE RiskProfiles SET MaxOpenPositions = 3`
- Etki: Risk concentration düşer
- 3 SL hit toplamı küçük

### Korunur (önceki tune'lar)
- TriggerPct 0.002 (BE arm — Loop 97 doğrulandı)
- TrailPct 0.003
- MTF threshold 0.002m (gevşek doğru yön)
- RequiredScore 2 (frekans için)
- OffsetPct 0.002

## Ek Workaround: Bot Boot Sonrası Manuel State Cleanup

PaperTrade reset endpoint bug:
- Force-close açık pozisyonlar → realized loss → CB tripped (Loop 97'de gözlemlendi)

Workaround: bot boot → CB reset API + papertrade reset API + manuel SQL UPDATE (RiskProfile counter=0, strategies activate). Loop 99 backlog: PaperTrade reset endpoint düzgün hale getir.

## Hipotez Test

Loop 98 hedef:
- Realized > -$0.50 (3 ardışık SL hit oluşsa bile pos başı küçük loss)
- Win rate %33+ (Loop 97 %25, Loop 96 %50, Loop 94 %50)
- Pozitif loop (18 loop'tan sonra ilk pozitif loop)

## Cumulative

18 loop -$21.5, 0 pozitif loop. Loop 98 = pos sizing test.
