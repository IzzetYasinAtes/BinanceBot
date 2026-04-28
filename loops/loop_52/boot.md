# Loop 52 Boot — HybridMomentum1m Daha Agresif Gevşetme (2026-04-29 01:23 TR)

## Pivot Sebebi
Loop 51 t30: 0 SignalEmitted, 160 SignalSkipped (gevşek filtre yetmedi).

## Loop 51 → Loop 52 Daha Agresif Gevşetme

| Parametre | Loop 50 | Loop 51 | **Loop 52** |
|---|---|---|---|
| `BbStdMultiplier15m` | 2.0 | 1.5 | **1.3** (band çok dar) |
| `RsiOversoldThreshold15m` | 40 | 50 | **55** (orta-üst RSI bile kabul) |
| `VolumeMultiplier1m` | 1.2 | 0.8 | **0.6** (vol çok gevşek) |
| `MinAtrPct1m` | 0.0003 | 0.0002 | **0.0001** (neredeyse sınırsız) |
| `CooldownBarsAfterSignal` | 3 | 3 | **2** (60s cooldown) |

Bu parametreler hibrit AND koşullarının her birini neredeyse devre dışı bırakacak seviye. Eğer Loop 52'de de 0 emit kalırsa **strateji konsept hatası** (HybridMomentum1m yapısal başarısız) → Loop 53 binance-expert ile farklı strateji.

## 5 Aktif Coin
BTC, ETH, SOL, XRP, ADA. MaxOpenPositions=5.

## Beklenti
- Frekans: 0/h → **5-20/h** (4 filtre + cooldown gevşek)
- WR: %25-40 (kalite ciddi düşer)
- Karar testi: bu parametrelerle 0 emit ise konsept yanlış

## Boot State
| Metrik | Değer |
|---|---|
| Mode | Paper |
| StartingBalance | $500 |
| Equity | $500 |
| Active | 5 (HybridMomentum1m agresif gevşek) |
| API Port | 5188 |

## Halt Eşikleri
- Realized < -$1.50 → Loop 53 binance-expert
- 5+ ardışık SL → Loop 53
- SignalEmitted = 0 (30dk içinde) → Loop 53 binance-expert (HybridMomentum1m mimari hata)
- WR < %25 (10+ trade) → Loop 53

## Sıradaki Wakeup
**ScheduleWakeup 1800s → t=30dk (01:53 TR)**

— PM 2026-04-29 Loop 52 boot
