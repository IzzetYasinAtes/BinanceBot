# Loop 78 — Check t=210dk (2026-05-01 22:53 TR) — ✓ Emit Geldi (BBW 0.003 yeterli)

## Sonuç: BBW 0.003 Düzeltme İşe Yaradı — 3 Yeni Emit

t180→t210 fark: BBW threshold 0.003 düzeltmesi sonrası **3 yeni emit** geldi (ETH duplicate skip + BTC 10534 fill + 1 daha). BTC 10534 açık, **UPnl +$0.027** (BE trigger %0.10 yarı yolda).

## Sayım (210dk)
| Metrik | t180 | **t210** | Δ |
|---|---|---|---|
| **SignalEmitted** | 3 | **6** | **+3** ✓ |
| SignalSkipped | 173 | 197 | +24 |
| OrderPlaced | 5 | 6 | +1 |
| OrderFilled | 4 | 5 | +1 |
| PositionOpened | 2 | 3 | +1 |
| PositionClosed | 2 | 2 | 0 |
| Realized PnL | -$0.39 | -$0.39 | 0 |
| RiskAlert | 0 | 0 | 0 |

## ✓ Açık Pozisyon (Status=1)
| Symbol | Hold | Entry | Mark | UPnl | %UPnl |
|---|---|---|---|---|---|
| **BTCUSDT 10534** | 13min | $78351 | $78373 | **+$0.027** | +%0.03 (BE %0.10 yarı yolda) |

→ BTC trend yukarı, BE trigger'a 7-8 dakika içinde ulaşırsa BE move + Trailing aktif olabilir.

## BBW Son Skip Değerleri
- 0.0023, 0.0024, 0.0024, 0.0023, 0.0025

→ BBW < 0.003 hala bbw_hard_gate skip ediyor. BTC için BBW > 0.003 olduğunda emit verdi.

## Stack Davranış
- BBW hard-gate (0.003) ✓ çalışıyor (24+ skip son 30dk)
- BBW > 0.003 olduğunda KMS emit verdi (BTC, ETH duplicate)
- Tam stack (BE+Trail+EMA200+BBW) aktif

## Karar
| Şart | Aksiyon |
|---|---|
| ≥1 yeni emit fill (BTC 10534) | **Loop 78 devam, t240** |
| Realized -$0.39 sabit | ✓ |
| BTC açık BE yakın | TP/BE/trailing potansiyeli |

## t240 Beklenti (23:18 TR)
- BTC 10534 BE trigger geçer → BE move + Trailing aktif
- TP hit veya BE stop hit küçük kar
- Yeni emit (BBW > 0.003 stable)

## Halt Eşikleri
- Realized < -$0.80 → Loop 79 binance-expert
- 5+ ardışık SL → CB reset + Loop 79
- 1h hala 0 yeni emit → BBW threshold düzelt veya Loop 79

## Sıradaki Wakeup
**ScheduleWakeup 1500s → t=240dk (23:18 TR)**

— PM 2026-05-01 Loop 78 check-t210 (BBW 0.003 yeterli, BTC açık)
