# Loop 58 — Check t=420dk (2026-04-29 16:07 TR) — ETH TOPARLAMA

## ETH Unrealized İyileşti -$0.168 → -$0.043

| Metrik | t360 | t420 | Δ |
|---|---|---|---|
| Cash | $399.18 | $399.18 | 0 |
| OpenPositionsValue | $99.82 | $100.01 | +$0.19 (mark recovery) |
| Equity | $498.99 | **$499.19** | **+$0.20** ✓ |
| Realized | -$0.764 | -$0.764 | 0 (yeni kapanış yok) |
| Unrealized | -$0.168 | **-$0.043** | **+$0.125** ✓ toparlama |
| Net | -$1.007 | -$0.814 | +$0.193 |
| Komisyon | $0.525 | $0.525 | 0 |
| Open Pos | 1 (ETH) | 1 (ETH) | 0 |
| Closed Pos | 3 | 3 | 0 |
| **SignalEmitted** | 4 | 4 | 0 yeni |
| SignalSkipped | 1287 | 1459 | +172 |
| WinRate | %33 | %33 (1/3) | — |

## ETH Açık (37dk hold, MaxHold 120dk → 83dk kaldı)

- Entry $2,314.48 @ 12:30 UTC | Mark $2,313.48 (-%0.04, recovery)
- TP yakını ~$2,330 (+%0.67 hedef)
- SL ~$2,303 (-%0.50)
- Unrealized: **-$0.043** (komisyon dahil net -$0.118 olur kapanışta)

## Halt Kriter
| Kriter | Durum | Verdict |
|---|---|---|
| Realized < -$1.50 | -$0.764 | ✓ buffer **$0.74** |
| 4+ ardışık SL | 2 SL (zincir uzar mı bilinmez) | ✓ |
| WR < %25 (5+ trade) | %33 (3 trade) | ✓ |
| ETH SL + realized -$1.30+ | ETH açık | ⏳ |
| Zombie | 37dk (MaxHold 120dk) | ✓ |

**HALT YOK + ETH TOPARLAMA TRENDİ.**

## Senaryo (ETH güncel)
- **Best (TP):** mark $2,330'a uzanırsa +$0.45 → realized -$0.31 (toparlama!)
- **Mark anki TimeStop:** -$0.12 net → realized -$0.88 (halt altı)
- **Worst (SL):** -$0.55 net → realized -$1.31 (halt eşiğine çok yakın ama altı)

## Karar
**Loop 58 DEVAM** ✓ ETH toparlama, halt güvende.

## Sıradaki Wakeup
**ScheduleWakeup 3600s → t=480dk (17:07 TR)**

t480'de:
- ETH ya kapanmış (TimeStop 17:30 TR yaklaşır), ya hala açık
- Halt değerlendirme

— PM 2026-04-29 Loop 58 t=420
