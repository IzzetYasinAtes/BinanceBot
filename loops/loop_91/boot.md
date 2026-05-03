# Loop 91 Boot — MTF Gate GERİ Eklendi (Pazar Downtrend Kabulu) (2026-05-03 08:53 TR)

## Pivot Sebebi
Loop 90 t90: 3/3 yeni emit kötü başlangıç (sahte breakout). MTF kapatma deney **kanıtladı**: pazar downtrend ise long emit yanlış.

## Loop 91 Değişiklik
MTF gate Loop 88 davranışına geri ekle (slope < -%0.1 strict):
```csharp
var slope15m = snapshot.Ema21_15m - snapshot.Ema21Prev5_15m;
var mtfStrongDownThreshold = -snapshot.Ema21_15m * 0.001m;
if (Ema21_15m <= 0 || slope15m < mtfStrongDownThreshold) skip;
```

## Felsefi Karar: Memory #12 vs Pazar Gerçeği
**Memory #12**: "0 emit > 1h pivot zorunlu, sermaye koruma anti-pattern"

**Pazar gerçeği**: Bot long-only, pazar downtrend → emit veremez (doğal). Pivot = sahte breakout, daha çok loss.

**Yeni anlayış**: Memory #12 anti-sermaye-koruma için yazılmış (2 strateji denemeden vazgeç değil). Pazar koşulu vs anti-pattern ayrımı:
- Anti-pattern: Filtre çok katı (kontrol edilebilir)
- Pazar koşulu: Bot dışı sebep (pas geç)

Loop 91 strateji: **Pazar dönüşünü bekle**, MTF gate doğru çalışsın, bot pas geçsin. Short positions = uzun vadeli yapısal değişim (Loop 100+).

## Aktif Filtre Stack (Loop 91)
| Filtre | Durum |
|--------|-------|
| Composer hard-gate skip | OFF (Loop 89) |
| **MTF gate (slope < -%0.1)** | **ON (Loop 91 GERİ)** ✓ |
| RSI cap (RSI > 85) | ON |
| RequiredScore 3 | ON |
| BE.OffsetPct 0.0020 | ON |
| Trail.TrailPct 0.0050 | ON |

## Boot State
| Metrik | Değer |
|---|---|
| Bot PID | **5028** |
| MTF gate | **ON yumuşak** (-%0.1 strict) |
| CB | Healthy |
| Açık | 3 (Loop 90 carryover SOL/BTC/ADA, hepsi negatif) |
| Realized | $0 (L90) |

## Loop 90 Carryover
- SOL Hold 42min UPnL -$0.115
- BTC Hold 42min UPnL -$0.059
- ADA Hold 3min UPnL -$0.030
- UPnL toplam -$0.20

Bu 3 pozisyon mevcut SL/TP/Trail ile kapanır (Loop 90 paramı). Yeni emit'ler Loop 91 paramı.

## L80→L91 Stack
| Loop | MTF | Hard-gate | Sonuç |
|------|-----|-----------|-------|
| L84 | - | OFF | sahte breakout |
| L85 | - | OFF | 3 SL CB tripped |
| L86 | - | ON | 0 emit (sonra 2 sahte) |
| L87 | ON | ON | 0 emit (1.5h) |
| L88 | yumuşak | ON | 0 emit (1h) |
| L89 | yumuşak | OFF | 0 emit (downtrend) |
| L90 | OFF | OFF | 3 sahte emit |
| **L91** | **yumuşak** | **OFF** | **TEST** (pazar dönmesini bekle) |

## L91 KPI
- Pazar dönerse emit gel + BE-stop pozitif
- Pazar dönmezse 0 emit (sermaye koru)
- Realized hedef: ≥$0 (carryover SL'leri tolere)

## Halt Eşikleri
- Realized < -$2.00 → daha derin pivot (genişletilmiş eşik, 3 carryover SL ihtimali)
- 4+ ardışık SL → spec yanlış

## Sıradaki Wakeup
**ScheduleWakeup 1500s → t=30dk (09:18 TR)**

— PM 2026-05-03 Loop 91 boot (MTF geri ekleme, pazar downtrend kabul, sermaye koru)
