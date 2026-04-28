# Loop 45 — Check t=60dk (2026-04-28 04:53 TR)

## Durum: Filtre Gevşetildi, Yine 0 Sinyal (1h)

| Metrik | Boot | t60 | Δ |
|---|---|---|---|
| Cash | $500 | $500 | 0 |
| Equity | $500 | $500 | 0 |
| Realized | $0 | $0 | 0 |
| Open Pos | 0 | 0 | 0 |
| Closed Pos | 0 | 0 | 0 |
| Orders | 0 | 0 | 0 |
| Signals | 0 | 0 | 0 |
| Fills | 0 | 0 | 0 |
| SignalSkipped | 0 | 310 | +310 (eval rate normal) |
| SignalSkipped (60dk son) | — | 300 | tutarlı |

## Halt Kriter
| Kriter | Durum | Verdict |
|---|---|---|
| Realized < -$1.50 | $0 | ✓ buffer $1.50 |
| 5+ ardışık SL | 0 | ✓ |
| Zombie | 0 açık | ✓ |
| Signal akmıyor (>4h) | 1h, henüz erken | ⏳ |
| WS / CB | 4 state change normal | ✓ |
| API health | http://localhost:5188 ✓ | ✓ |

**HALT YOK** ama beklenenin altında performans (0/h vs beklenen 0.5-1/h).

## Yorum
Filtre gevşetilmesine rağmen 1h'de 0 sinyal. Olası sebepler:
1. **Asia gece dilimi (UTC 00-04 = 03-07 TR)**: 5 büyük coin'in hepsi konsolidasyon, oversold koşulu hiç birinde tetiklenmiyor
2. **5 coin yetersiz**: BTC/ETH/XRP/SOL/ADA blue-chip'ler düşük volatilite — DOGE/AVAX/LINK gibi mid-cap'lerde oversold daha sık olur
3. **Filtreler hala sıkı**: BBstd 1.8 hala restriktif (gerçek mean reversion bot'larında 1.5 kullanılır)

## Karar Penceresi
- **t120 (05:53 TR):** hala 0 sinyal kalırsa hipotez 2 (coin sayısı artırma) düşünülecek
- **t240 (07:51 TR):** 4h karar penceresi — 0 sinyal → Loop 46 daha radikal pivot

Erken pivot yapmıyorum çünkü filtre gevşetmesi henüz 1h'de ölçülemez. binance-expert beklentisi sıkı koşulda 8h'da 1 sinyal idi; gevşetme ile yarıya inmeli ama hala saatlik sıklık değil.

## Sıradaki Wakeup
**ScheduleWakeup 3600s → t=120dk (05:53 TR)**

— PM 2026-04-28 Loop 45 t=60
