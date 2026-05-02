# Loop 81 — Check t=120dk (2026-05-02 13:08 TR) — İlk 2 Close: Trailing-Exit Küçük Loss

## Sonuç: Trailing Buffer Çok Dar — Komisyon Eşiği Geçilmiyor

t90→t120 (30dk): **2 close (ikisi de trailing-exit)**, **+1 yeni emit (BTC)** ama açık değil. Realized **-$0.0586** (küçük net negatif). Trailing %0.15 buffer komisyon %0.20 (entry+exit) eşiğini aşamıyor.

## Sayım (120dk)
| Metrik | t90 | **t120** | Δ |
|--------|-----|----------|---|
| SignalEmitted | 3 | **4** | +1 (BTC) |
| SignalSkipped | 92 | 121 | +29 |
| OrderFilled | 3 | 5 | +2 (trailing exits) |
| PositionOpened | 3 | 3 | sabit |
| **PositionClosed** | 0 | **2** | **+2 (ETH+SOL)** |
| **Realized PnL** | $0 | **-$0.0586** | -$0.06 |
| Open | 3 | 1 (XRP) | -2 |

## Closed Detail
| Symbol | Hold | Entry | Exit | Peak | PnL | Exit Tipi |
|--------|------|-------|------|------|-----|-----------|
| ETH | 109min | 2301.71 | 2303.89 | **2307.60** (+%0.26) | **-$0.055** | trailing-exit |
| SOL | 69min | 83.87 | 83.99 | **84.15** (+%0.33) | -$0.003 | trailing-exit |

→ Her ikisi de peak'te +%0.26-%0.33 kar potansiyeli vardı, ama trailing %0.15 buffer ile geri çekme sırasında exit. Komisyon ~%0.20 (giriş+çıkış) → net küçük kayıp.

## Açık Pozisyon
| Symbol | Hold | UPnl | %UPnl | Durum |
|--------|------|------|-------|-------|
| XRPUSDT | 43min | **+$0.022** | +%0.022 | Devam, BE altında |

## Kritik Tespit: Trailing Buffer Yetersiz
**Mevcut**: Trailing aktif UPnL > +%0.10, exit = peak × (1 - 0.0015) = peak × 0.9985 (%0.15 geri çekilme)
**Sorun**: Komisyon ~%0.20 (giriş + çıkış) → trailing exit profit eşiği = +%0.20+. Peak +%0.26'ya kadar çıktı ama %0.15 geri çekilince çıkıldı = +%0.11 → komisyon sonrası **negatif**.

**Loop 82 backlog**: Trailing buffer 0.0015 → 0.0030 (%0.30 genişlik) veya komisyon-aware exit (peak × (1 - feeRate × 1.5) = peak × 0.997).

## Karar
| Şart | Aksiyon |
|---|---|
| Realized -$0.06 (>-$2.00) | **Loop 81 devam, t150** |
| Trailing buffer dar | **Loop 82 backlog** (henüz halt değil) |
| 1 açık (XRP +$0.022) | İzle |
| 4. emit (BTC) açık değil | risk gate veya cooldown |

## L80 vs L81 Karşılaştırma (120dk)
| Metrik | L80 t120 | **L81 t120** |
|--------|----------|--------------|
| Emit | 7 | 4 |
| Closed | 3 | 2 |
| Realized | -$0.51 | **-$0.06** ✓ (8x iyileşme) |
| WR | 0/3 | 0/2 (ama loss küçük) |

L81 küçük loss alıyor (trailing-exit) ama hızlı SL almıyor (multi-pattern stack güvenlik).

## t150 Beklenti (13:35 TR)
- XRP outcome (TP/BE/trailing/timestop)
- 5. emit (4 coin slot boş, MaxOpen=2 boş)
- BTC fill (4. emit)
- Realized: ~-$0.05 ila +$0.10 hedef

## Halt Eşikleri
- Realized < -$2.00 → Loop 82
- 5+ ardışık SL → CB tripped
- 4 ardışık trailing-exit küçük loss → Loop 82 backlog trigger (trailing buffer fix)

## Sıradaki Wakeup
**ScheduleWakeup 1800s → t=150dk (13:35 TR)**

— PM 2026-05-02 Loop 81 check-t120 (2 trailing-exit küçük loss, sistem stabil ama trailing tune gerek)
