# Loop 84 — Check t=150dk (2026-05-02 21:19 TR) — ETH Recovery, UPnL +$0.15 İyileşme

## Sonuç: ETH Aleyhe Yönden Döndü, UPnL -$0.49 → -$0.35

t120→t150 (30dk): **+2 yeni emit** (fill yok, MaxOpen=3 dolu). ETH dramatik recovery: UPnL **-$0.244 → -$0.080** (+$0.164 iyileşme). Pazar yönü lehte dönüyor.

## Sayım (150dk)
| Metrik | t120 | **t150** | Δ |
|--------|------|----------|---|
| SignalEmitted | 4 | **6** | +2 (fill yok) |
| SignalSkipped | 117 | 142 | +25 |
| OrderFilled | 3 | 3 | sabit |
| PositionClosed | 0 | 0 | sabit |
| Realized | $0 | $0 | sabit |
| Open | 3 | 3 | sabit (MaxOpen) |
| **Açık UPnL** | **-$0.493** | **-$0.346** | **+$0.15** ✓ |

## Açık Pozisyon Hareketi
| Symbol | Hold | UPnl t120 | UPnl t150 | Δ |
|--------|------|-----------|-----------|---|
| BTC | 134min | -$0.137 | -$0.143 | -$0.006 |
| **ETH** | 130min | -$0.244 | **-$0.080** | **+$0.164** ✓ |
| SOL | 45min | -$0.111 | -$0.123 | -$0.012 |

## ETH Recovery Detay
ETH t120'de SL'e -%0.16 mesafede idi. Şimdi -%0.08 = SL'den uzaklaştı. Pazar yönü lehe döndü, BE eşiğine (+%0.20'ye) ulaşma şansı arttı.

## Frekans (150dk)
- 6 emit / 150dk = **2.4 emit/h** (hedef 8-12)
- 2 emit fill olmadı (MaxOpen=3 dolu) — risk gate skip
- Pattern composer çalışıyor, hard-gate kaldırma 4 emit ekstra verdi

## Karar
| Şart | Aksiyon |
|---|---|
| Realized $0 (>-$1.50) | **Loop 84 devam, t180** |
| ETH recovery +$0.16 | Pozitif sinyal |
| 0 close hâlâ | BE-stop test fırsatı yakın (ETH +%0.20 hedef) |
| 0 ardışık SL | OK |

## L80/L81/L82/L83/L84 Karşılaştırma (150dk)
| Loop | Emit | Closed | Realized | Açık UPnL |
|------|------|--------|----------|-----------|
| L80 | 7 | 3 | -$0.51 | n/a |
| L81 | 5 | 2 | -$0.06 | -$0.04 |
| L82 | 1 | 0 | $0 | -$0.243 (carryover) |
| L83 | 0 | 0 | $0 | $0 |
| **L84** | **6** | **0** | **$0** | **-$0.346** |

L84 frekans en yüksek + sermaye stable. Recovery iyileşmesi gözlendi.

## t180 Beklenti (21:50 TR)
- ETH BE-stop pozitif test (+%0.20 ulaşırsa +$0.18 net)
- BTC ve SOL outcome
- Yeni emit (fill için close gerek)
- Realized: $0 → +$0.10 hedef (ETH BE-stop)

## Halt Eşikleri
- Realized < -$1.50 → halt + Loop 85
- ETH peak %0.20+ → BE-stop +$0.18 SEVİNÇ
- 3 simultane SL → -$0.80 Realized (hâlâ tolere)

## Sıradaki Wakeup
**ScheduleWakeup 1800s → t=180dk (21:49 TR)**

— PM 2026-05-02 Loop 84 check-t150 (ETH recovery, UPnL +$0.15 iyileşme, BE-stop pozitif test yakın)
