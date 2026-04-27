# Loop 44 — Check t=180dk (2026-04-28 02:48 TR)

## Durum: 3h Sabit, 0 Trade, 4h Karar Penceresine 1h Kaldı

| Metrik | t60 | t120 | t180 | Δ (t120→t180) |
|---|---|---|---|---|
| Cash | $500 | $500 | $500 | 0 |
| Equity | $500 | $500 | $500 | 0 |
| Realized | $0 | $0 | $0 | 0 |
| Open Pos | 0 | 0 | 0 | 0 |
| Closed Pos | 0 | 0 | 0 | 0 |
| Orders | 0 | 0 | 0 | 0 |
| Signals | 0 | 0 | 0 | 0 |
| Fills | 0 | 0 | 0 | 0 |
| SignalSkipped (toplam) | 345 | 655 | 965 | +310 |
| SignalSkipped (son 60dk) | — | 300 | 300 | tutarlı eval rate |

## Halt Kriter
| Kriter | Durum | Verdict |
|---|---|---|
| Realized < -$1.50 | $0 | ✓ buffer $1.50 |
| 5+ ardışık SL | 0 | ✓ |
| Zombie | 0 açık | ✓ |
| Signal akmıyor (>4h) | 3h, 1h kaldı | ⏳ |
| WS / CB | 4 state change normal | ✓ |
| API health | http://localhost:5188 ✓ | ✓ |

**HALT YOK** — ama 0 trade durumu sürerse t240'da otomatik aksiyon.

## Yorum
Asya gece dilimi (UTC 22-00) crypto'da düşük volatilite penceresi → oversold koşul (close<bbLower AND rsi14<30 AND volZ>1.0) zor tetikleniyor. Bu beklenen ama **0 öğrenme = atıl loop**. Kullanıcı disiplini: 24h bozuk çalıştırma yasak.

t240'da hala 0 sinyal kalırsa Loop 45 boot:
- RsiOversoldThreshold: 30 → 35
- VolumeZScoreThreshold: 1.0 → 0.8
- BbStdMultiplier: 2.0 → 1.8
- (parametre değişikliği — yeni kod gerekmez, sadece appsettings.json)

## Sıradaki Wakeup
**ScheduleWakeup 3600s → t=240dk (03:48 TR — 4h KARAR PENCERESİ)**

t240'da:
- 0 sinyal devam → backend-dev'siz appsettings tweak + Loop 45 boot
- ≥1 sinyal/trade geldi → Loop 44 devam, t300 wakeup

— PM 2026-04-28 Loop 44 t=180
