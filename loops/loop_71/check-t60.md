# Loop 71 — Check t=60dk (2026-05-01 06:01 TR)

## Sonuç: ✓ İLK EMITLER GELDİ (Skor sistemi çalışıyor)

KMS skor-tabanlı evaluator (4/6) **2 emit/60dk** üretti. ADA SL -$0.09 ilk Realized. Frekans (2/h) hedef (8-15/h) altında ama sistem çalışıyor.

## Sayım (60dk)
| Metrik | Değer |
|---|---|
| **SignalEmitted** | **2** (XRP + ADA Long) |
| **SignalSkipped** | **63** |
| OrderPlaced | 3 (2 entry + 1 exit) |
| OrderFilled | 3 |
| PositionOpened | 2 |
| **PositionClosed** | **1** (ADA SL/MaxHold) |
| RiskAlert | **0** ✓ |
| **Realized PnL** | **-$0.089** (ADA loss) |

## Pozisyonlar
| Symbol | Side | Hold | Entry | Mark | UPnl | Status |
|---|---|---|---|---|---|---|
| **XRPUSDT** | Long | 31min | $1.3698 | $1.3696 | **-$0.021** | Açık (1) |
| **ADAUSDT** | Long | 31min | $0.2469 | $0.2473 | $0 | Closing (2) |

→ ADA closing path'te. XRP MaxHold 30dk'a yakın (skor 4/6 → MaxHold 30 veya 5/6 → 45).

## Asimetri (devam)
- XRP, ADA emit veriyor (CoinClass=alt)
- BTC, ETH, SOL **hala 0 emit** (large/mid)
- CoinClass=large MinAtr 0.0002 yine de tetiklenmiyor → BTC/ETH ATR/Close oranı testnet'te çok düşük olabilir

→ Loop 72'de potansiyel: large MinAtr 0.0002 → 0.0001 (daha düşük)

## Loop Karşılaştırma
| Loop | Algoritma | t60 emit | Realized |
|---|---|---|---|
| L68 | AND, RSI 35, TC 0.8 | 2 (SOL+XRP) | $0 |
| L70 | AND, RSI 38, TC 0.6 | 0 | $0 |
| **L71** | **Skor 4/6, Zone+Mom, CoinClass** | **2 (XRP+ADA)** | **-$0.09** |

→ L68 ile aynı emit sayısı, ama ALG değişti.

## Karar
| Şart | Aksiyon |
|---|---|
| ≥1 emit + Realized > -$1 | **Loop 71 devam, t90 (öğreniyor)** |
| RiskAlert = 0 | ✓ |
| 5+ ardışık SL | 1 SL var |
| Frekans 2/h | Hedefin altında ama sistem çalışıyor |

## t90 Beklenti (06:31 TR)
- XRP TP/SL/MaxHold (Realized ikinci resim)
- Yeni emit (cooldown 15dk sonrası)
- BTC/ETH'ten emit gelirse asimetri çözüldü

## Halt Eşikleri
- Realized < -$1.50 → Loop 72 binance-expert (StreakGuard implement)
- 5+ ardışık SL → halt
- t90'da BTC/ETH 0 emit → Loop 72 large MinAtr 0.0002 → 0.0001 + diagnostic logging

## Sıradaki Wakeup
**ScheduleWakeup 1500s → t=90dk (06:26 TR)**

— PM 2026-05-01 Loop 71 check-t60
