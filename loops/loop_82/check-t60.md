# Loop 82 — Check t=60dk (2026-05-02 16:03 TR) — Carryover Pozisyonlar Kötüleşti, Yeni Param Test Hâlâ Yok

## Sonuç: Carryover Mean-Reversion Geri Döndü, ETH 2h 48min Hold (MaxHold Aşıldı?)

t30→t60 (30dk): 0 yeni emit, 0 close. Carryover ETH/BTC fiyat aleyhe döndü, UPnL toplam **-$0.020 → -$0.152** (-$0.13 kötüleşme). Yeni Loop 82 param henüz gerçek test almadı.

## Sayım (60dk)
| Metrik | t30 | **t60** | Δ |
|--------|-----|---------|---|
| SignalEmitted | 1 | 1 | sabit |
| SignalSkipped | 35 | 65 | +30 |
| OrderFilled | 0 | 0 | sabit |
| PositionClosed | 0 | 0 | sabit |
| Realized PnL | $0 | $0 | sabit |
| Open | 2 (carryover) | 2 (carryover) | sabit |

## Carryover UPnL Hareketi
| Symbol | Hold | UPnl t30 | UPnl t60 | Δ |
|--------|------|----------|----------|---|
| ETHUSDT | **168min** ⚠️ | -$0.021 | **-$0.087** | -$0.066 |
| BTCUSDT | 99min | +$0.001 | **-$0.065** | -$0.066 |
| **Toplam** | | -$0.020 | **-$0.152** | -$0.132 |

## Kritik Tespit: ETH 168min Hold (MaxHold 60dk Aşıldı)
ETH eski param ile entry (Loop 81 t150). MaxHoldMinutes=60 ama bot pozisyonu kapatmıyor. Olası sebep:
- `MarkToMarketWorker` MaxHold enforce etmiyor (sadece pattern evaluator parametresi)
- Veya Position record'una yazılan MaxHoldDurationSeconds null (eski seed'de yoktu?)

→ **Loop 83 backlog**: MaxHold timestop enforcement kontrol et.

## Yeni Emit Sorunu
1 emit sabit kaldı 60dk boyunca. Sebep:
- CooldownBarsAfterSignal=2 (10dk) — 60dk'da 6 cooldown periyodu olmalı
- MaxOpen=3 ama 2 dolu, 1 slot var
- Pattern composer threshold ≥5 — 60dk'da 5 coin × 12 bar = 60 evaluation, 0'ı geçti

Pattern selektivitesi çok yüksek olabilir veya volume_surge_gate gün içinde sürekli fail. Loop 83 backlog: composer threshold 5→4 düşürme tartış.

## Karar
| Şart | Aksiyon |
|---|---|
| Realized $0 (>-$1.50) | **Loop 82 devam, t90** |
| Carryover -$0.15 kötüleşti | İzle, SL hit yakın değil |
| 0 yeni emit 60dk | Frekans sorunu, ama henüz halt değil |
| Yeni param test yok | t90+ kritik (carryover kapanır kapanmaz) |
| ETH 168min hold | **Loop 83 backlog: MaxHold enforcement bug?** |

## L80/L81/L82 Karşılaştırma (60dk)
| Metrik | L80 t60 | L81 t60 | **L82 t60** |
|--------|---------|---------|-------------|
| Emit | 6 | 2 | 1 |
| Closed | 2 | 0 | 0 |
| Realized | -$0.45 | $0 | **$0** ✓ |

L82 sermaye stable, AMA frekans + carryover sorunu var.

## t90 Beklenti (16:30 TR)
- ETH SL hit (-$0.30+) veya recovery
- BTC SL hit veya recovery
- Yeni emit (yeni param ile ilk close)
- Realized: -$0.30 ila $0 hedef

## Halt Eşikleri
- Realized < -$1.50 → halt + Loop 83
- ETH SL hit → -$0.30+ Realized, hâlâ tolere
- 0 yeni emit + 0 close 90dk → Loop 83 frekans/MaxHold tartış

## Sıradaki Wakeup
**ScheduleWakeup 1800s → t=90dk (16:30 TR)**

— PM 2026-05-02 Loop 82 check-t60 (carryover kötüleşti, MaxHold şüphe, yeni param test geç kaldı)
