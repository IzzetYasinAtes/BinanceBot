# Loop 49 — Check t=180dk (2026-04-28 16:03 TR)

## Durum: 2 LONG Hala Açık (MaxHold 120dk hatırlatma)

| Metrik | t120 | t180 | Δ |
|---|---|---|---|
| Cash | $299.79 | $299.79 | 0 |
| OpenPositionsValue | $199.86 | $199.99 | +$0.13 (mark recovery) |
| Equity | $499.66 | $499.79 | +$0.13 |
| Realized | $0 | $0 | 0 |
| Unrealized | -$0.194 | **-$0.062** | **+$0.132** ✓ toparlama |
| Net | -$0.344 | -$0.213 | +$0.132 |
| Open Pos | 2 | 2 | 0 (henüz kapanmadı) |
| Signals | 2 | 2 | 0 yeni |
| SignalSkipped | 565 | 875 | +310 |
| WsStateChanged | 51 | 51 | 0 (stabil) ✓ |

## Açık Pozisyonlar Detay

| Coin | Entry | Mark | Hold | MaxHold | Unrealized |
|---|---|---|---|---|---|
| BTC | $76,350 | $76,307 (-%0.06) | 93dk | 120dk → 27dk kaldı | **-$0.056** |
| XRP | $1.3817 | $1.3816 (-%0.01) | 63dk | 120dk → 57dk kaldı | **-$0.006** |

İki pozisyon da entry'ye çok yakın → tipik **mean reversion** davranışı. Mark fiyatlar BB lower'dan toparlanıyor ama TP'ye henüz uzak.

## Halt Kriter
| Kriter | Durum | Verdict |
|---|---|---|
| Realized < -$1.50 | $0 | ✓ buffer $1.50 |
| 5+ ardışık SL | 0 | ✓ |
| Zombie | 93dk + 63dk (MaxHold 120dk) | ✓ |
| Signal akmıyor | 2 sinyal | ✓ |
| WS / CB | 51 stabil | ✓ |
| API health | http://localhost:5188 ✓ | ✓ |

**HALT YOK + TOPARLAMA TRENDİ.**

## Yorum
Mean reversion tipik çalışma:
- BB lower'da open
- Bounce başladı (mark recovery -$0.194 → -$0.062)
- TP'ye ulaşma 27dk içinde (BTC) muhtemel
- TimeStop riski hala var (mark flat kalırsa)

## Beklenen t240 Sonuç
- BTC TimeStop ~16:30 TR'de (entry + 120dk)
- XRP TimeStop ~17:00 TR'de
- t240 = 17:03 TR → ikisi de kapanmış olacak

Mark fiyat anki seviyede kalırsa:
- BTC: -$0.056 unrealized + komisyon $0.075 entry/exit ≈ **-$0.20 net**
- XRP: -$0.006 unrealized + komisyon $0.150 ≈ **-$0.16 net**
- Toplam: **-$0.36** (kötü senaryo, halt eşiğinin çok altı)

Eğer TP'lerden biri tetiklenirse:
- BTC TP +%0.67 = +$0.67 - $0.15 fee = **+$0.52 net**
- XRP TP +%0.47 = +$0.47 - $0.15 fee = **+$0.32 net**

## Sıradaki Wakeup
**ScheduleWakeup 3600s → t=240dk (17:03 TR)**

t240'da:
- BTC + XRP kapanmış (TimeStop ya TP ya SL)
- İlk Realized PnL ölçümü
- Yeni sinyal var mı?

— PM 2026-04-28 Loop 49 t=180
