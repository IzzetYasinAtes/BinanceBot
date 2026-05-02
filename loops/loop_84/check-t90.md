# Loop 84 — Check t=90dk (2026-05-02 20:25 TR) — Pozisyonlar Kötüleşti, SL Yakın

## Sonuç: BTC/ETH UPnL -$0.26, SL'e Yaklaşıyor, Yeni Emit Yok

t60→t90 (30dk): 0 yeni emit, 0 close. BTC ve ETH UPnL -$0.029 → **-$0.260** (-$0.23 ek kötüleşme). Realized **$0 sabit**, Counter 0/4.

## Sayım (90dk)
| Metrik | t60 | **t90** | Δ |
|--------|-----|---------|---|
| SignalEmitted | 2 | 2 | sabit |
| SignalSkipped | 58 | 93 | +35 |
| OrderFilled | 2 | 2 | sabit |
| PositionClosed | 0 | 0 | sabit |
| Realized | $0 | $0 | sabit |
| Open | 2 | 2 | sabit |
| **Açık UPnL** | **-$0.029** | **-$0.260** | **-$0.23** |

## Açık Pozisyon (Hepsi Kötü Yön)
| Symbol | Hold | UPnl t60 | UPnl t90 | %UPnl | SL'e Mesafe |
|--------|------|----------|----------|-------|-------------|
| BTC | 80min | +$0.007 | **-$0.082** | -%0.08 | -%0.32 (MaxSL %0.4) |
| ETH | 76min | -$0.036 | **-$0.178** | -%0.18 | -%0.22 (MaxSL %0.4) |

**ETH yakın SL** (%0.22 mesafe). Eğer hit olursa Realized -$0.40 → -$0.45.

## Frekans Analizi
- 2 emit / 90dk = **1.3 emit/h** (hedef 8-12)
- Hard-gate kaldırma sadece **ilk 30dk'da 2 emit** verdi, sonra sessiz
- Pazar volatilitesi düşük (gece TR saati)
- Pattern composer hâlâ çoğu bar'da threshold ≥4 sağlamıyor

## Karar
| Şart | Aksiyon |
|---|---|
| Realized $0 (>-$1.50) | **Loop 84 devam, t120** |
| Açık UPnL -$0.26 | İzle (SL hit Realized -$0.40 max) |
| 0 yeni close | t120'da BE-stop test fırsatı azalıyor |
| 0 yeni emit 60dk | Frekans sorunu devam |

## L80/L81/L82/L83/L84 Karşılaştırma (90dk)
| Loop | Emit | Closed | Realized | Açık UPnL |
|------|------|--------|----------|-----------|
| L80 | 7 | 3 | -$0.45 | n/a |
| L81 | 3 | 0 | $0 | +$0.306 ✓ |
| L82 | 2 | 2 | -$0.13 | -$0.07 |
| L83 | 0 | 0 | $0 | $0 |
| **L84** | **2** | **0** | **$0** | **-$0.260** |

L81 t90 hâlâ en iyi (UPnL +$0.306). L84 frekans düşük + UPnL kötü.

## t120 Beklenti (20:55 TR)
- ETH SL hit olabilir (Realized -$0.40)
- BTC recovery veya SL
- Yeni emit (henüz görmedik 60dk)
- Realized: $0 → -$0.40 muhtemel

## Halt Eşikleri
- Realized < -$1.50 → halt + Loop 85
- 2 simultane SL → -$0.80 Realized (ETH+BTC), hâlâ tolere
- 3+ ardışık küçük loss → spec yanlış (henüz close yok)

## Sıradaki Wakeup
**ScheduleWakeup 1500s → t=120dk (20:50 TR)** — ETH SL kritik

— PM 2026-05-02 Loop 84 check-t90 (pozisyonlar kötü, ETH SL yakın, frekans düşük gece volatilite)
