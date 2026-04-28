# Loop 49 — Check t=60dk (2026-04-28 13:59 TR)

## Durum: 1h Sabit, 0 Sinyal

| Metrik | Boot | t60 | Δ |
|---|---|---|---|
| Cash / Equity | $500 / $500 | $500 / $500 | 0 |
| Realized | $0 | $0 | 0 |
| Open / Closed Pos | 0 / 0 | 0 / 0 | 0 |
| Signals | 0 | 0 | 0 |
| SignalSkipped | 0 | 265 | +265 (eval rate normal) |
| WsStateChanged | 4 | 46 | **+42 ⚠️** |

## Halt Kriter
| Kriter | Durum | Verdict |
|---|---|---|
| Realized < -$1.50 | $0 | ✓ buffer $1.50 |
| 5+ ardışık SL | 0 | ✓ |
| Zombie | 0 açık | ✓ |
| Signal akmıyor (>4h) | 1h, t240 karar penceresi | ⏳ |
| WS / CB | **46 state change ⚠️** | uyarı |
| API health | http://localhost:5188 ✓ | ✓ |

**HALT YOK** ama 2 uyarı:
1. 0 sinyal 1h — beklenmedik (BB MeanRev gevşek olmalıydı)
2. WsStateChanged 46 (önceki loop'larda 4) — WS instability sinyali olabilir

## Yorum
BB MeanRev 15m önce 1h içinde sinyal gelmiş olabilirdi (Loop 45'te 3.5h sonra ilk sinyal geldi). 1h erken — endişe değil.

WsStateChanged 46 endişeli: testnet WebSocket sürekli reconnect yaşıyorsa kline akışı kesintili → indikatör hesabı eksik → sinyal kaybedilebilir. Bu Loop 41-48'de görülmedi, yeni durum.

Olası sebepler:
- Testnet WS endpoint instability (geçici)
- DB reset sonrası SystemEvents'te eski ws state change kayıtları silinmiş, yeni kayıtlar başlangıç bağlantı pattern'i (bot her başlangıçta ~5-8 state change yapıyor olabilir)

## Karar
**Loop 49 DEVAM** (0 sinyal henüz halt değil).

t240 (16:57 TR) = 4h karar penceresi:
- Hala 0 sinyal → filtre daha gevşet (RsiOversoldThreshold 38→42, VolumeZScoreThreshold 0.5→0.3) → Loop 50

## Sıradaki Wakeup
**ScheduleWakeup 3600s → t=120dk (14:59 TR)**

— PM 2026-04-28 Loop 49 t=60
