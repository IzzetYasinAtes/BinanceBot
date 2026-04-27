# Loop 44 — Check t=60dk (2026-04-28 00:44 TR)

## Durum: API Healthy, Hiç Trade Yok (BB Mean Rev nadir koşul)

| Metrik | Değer |
|---|---|
| API | http://localhost:5188 ✓ |
| Mode | Paper |
| StartingBalance | $500.0000 |
| CurrentCash | $500.0000 |
| Equity | $500.0000 |
| Realized | $0 |
| Net | $0 |
| Open Pos | 0 |
| Closed Pos | 0 |
| Orders | 0 |
| Signals | 0 |
| Fills | 0 |

## DB Sayım — SystemEvents
| Event | Count |
|---|---|
| SignalSkipped | 345 (1h 8dk içinde) |
| WarmupCompleted | 12 |
| StrategyActivated | 5 (BB Mean Rev) |
| WsStateChanged | 4 (normal başlangıç) |

- FirstEvent: 04/27 20:35:58 UTC (boot)
- LastEvent: 04/27 21:44:01 UTC (live)
- Süre: 1h 8dk
- 5 strateji × 4 bar/h × 1h = 20 beklenen evaluation, ama 345 skip = evaluator her tick çağrılıyor (sıkıntı değil, normal)

## Halt Kriter
| Kriter | Durum | Verdict |
|---|---|---|
| Realized < -$1.50 | $0 | ✓ buffer $1.50 |
| 5+ ardışık SL | 0 | ✓ |
| Zombie | 0 açık | ✓ |
| Signal akmıyor (>4h) | 1h sıfır, henüz erken | ⏳ izle |
| WS / CB | 4 state change (normal) | ✓ |

**HALT YOK.**

## Yorum
BB Mean Reversion giriş koşulu (close<bbLower AND rsi14<30 AND volZ>1.0 AND atrPct>=0.0007) **çok sıkı bir kombinasyon** — sağlıklı piyasada nadir tetiklenir. 1 saatte 0 sinyal beklenmiş bir sonuç değil:
- Loop 43 Donchian: 6.5h'da 2 sinyal
- Loop 44 BB Mean Rev: ilk 1h'de 0 sinyal — normal

**Erken pivot kararı verilmeyecek.** binance-expert beklenti tablosu: kötü %35 WR, 3 sinyal/gün → 24h'da 3 sinyal yeterli verisel. Şu an 1h içindeyiz, 4-6h'a kadar 0 sinyal kalırsa filtre gevşetme (RSI threshold 30→35, volZ 1.0→0.8) düşünülecek.

## Sıradaki Wakeup
**ScheduleWakeup 3600s → t=120dk (01:44 TR)**

— PM 2026-04-28 Loop 44 t=60
