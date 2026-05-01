# Loop 75 — Check t=120dk (2026-05-01 14:53 TR) — Realized -$2.30 (Eşiğe Yaklaşıyor)

## Sonuç: BE Devam Ama 1 Büyük Loss (-$0.37) Daha — Realized -$2.30

t90 → t120 fark:
- 1 yeni close: ADA 10510 timestop **-$0.37** (BE'ye varmadı, UPnl -$0.11 idi)
- 4 yeni açık: SOL/BTC/ETH/XRP (3'ü pozitif, 1'i negatif)
- BTC 10513 UPnl +$0.051 → BE trigger yakın

## Sayım (Loop 74 boot 5.5h)
| Metrik | Değer | Δ (t90 → t120) |
|---|---|---|
| SignalEmitted | 30 | +4 |
| PositionClosed | 14 | +1 |
| Open (Status=1) | 4 | +3 |
| **Realized PnL** | **-$2.30** | **-$0.37** |

## Yeni Trade Sonucu (1)
| # | Time | Symbol | PnL | Reason |
|---|---|---|---|---|
| 14 | 11:35 | ADA 10510 | **-$0.37** | timestop (BE'ye varmadı) |

## Açık Pozisyonlar (Status=1)
| Symbol | Hold | UPnl | %UPnl | BE Trigger %0.10? |
|---|---|---|---|---|
| **BTCUSDT 10513** | 13min | **+$0.051** | **+%0.05** | YARI YOLDA |
| XRPUSDT 10515 | 13min | +$0.059 | +%0.04 | |
| SOLUSDT 10512 | 14min | +$0.032 | +%0.03 | |
| ETHUSDT 10514 | 13min | -$0.011 | -%0.001 | negatif |

## Cumulative Update
- L71: +$0.85
- L72-L75: ~-$3.15
- **TOTAL: ~-$2.30**

## ⚠️ Loop 76 Eşiği Yakın
- Realized -$2.30 (eşik -$2.50)
- t150'de eşiği geçerse Loop 76 binance-expert ZORUNLU
- BTC açık BE trigger yakın → BE move + kar potansiyeli
- ADA 10510 yeni büyük loss göstergesi: BE'ye varmayan pozisyonlar hala -$0.30+ loss üretebilir

## Karar
| Şart | Aksiyon |
|---|---|
| Realized -$2.30 (≥-$2.50) | **Loop 75 devam, t150** |
| BE feature aktif | ✓ |
| 1 büyük loss (BE'ye varmadı) | Loop 76 entry kalitesi gerekli |

## t150 Beklenti (15:15 TR)
- BTC 10513 BE trigger geçer mi? (%0.05 → %0.10)
- 4 açık TP/BE outcome
- Realized iyileşme ya da -$2.50 eşik
- Eşik geçerse Loop 76 binance-expert KESIN

## Halt Eşikleri
- Realized < -$2.50 → **Loop 76 binance-expert ZORUNLU**
- Realized < -$3.00 → Acil halt
- 5+ ardışık SL → CB reset

## Sıradaki Wakeup
**ScheduleWakeup 1500s → t=150dk (15:18 TR)**

— PM 2026-05-01 Loop 75 check-t120 (-$2.30 eşiğe yakın)
