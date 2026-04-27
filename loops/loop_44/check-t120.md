# Loop 44 — Check t=120dk (2026-04-28 01:46 TR)

## Durum: 2h Sabit, 0 Trade, BB Mean Rev Çok Sıkı

| Metrik | t60 | t120 | Δ |
|---|---|---|---|
| Cash | $500 | $500 | 0 |
| Equity | $500 | $500 | 0 |
| Realized | $0 | $0 | 0 |
| Open Pos | 0 | 0 | 0 |
| Closed Pos | 0 | 0 | 0 |
| Orders | 0 | 0 | 0 |
| Signals | 0 | 0 | 0 |
| Fills | 0 | 0 | 0 |
| SignalSkipped (toplam) | 345 | 655 | +310 |
| SignalSkipped (son 60dk) | — | 300 | normal evaluator tick |

## Halt Kriter
| Kriter | Durum | Verdict |
|---|---|---|
| Realized < -$1.50 | $0 | ✓ buffer $1.50 |
| 5+ ardışık SL | 0 | ✓ |
| Zombie | 0 açık | ✓ |
| Signal akmıyor (>4h) | 2h, henüz erken | ⏳ izle |
| WS / CB | 4 state change normal | ✓ |
| API health | http://localhost:5188 ✓ | ✓ |

**HALT YOK.**

## 4h Karar Penceresi
binance-expert beklentisi: 3-5 sinyal/gün ≈ 8h'da 1 sinyal. 2h'da 0 sinyal **beklenti dahilinde** (negatif sinyal değil).

**4h kuralı (t240, 03:46 TR):** Hala 0 sinyalse filtre gevşetme + Loop 45 boot.
- RsiOversoldThreshold: 30 → 35 (oversold tanımı genişler, daha fazla bar match)
- VolumeZScoreThreshold: 1.0 → 0.8 (panik teyidi gevşer)
- BbStdMultiplier: 2.0 → 1.8 (BB band daralır, lower band daha sık dokunulur)

Bu üçü kombine: 0.9-1.4× sinyal frekansı artışı beklenir (kalite/frekans trade-off).

## Sıradaki Wakeup
**ScheduleWakeup 3600s → t=180dk (02:46 TR)**

Eğer t180'de hala 0 sinyal → t240 karar penceresi yaklaşıyor uyarısı. t240'da 0 sinyal kalırsa otomatik Loop 45 boot.

— PM 2026-04-28 Loop 44 t=120
