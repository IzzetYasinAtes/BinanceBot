# Loop 21 Boot — 2026-04-19 14:50 UTC

## Başlangıç
- API: `http://localhost:5188` çalışıyor
- DB: Temiz reset, $100 başlangıç
- 3 VwapEmaHybrid strateji aktif (BTC/BNB/XRP-VwapEma-Scalper)
- SystemEvents tablosu aktif, publisher çalışıyor

## Reform özeti (ADR-0016)

| Parametre | Değer |
|---|---|
| directionGate | `nowEma >= prevEma * 0.9995` (slope ≥−%0.05) |
| vwapReclaim | `last > VWAP` OR zone ±%0.15 |
| volumeConfirm | SMA20 × 1.05 |
| TP | entry × 1.007 (sabit %0.7 gross / %0.5 net) |
| SL BTC/BNB | %0.3 |
| SL XRP | %0.4 |
| MaxHoldMinutes | 12 |

Beklenti: 4-8 sinyal/saat, EV +%0.18/trade (WR=%58), günlük +%3-4.

## UI polish aktif
- Hero 30px (index ana panel)
- AnimatedNumber counter hero'da
- PriceTicker marquee üstte
- RingProgress risk.html'de
- Neon glow hover, favicon.svg

## Sistem Olayları (reformun büyük kazanımı)
- 50+ event anında yazıldı (Startup, WsStateChanged, WarmupCompleted, StrategyActivated)
- logs.html artık dolu
- Throttling SignalSkipped per-(strategy,minute) max 1

## Health check programı
- t30 / t90 / t150 / t210 (60dk aralık)
- t240 final

## Red flag
- CB Tripped → halt
- WS 5+ dk disconnect → halt
- 3 ardışık zarar → PAUSE
- 30+ dk 0 sinyal + tuned koşullar hala false → strateji yeniden değerlendir
