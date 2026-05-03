# Loop 96 Spec — MTF Threshold Yön Düzeltmesi (Loop 95 Yanlış Yön)

Tarih: 2026-05-03 | Author: PM | Status: Boot ready

## Bağlam (kısa)

Loop 95 t60 halt: frekans 3/h düştü (Loop 94'te 44/h). Sebep: MTF threshold tune'unu YANLIŞ YÖN uyguladık (0.001 → 0.0005 sıkıştırdı, gevşetmedi).

Detay: `loops/loop_95/halt-t60.md`

## Fix (Tek Satır)

PM doğrudan Edit ile düzeltildi (commit'lenecek):
```csharp
// PatternCompositeEvaluator.cs:118
var mtfThreshold = snapshot.Ema21_15m * 0.002m;  // %0.2 kademe (gevşek, doğru yön)
```

## Ek Tune (Yok — Loop 95 diğer tune'ları korunur)

- WeightOverrides 7 Short detector = 0 ✓ (Long-only emit)
- TrailPct 0.003 ✓ (winning trade pencere)
- TriggerPct 0.003 ✓ (BE geç arm)
- RequiredScore 3 ✓ (DB seed)

## Done-Definition

- 1 commit (development branch)
- dotnet build 0 hata 0 uyarı (✓)
- dotnet test 0 fail (test bu satıra dokunmuyor)
- Bot boot t30: frekans hedef 30+/h restore

## Hipotez Test

Loop 96 hipotezi: Loop 95 fix'leri korunur (Long-only + R:R) + MTF doğru yön gevşetme = ilk kez:
- Frekans 30+/h (kartopu için)
- Long-only emit (Short bias zarar yok)
- TrailPct 0.003 winning trade pencere → avg win büyük olur

Beklenti: Loop 94 → Loop 95 → Loop 96:
- Loop 94: -$1.16 realized (Short bias toxic)
- Loop 95: -$0 realized (frekans yok)
- Loop 96: hedef pozitif veya ≥ -$0.30 realized
