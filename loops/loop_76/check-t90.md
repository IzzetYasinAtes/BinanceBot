# Loop 76 — Check t=90dk (2026-05-01 17:14 TR) — ✓ TRAILING ÇALIŞIYOR + ❌ Entry Kalitesi

## Sonuç: Trailing Module ÇALIŞIYOR ✓ — AMA Entry Kalitesi Hala Problem

RsiCeiling 70 ile 2 emit geldi (önceki 0). **TRAILING peak-up log GÖRÜLDÜ** (Loop 76 deploy başarılı). AMA ilk emit ADA -$0.61 büyük loss (BE öncesi).

## ✓ TRAILING LOG GÖRÜLDÜ
```
[17:13:47 INF] TRAILING peak-up pos=10518 symbol=SOLUSDT 
  prevPeak=0.00000000 newPeak=84.6550000000 trailPct=0.0015
```

→ MarkToMarketWorker.TryApplyTrailingStop hook çalışıyor, peak tracking aktif. SOL UPnl +$0.125 (BE trigger %0.10 yakın), trailing exit potansiyeli var.

## Sayım (90dk)
| Metrik | Değer |
|---|---|
| **SignalEmitted** | **2** ✓ (RsiCeiling 70 ile) |
| SignalSkipped | 93 |
| OrderPlaced | 3 |
| OrderFilled | 3 |
| PositionOpened | 2 |
| **PositionClosed** | **1** |
| RiskAlert | 0 |
| **Realized PnL** | **-$0.61** ⚠️ |

## Trade Sonuçları
| # | Symbol | Hold | PnL | Tip |
|---|---|---|---|---|
| 1 | ADAUSDT 10517 | ~30min | **-$0.61** | büyük loss (BE'ye varmadı) |

## Açık Pozisyon
| Symbol | Hold | Entry | Mark | UPnl | %UPnl | Trailing? |
|---|---|---|---|---|---|---|
| **SOLUSDT 10518** | 9min | $84.55 | $84.66 | **+$0.125** | **+%0.13** | Peak update ✓ (BE yakın) |

## Pattern Tekrarı: BE Öncesi Büyük Loss
ADA emit → fiyat hızla düştü → SL hit → -$0.61 (BE move tetiklenmedi).

Bu binance-expert'in Loop 77 önerisinin gerekçesi: **EMA200 trend gate** + **BBW regime filter** entry kalitesini artıracak (RSI Zone yetmiyor, trend ve volatilite filtreleri gerekli).

## Karar
| Şart | Aksiyon |
|---|---|
| Realized -$0.61 < -$0.50 | **Loop 77 EMA200+BBW deploy KESIN** |
| Trailing module aktif ✓ | Loop 76 deploy başarılı |
| SOL pozitif UPnl | Trailing exit izle (kar potansiyeli) |

## Loop 77 Plan (background backend-dev)
1. **EMA200 indicator**: 200 bar warmup (zaten var, eşik yükseltilmeli)
2. **EMA200 trend gate**: KMS evaluator hard-gate (closePrice > EMA200 long zorunlu)
3. **BBW snapshot**: KmsMomentumSnapshot'a eklenecek
4. **BBW regime filter**: nice-to-have (skor 1pt) veya hard-gate
5. KMS Parameters: `Ema200GateEnabled bool` (toggle, 0-emit sigortası)

## t120 Beklenti (17:39 TR)
- SOL trailing exit veya TP hit (kar)
- Loop 77 backend-dev background iş hala sürebilir
- Yeni emit gelir mi (RsiCeiling 70 stable)

## Halt Eşikleri
- Realized < -$1.50 → Loop 77 hızlandır
- Circuit breaker → API reset
- 5+ ardışık SL → halt

## Sıradaki Wakeup
**ScheduleWakeup 1500s → t=120dk (17:38 TR)**

— PM 2026-05-01 Loop 76 check-t90 (trailing ✓ entry kalitesi ❌ → Loop 77 EMA200/BBW)
