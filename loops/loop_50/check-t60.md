# Loop 50 — Check t=60dk (2026-04-29 00:46 TR)

## Durum: 1h Sabit, 0 Sinyal

| Metrik | t30 | t60 | Δ |
|---|---|---|---|
| Cash / Equity | $500 / $500 | $500 / $500 | 0 |
| Realized | $0 | $0 | 0 |
| Open / Closed Pos | 0 / 0 | 0 / 0 | 0 |
| Signals | 0 | 0 | 0 |
| SignalSkipped | 160 | 315 | +155 (eval normal) |
| WsStateChanged | 4 | 4 | 0 stabil ✓ |

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
1h içinde 0 sinyal. HybridMomentum1m AND koşullarının birlikte sağlanması zor:
- 15m BB lower kırılım + 15m RSI<40 dönüş + 1m EMA9>EMA21 + 1m vol×1.2 + 1m ATR aktif

5 koşulun **eş zamanlı** sağlanma olasılığı düşük. Filtre çok sıkı olabilir.

## Karar
**Loop 50 DEVAM** (uyarı), t120'de hala 0 sinyalse Loop 51 boot (filtre gevşetme).

## Sıradaki Wakeup
**ScheduleWakeup 1800s → t=90dk (01:16 TR)**

t90'da:
- 0 sinyal devam → t120 karar penceresine 30dk
- 1+ sinyal → strateji çalıştığını doğrular, normal cycle

— PM 2026-04-29 Loop 50 t=60
