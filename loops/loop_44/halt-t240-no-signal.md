# Loop 44 — Halt @ t=240dk (2026-04-28 03:49 TR) — NO-SIGNAL

## Durum: 4h Boyunca 0 Sinyal — Pivot Zorunlu

| Metrik | t60 | t120 | t180 | t240 | Δ Toplam |
|---|---|---|---|---|---|
| Cash / Equity | $500 | $500 | $500 | $500 | sabit |
| Realized | $0 | $0 | $0 | $0 | 0 |
| Open Pos | 0 | 0 | 0 | 0 | 0 |
| Closed Pos | 0 | 0 | 0 | 0 | 0 |
| Orders | 0 | 0 | 0 | 0 | 0 |
| Signals | 0 | 0 | 0 | 0 | 0 |
| Fills | 0 | 0 | 0 | 0 | 0 |
| SignalSkipped (toplam) | 345 | 655 | 965 | 1270 | +925 |
| SignalSkipped (60dk son) | — | 300 | 300 | 300 | tutarlı eval |

## Halt Sebebi
**4h penceresinde 0 öğrenme = atıl loop.** Kullanıcı disiplini: 24h bozuk loop yasak.

BB Mean Reversion 15m giriş koşulu (`close<bbLower AND rsi14<30 AND volZ>1.0`) BTC/ETH/XRP/SOL/ADA gibi yüksek hacimli coin'lerde **çok sıkı kombinasyon**. Asia gece dilimi düşük volatilite + sağlıklı piyasa rejimi → oversold zor tetikleniyor.

## Loop 41-44 Aggregate
| Loop | Strateji | Trade | Realized | Sebep |
|---|---|---|---|---|
| 41 | Donchian BO (no cooldown) | 8 | -$1.80 | LTC whipsaw |
| 42 | + cooldown | 2 | -$0.73 | XRP+SOL eş-SL |
| 43 | + filtre gevşetme | 1 | -$0.45 | ADA SL, DOGE stale |
| **44** | **BB Mean Rev sıkı** | **0** | **$0** | **0 sinyal 4h** |

## Loop 45 Pivot — Aynı Strateji, Filtre Gevşeme

**Değişen parametreler (kod yok, sadece appsettings.json):**
- `RsiOversoldThreshold`: 30 → **35** (oversold tanımı genişler)
- `VolumeZScoreThreshold`: 1.0 → **0.8** (panik teyidi gevşer)
- `BbStdMultiplier`: 2.0 → **1.8** (BB band daralır → lower band daha sık dokunulur)

**Beklenen etki:** 0 → 0.5-1 sinyal/saat (3 filtre kombine ~%40-80 tetikleme artışı). Kalite muhtemelen düşer ama ölçüm imkanı doğar.

Loop 45'te de 4h boyunca 0 sinyal kalırsa → daha radikal pivot (ör. 5m timeframe veya farklı evaluator).

## Sıradaki: Loop 45 Boot
1. appsettings.json patch (5 BB strategy)
2. dotnet kill + DB reset + VirtualBalance reseed
3. API restart
4. Loop 45 boot rapor
5. ScheduleWakeup t60

— PM 2026-04-28 Loop 44 halt @ t=240
