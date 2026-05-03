# Loop 97 Spec — TriggerPct Geri %0.20 + RS 2 (Frekans)

Tarih: 2026-05-04 | Author: PM | Status: Boot ready

## Bağlam

Loop 96 t90 halt: realized -$1.30 (eşik marj $0.20). 2 Long SL hit (-$0.62 + -$0.68). 

**Kök sebep**: BE eşiği TriggerPct %0.30 (Loop 95 tune'um) ULAŞILAMADI. BTC peak +%0.28 ile BE NEVER ARMED → trailing locked profit yoktu → kar kayboldu.

Detay: `loops/loop_96/halt-t90.md`

## Tune (PM Doğrudan, Kod YOK)

### Tune #1: TriggerPct 0.003 → 0.002 (Loop 91 değeri)
- `appsettings.json:56` `"TriggerPct": 0.0020` ✓ (Edit yapıldı)
- BE arm olabilir → trailing locked profit aktif

### Tune #2: RequiredScore 3 → 2 (frekans artır)
- DB UPDATE: `Strategies.ParametersJson.RequiredScore = 2` (5 strateji) ✓ yapıldı
- Loop 95-96 frekans 3-10/h → Loop 97 hedef 20+/h

### Korunur (Loop 95+96 fix'leri)
- WeightOverrides 7 Short detector = 0 (Long-only emit) ✓
- TrailPct 0.003 (winning trade pencere) ✓
- MTF threshold 0.002m (gevşek doğru yön) ✓
- MaxOpenPositions 5 ✓

## Hipotez Test

Loop 91 TriggerPct=0.002 + Loop 96 BTC peak +%0.28 verisi:
- BTC Long Peak entry+0.28%
- TriggerPct 0.002 = +%0.20 → BTC peak %0.28'de BE ARM ✓
- BE arm sonrası SL = entry × 1.002 (OffsetPct=0.002)
- TrailPct 0.003: peak'ten %0.3 düşüş → exit
- BTC Loop 96'da peak $79013 → trailing exit $79013 × 0.997 = $78776 → +$0.05 lock
- Loop 96'da BTC mark $78836'a düştü → trailing tetiklenecek $78776'ya gelmeden önce kar kalır

Beklenti: BTC kar realize edilir (+$0.05 ile +$0.20 arası), Loop 91'in BE-stop matematik'i restore.

## Sonraki

Bot restart + DB reset + Loop 97 boot.md + ScheduleWakeup t30.
