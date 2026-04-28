# Loop 53 — Check t=30dk (2026-04-29 02:31 TR)

## Durum: 0 SignalEmitted, 160 SignalSkipped

| Metrik | Boot | t30 |
|---|---|---|
| Cash / Equity | $500 / $500 | $500 / $500 |
| Realized | $0 | $0 |
| Open / Closed Pos | 0 / 0 | 0 / 0 |
| **SignalEmitted** | 0 | **0** |
| SignalSkipped | 0 | 160 (5/dk eval rate) |

## binance-expert Teşhis (acil çağrıldı)

**Karar: BEKLE — t60 ve t120 izle, hemen pivot YOK.**

Gerekçe:
- Warmup tamam — REST 80 bar backfill anında doluyor, BB(20) için 20 bar yeterli
- 160 SignalSkipped = `TryGetBbMeanReversionSnapshot` null değil, evaluator çalışıyor, filtre koşulları yetersiz
- **Loop 49'da ilk sinyal 2h sonra geldi** — 30dk erken karar
- Asıl sebep: piyasa rejimi (sıkışmış band, BB lower'a temas yok)

Backend-dev debug gereksiz (snapshot null olsaydı farklı log basardı).

## Halt Kriter
| Kriter | Durum | Verdict |
|---|---|---|
| Realized < -$2.00 | $0 | ✓ |
| 4+ ardışık SL | 0 | ✓ |
| WR < %25 (5+ trade) | 0 trade | ⏳ |
| 0 emit (t120 eşiği) | 30dk, 90dk daha bekle | ⏳ |

**HALT YOK.**

## Karar Akışı

- **t60 (30dk sonra):** SignalEmitted hala 0 ise **devam izle**, Loop 49 deneyimi 2h
- **t120 (1.5h sonra):** Hala 0 ise → **Loop 54 boot**: parametre maksimuma gevşet (BBstd 1.5, RSI 55, volZ 0.0 = volume filtresi off)
- **t180 (2.5h sonra):** Loop 54 de 0 emit → BTC-only 5m timeframe

## Sıradaki Wakeup
**ScheduleWakeup 1800s → t=60dk (03:01 TR)**

— PM 2026-04-29 Loop 53 t=30
