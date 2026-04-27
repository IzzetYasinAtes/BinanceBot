# Loop 43 — Check t=390dk (2026-04-25 00:15 TR)

## 🎯 YENİ POZİSYON — DOGEUSDT LONG (İlk pozitif unrealized!)

| Metrik | Değer |
|---|---|
| Symbol | DOGEUSDT LONG |
| Entry @ 20:59 UTC (23:59 TR) | $0.0988899 (qty 1011, $99.9777) |
| StopPrice (SL) | $0.0983856 (-%0.51) |
| TakeProfit | $0.1000666 (+%1.19) |
| R:R tasarımı | **2.34** |
| Mark @ t390 | $0.0990 |
| **Unrealized** | **+$0.1265 (+%0.13)** ← İLK POZİTİF |
| Hold | 16dk 3sn / MaxHold 90dk |
| Komisyon (entry) | $0.0750 |
| Net henüz | +$0.0515 |
| Status | OPEN / AKTIF |

## DB Sayım
| Metrik | t330 | t390 | Δ |
|---|---|---|---|
| Cash | $499.5527 | $399.5000 | -$100.05 (DOGE pos kilit) |
| Equity | $499.5527 | $499.5536 | +$0.001 (mark up) |
| netPnl | -$0.4473 | -$0.4464 | +$0.001 |
| Pos Open | 0 | 1 | +1 ✓ |
| Pos Closed | 1 | 1 | 0 |
| Order Total | 2 | 3 | +1 |
| Signals | 1 | 2 | +1 ✓ |
| Fills | 2 | 3 | +1 |
| Komisyon | $0.150 | $0.225 | +$0.075 |

## Halt Kriter
| Kriter | Durum | Verdict |
|---|---|---|
| Realized < -$1.50 | -$0.4473 (closed sabit) | ✓ buffer **$1.05** |
| 5+ ardışık SL | 1 | ✓ |
| Zombie | 16dk açık | ✓ |
| WS / CB | Streaming, drift -623ms, HEALTHY | ✓ |

**HALT YOK + İLK POZİTİF MOMENTUM.**

## Önemli Gözlem
DOGE pozisyonu Asya gece geçişinde (20:59 UTC) açıldı — beklenmedik zaman. Top bar t330'da DOGE +%1.89 görünüyordu, bu momentum Donchian üst kırılım tetikledi.

**Eğer DOGE TP'ye ulaşırsa (~+$1.05 net = $1.20 gross - $0.15 komisyon)**, Loop 43 toplam:
- Realized -$0.4473 + DOGE TP +$1.05 = **+$0.60 NET KAR** (Loop 41-42-43 ilk kümülatif kâr)

Ama mark fiyat henüz TP'den uzakta (entry'den +%0.07, TP +%1.19'a 16x mesafe kaldı). Realistic: TP ya da SL %50/%50 olasılık (AR-GE %42 WR varsayımı).

## Playwright Smoke (1 sayfa)
- ui-t390-01-positions-open.png — DOGEUSDT LONG 1011 qty, entry 0.0989, mark 0.0990, +$0.1265 (+%0.13) YEŞİL, 16dk 3sn AKTIF
- Console error 0
- TP/SL kolonu hala "—" (backlog)

## Sıradaki Wakeup
**ScheduleWakeup 3600 → t=450dk (01:15 TR)**

DOGE pozisyon 60dk daha hold (toplam 76dk, MaxHold 90dk'a yakın) → t450'de **kapanmış olur** (ya TP ya SL ya TimeStop).

— PM 2026-04-25 Loop 43 t=390
