# Loop 45 — Check t=240dk (2026-04-28 07:58 TR)

## BTC Kapandı (TimeStop) + XRP Açık (88dk)

| Metrik | t180 | t240 | Δ |
|---|---|---|---|
| Cash | $299.19 | $399.76 | +$100.57 (BTC pos kapandı) |
| OpenPositionsValue | $200.91 | $100.31 | -$100.59 |
| Equity | $500.09 | $500.07 | -$0.02 |
| Realized | $0 | **-$0.0775** | -$0.0775 (BTC TimeStop) |
| Unrealized | +$0.244 | +$0.188 | -$0.056 (XRP) |
| Net | +$0.094 | **+$0.0715** | -$0.0225 |
| Komisyon | $0.150 | $0.226 | +$0.076 (BTC exit) |
| Open Pos | 2 | 1 | -1 (BTC kapandı) |
| Closed Pos | 0 | 1 | +1 |
| Orders | 2 | 3 | +1 (BTC exit) |
| Signals | 2 | 2 | 0 (yeni signal yok) |
| Fills | 2 | 3 | +1 |
| WinningTrades | 0 | 0 | — |
| LosingTrades | 0 | 1 | +1 (BTC) |

## Pozisyon Detay

### BTCUSDT (KAPALI — TimeStop)
- Entry: $76,773.17 @ 03:15 UTC (06:15 TR)
- Exit: $76,829.25 @ 04:45 UTC (07:45 TR) — **MaxHold 90dk = TimeStop**
- TP: $77,204.98 (+%0.56) — UNREACHED
- SL: $76,472.48 (-%0.39) — UNREACHED
- Mark price kapanışta entry'den +%0.07 (TP'nin sadece %12'si)
- Komisyon: $0.0754 + $0.0755 = $0.1509
- **Realized: -$0.0775** (mark up = $0.0734, komisyon = -$0.1509 → -$0.0775)

### XRPUSDT (HALA AÇIK — 88dk hold, MaxHold 90dk)
- Entry: $1.3901 @ 03:30 UTC (06:30 TR)
- Mark: $1.3928 (+%0.19)
- TP: $1.3954 (+%0.39) — yakın, %50 mesafe kaldı
- SL: $1.3856 (-%0.32) — uzak
- Komisyon: $0.0751 entry
- **Unrealized: +$0.188** (mark profit + entry komisyon)
- 2dk içinde TimeStop tetiklenebilir → mark anki seviye kapanırsa net +$0.038 (komisyon dahil)

## Halt Kriter
| Kriter | Durum | Verdict |
|---|---|---|
| Realized < -$1.50 | -$0.077 | ✓ buffer **$1.42** |
| 5+ ardışık SL | 0 (BTC TimeStop, SL değil) | ✓ |
| Zombie | 88dk (XRP) MaxHold yakın | ⏳ |
| WS / CB | 4 state change normal | ✓ |
| API health | http://localhost:5188 ✓ | ✓ |

**HALT YOK + NET HALA POZİTİF (+$0.07).**

## Loop 41-45 Aggregate
| Loop | Trade | TP | SL/Time | Realized |
|---|---|---|---|---|
| 41 | 8 | 0 | 8 SL | -$1.7985 |
| 42 | 2 | 0 | 2 SL | -$0.7262 |
| 43 | 1 | 0 | 1 SL | -$0.4473 |
| 44 | 0 | 0 | 0 | $0 |
| 45 (t240) | 1 closed | 0 | 1 TimeStop | -$0.0775 |

İlk **TimeStop** (SL değil) → BTC mark flat = strateji yorumu: BB lower bounce gerçekleşti ama TP'ye yetmedi. R:R 1.43:1 düşük R:R = WR yüksek olmalı. 1 trade ölçüm değil.

## Karar
**Loop 45 DEVAM** (Realized -$0.077, buffer $1.42 sağlam).

XRP TimeStop'a 2dk → +$0.04 net beklenti. Loop 45 toplam +$0.04 - $0.08 BTC = **-$0.04 net** (~breakeven).

## Sıradaki Wakeup
**ScheduleWakeup 3600s → t=300dk (08:58 TR)**

Beklenti:
- XRP kapanmış (TimeStop +$0.04 net VEYA TP +$0.39 net)
- ABD piyasa açılışına yakın (UTC 13:30 = TR 16:30) → daha sonra ama Avrupa açılışı (UTC 06:00 = TR 09:00) yakın → yeni sinyal şansı artıyor

— PM 2026-04-28 Loop 45 t=240
