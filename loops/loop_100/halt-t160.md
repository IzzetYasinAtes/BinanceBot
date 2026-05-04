# Loop 100 Halt — t160 R:R Asymmetri Devam (Realized -$1.26 Eşik Yakın)

Tarih: 2026-05-04 06:24 UTC | Loop 100 boot 03:15 UTC + Status fix 04:38 UTC | Süre: 3h09m total, ~1h36m fix sonrası

## Halt: Realized -$1.26 (Eşik -$1.50 Marj $0.24)

### Closed (3, %33 win rate)
| # | Symbol | Direction | Entry | Exit | RPnL |
|---|---|---|---|---|---|
| 1 | ADAUSDT | Long | $0.2528 | $0.2532 | **+$0.037** |
| 2 | BTCUSDT | Long | $80347 | $79945 | **-$0.626** SL hit |
| 3 | ADAUSDT | Long | $0.2535 | $0.2521 | **-$0.672** SL hit |

Net realized: -$1.261. Win rate %33 (1W/2L). Avg win $0.04 vs avg loss $0.65 → **R:R 1:16 (Loop 94+97 paterni devam)**.

### Open (2, ikisi de zarar)
| Symbol | Entry | Mark | UPnL | Peak | Hold |
|---|---|---|---|---|---|
| BTCUSDT | $80078 | $79792 | -$0.372 | $80062 (entry'nin **altında**) | 45min |
| ETHUSDT | $2374 | $2366 | -$0.313 | $2372 (entry'nin **altında**) | 10min |

**KRİTİK PATTERN**: Tüm açık + kapalı Long pozisyonlarda Peak entry'nin ALTINDA. BE arm asla olmuyor — pos açıldıktan sonra mark sürekli düşüyor. Bu pazar düşüş trendinde Long emit etmenin doğal sonucu.

## KÖK SEBEP: MTF Threshold Çok Gevşek

Loop 99'da MTF threshold 0.005 (=%0.5) yapmıştım (frekans için). AMA bu Long skip eşiğini gevşetti — bot downtrend pazarda BILE Long emit ediyor → Long açılır → mark düşmeye devam → SL hit.

Loop 91 değer: 0.001 (=%0.1) **strict**. Bu pazar slope -%0.1'den daha negatifse Long skip → downtrend Long emit'i öler.

## Loop 101 Tune (PM Doğrudan, Tek Satır)

`PatternCompositeEvaluator.cs:118` Edit yapıldı:
```diff
- var mtfThreshold = snapshot.Ema21_15m * 0.005m;  // %0.5 (Loop 99 gevşek)
+ var mtfThreshold = snapshot.Ema21_15m * 0.001m;  // %0.1 strict (Loop 91 değer)
```

Build 0 hata.

## Korunur (Loop 95-100 fix'leri)
- WeightOverrides 7 Short=0 (Long-only)
- TriggerPct 0.002 (BE arm — eşiğe ulaşırsa)
- TrailPct 0.003
- RPT 0.01 (pos sizing)
- MaxOpen 3
- RS=2
- AdxMultiplier 1.0, Cooldown 1
- **Status=3 fix korunur** (kritik!)

## Hipotez

Loop 101 hedef: MTF strict skip ile downtrend Long emit kapansın. Pos sadece uptrend (slope > -%0.1) açılır → Peak entry üstüne çıkma şansı artar → BE arm + trailing locked profit.

## Cumulative

20 loop -$22.8, 0 pozitif loop. Loop 101 = MTF strict + Status=3 + pos sizing kombo.

## Sonraki

Bot restart + reset + Loop 101 boot.md.
