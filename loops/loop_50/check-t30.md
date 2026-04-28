# Loop 50 — Check t=30dk (2026-04-29 00:15 TR)

## Durum: 0 Sinyal (15m BB Warmup Beklenen)

| Metrik | Boot | t30 |
|---|---|---|
| Cash / Equity | $500 / $500 | $500 / $500 |
| Realized | $0 | $0 |
| Open / Closed Pos | 0 / 0 | 0 / 0 |
| Signals | 0 | 0 |
| SignalSkipped | 0 | 160 (eval rate normal) |
| WsStateChanged | 4 | 4 (stabil ✓) |

## Halt Kriter
| Kriter | Durum | Verdict |
|---|---|---|
| Realized < -$1.50 | $0 | ✓ buffer $1.50 |
| 5+ ardışık SL | 0 | ✓ |
| Zombie | 0 açık | ✓ |
| WS / CB | 4 stabil | ✓ |
| API health | http://localhost:5188 ✓ | ✓ |

**HALT YOK.**

## Yorum
HybridMomentum1m warmup beklentileri:
- 1m EMA9/21: 22dk warmup (✓ tamam)
- 1m ATR14: 15dk warmup (✓ tamam)
- 15m BB(20): **300dk = 5h warmup gerekiyor** (DB Klines'ten backfill yapılıyor olmalı)
- 15m RSI14: 30dk warmup (15m × 2 bar minimum)

30dk'da hibrit AND koşullarının hepsi sağlanması zor — 15m BB lower kapısı ana darboğaz. WarmupCompleted=12 görünüyor ama bu strateji-strateji başlangıçta tetiklendi, gerçek bar count'unu garanti etmiyor.

DB Klines tablosu korundu (boot script ile) → 15m bar geçmişi dolu olmalı, warmup hızlı tamamlanmalı.

## Karar
**Loop 50 DEVAM** (0 sinyal beklenen, halt eşiği uzak).

## Sıradaki Wakeup
**ScheduleWakeup 1800s → t=60dk (00:45 TR)**

t60'ta hala 0 sinyalse 15m BB filtre çok sıkı kanıtı (paper rejim BB 2σ band'ı asla kırmıyor) — gerekirse parametre revizyon Loop 51.

— PM 2026-04-29 Loop 50 t=30
