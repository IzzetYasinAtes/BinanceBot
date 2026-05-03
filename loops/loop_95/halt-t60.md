# Loop 95 Halt — t60 SPEC YÖN HATASI

Tarih: 2026-05-03 20:16 UTC | Boot: 19:12 UTC | Süre: 64dk

## Halt Sebebi: Frekans Donması (3 emit/h) — MTF Threshold Yön Hatası

Loop 95 boot.md'deki tune'lardan #4 (MTF threshold gevşetme) MATEMATIKSEL TERS uygulanmıştı:

**Threshold mekaniği** (`PatternCompositeEvaluator.cs:118`):
```
var mtfThreshold = snapshot.Ema21_15m * X;
if (Long && slope15m < -mtfThreshold) skip;  // slope çok negatif (downtrend)
if (Short && slope15m > +mtfThreshold) skip;  // slope çok pozitif (uptrend)
```

- **Threshold küçük** (0.0005, %0.05) → küçük slope bile skip → çok hassas → frekans DÜŞER
- **Threshold büyük** (0.002, %0.2) → sadece güçlü slope skip → tolere edici → frekans ARTAR

Loop 95 spec'inde "0.001 → 0.0005 (gevşek)" yazdım — TAM TERSİ. Doğru gevşeme: 0.001 → 0.002.

Sonuç: Loop 94'te 22 emit/30dk (44/h) → Loop 95'te 3 emit/60dk (3/h). %93 frekans kaybı.

## DB Snapshot

### Open (2 Long)
| Symbol | Entry | Mark | UPnL | Peak | Hold |
|---|---|---|---|---|---|
| BTCUSDT | $78792 | $78729 | -$0.082 | $78752 | 42min |
| ADAUSDT | $0.2510 | $0.2504 | -$0.230 | $0.25115 | 37min |

ADA t30'da +$0.05 idi, t60'ta -$0.23 (pazar dönüştü). BTC az değişmedi.

### Frekans (Loop 95 boot sonrası)
- 3 emit toplam, 60dk → 3/h
- Loop 94 t60: 26 emit (24/h cumulative)

### VirtualBalance
- Wallet $499.90 ✓
- AllocatedMargin ~$200
- realizedPnl: $0
- netPnl: -$0.42 (-0.08%)

## Loop 95 Kazanımları (yine de var)

- ✅ Long-only emit doğrulandı (Short=0 ✓)
- ✅ WeightOverrides migration çalıştı
- ✅ Wallet semantik korundu
- ⚠ Frekans bug (yön hatası bende, spec hatası)

## Loop 96 Fix (Tek Satır Kod)

PM doğrudan Edit yaptı (`PatternCompositeEvaluator.cs:118`):
```diff
- var mtfThreshold = snapshot.Ema21_15m * 0.0005m;  // sıkı (yanlış yön)
+ var mtfThreshold = snapshot.Ema21_15m * 0.002m;   // gevşek (doğru yön)
```

Beklenti: Frekans Loop 94 seviyesine geri döner (~30+/h), Long-only emit korunur.

## RequiredScore Notu

PM brief'inde "RequiredScore: 3" yazıyordu. PM frekans donmasının olası sebebi olarak RS=5 hipotezi yapmıştı, AMA DB'de zaten 3 imiş (Loop 92 commit 13'teki appsettings 5 default ama Strategies seed 3 olarak ayarlanmış). DB UPDATE no-op döndü (zaten 3'tü). Bu hipotez yanlıştı, asıl sebep MTF threshold yön hatası.

## Carryover

- 2 açık pos (BTC -$0.08, ADA -$0.23)
- 0 close
- Loop 96 boot: bot restart + reset + MTF fix yansıması
