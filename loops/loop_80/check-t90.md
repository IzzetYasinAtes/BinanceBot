# Loop 80 — Check t=90dk (2026-05-02 06:38 TR) — ADX Gate Gevşetildi

## Sonuç: 90dk Hala 0 Yeni Fill, ADX Gate Gevşetme (KMS 20→18, BBR 25→30)

t60→t90: 1 yeni emit (3 toplam) ama fill yok. 189 SignalSkipped, ADX gate çok katı. Frekans 2/h hedefin (8-15/h) çok altında.

## Düzeltme: ADX Gate Gevşet
- **KMS AdxTrendingThreshold**: 20 → 18 (daha permisif, zayıf trend de geçer)
- **BBR AdxRangeMax**: 25 → 30 (range daha geniş, hafif trend de OK)
- 10 row UPDATE (5 KMS + 5 BBR JSON inject)

**Önce yapılan keşif**: backend-dev ADX param'larını sadece appsettings.json'a ekledi, mevcut DB row'larındaki ParametersJson'a yansımadı (StrategySeeder yeni param için merge/inject yapmıyor). Kod default değerleri (20/25) kullanıyordu. Manuel inject yapıldı.

## Sayım (90dk)
| Metrik | t60 | **t90** | Δ |
|---|---|---|---|
| SignalEmitted | 2 | **3** | +1 |
| SignalSkipped | 129 | **189** | +60 |
| OrderFilled | 2 | 2 | 0 |
| PositionClosed | 1 | 1 | 0 |
| Realized PnL | -$0.155 | **-$0.155** | sabit |

## Cumulative
- L71-L79: -$7.74
- L80 t90: -$0.155
- **TOTAL: -$7.90** (sabit)

## Karar
| Şart | Aksiyon |
|---|---|
| 0 yeni fill (90dk) + ADX katı | **ADX gate gevşetildi (KMS 20→18, BBR 25→30) ✓** |
| Sermaye korundu | İyi sinyal (Loop 79'daki yanlış emit'ler önlendi) |
| Bot DB fresh okur | Restart gereksiz |

## t120 Beklenti (07:08 TR)
- KMS BTC/ETH/SOL emit (ADX 18-25 arası bölgede aktif olabilir)
- BBR Range coin emit (BBR ADX 30 altı = daha çok pencere)
- Realized iyileşme

## Halt Eşikleri
- Realized < -$1.00 → Loop 81 backlog
- 5+ ardışık SL → CB reset
- 0 emit (120dk) → daha gevşet

## Sıradaki Wakeup
**ScheduleWakeup 1500s → t=120dk (07:03 TR)**

— PM 2026-05-02 Loop 80 check-t90 (ADX gate gevşetme)
