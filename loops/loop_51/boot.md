# Loop 51 Boot — HybridMomentum1m Filtre Gevşetme (2026-04-29 00:50 TR)

## Pivot Sebebi
Kullanıcı feedback: "çok sinyal var ama alım yok" → SignalSkipped 315 (eval) ama SignalEmitted 0 (gerçek sinyal). 5 AND koşul aşırı sıkı, eş zamanlı sağlanmıyor.

**Açıklama:**
- **SignalSkipped** = evaluator AND koşullarını her bar'da kontrol etti, hiçbiri sağlanmadı (skip log)
- **SignalEmitted** = gerçek alım sinyali, order yaratıldı

Loop 50 1h'da 0 emit → filtre çok sıkı kanıtı. t120 beklenmedi, hızlı pivot.

## Loop 50 → Loop 51 Filtre Gevşetme

| Parametre | Loop 50 | **Loop 51** | Etki |
|---|---|---|---|
| `BbStdMultiplier15m` | 2.0 | **1.5** | BB lower band çok daha yakın → daha sık dokunulur |
| `RsiOversoldThreshold15m` | 40 | **50** | "oversold" tanımı genişler (RSI<50 normal aralığa yaklaşır) |
| `VolumeMultiplier1m` | 1.2 | **0.8** | hacim teyidi gevşek (vol average altı bile kabul) |
| `MinAtrPct1m` | 0.0003 | **0.0002** | sessiz coin daha az dışlanır |

Diğer aynı: KlineInterval 1m+15m, EmaFast=9/Slow=21, RsiPeriod=14, AtrPeriod=14, TpAtr 1.5×, SlAtr 0.8×, MaxHold 30dk, Cooldown 3 bar, R:R 1.875:1

## 5 Aktif Coin (Aynı)
BTC, ETH, SOL, XRP, ADA. MaxOpenPositions=5.

## Beklenti
- Frekans: 0/h → **5-15/h** (4 filtre birden gevşek, AND olasılığı 5-10x artmalı)
- WR: %30-45 (kalite biraz düşer ama frekans öncelikli)
- Net/h paper: hedef +$0 ile +$0.50

## Boot State
| Metrik | Değer |
|---|---|
| Mode | Paper |
| StartingBalance | $500.0000 |
| Equity | $500.0000 |
| Active | 5 (HybridMomentum1m gevşek) |
| MaxOpenPositions | 5 |
| API Port | 5188 |

## Halt Eşikleri
- Realized < -$1.50 → Loop 52 (radikal pivot, binance-expert)
- 5+ ardışık SL → Loop 52
- Signals = 0 (60dk içinde) → daha fazla gevşet (BBstd 1.5→1.3, RSI 50→55)
- WR < %25 (10+ trade sonrası) → Loop 52

## Sıradaki Wakeup
**ScheduleWakeup 1800s → t=30dk (01:20 TR)**

— PM 2026-04-29 Loop 51 boot
