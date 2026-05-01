# Loop 78 — Check t=60dk (2026-05-01 20:35 TR) — BBW Gevşedi, 3 Emit, 2 Loss

## Sonuç: Trend Güçlendi (BBW > 0.008) → 3 Emit, AMA 2 Loss

20:04-20:05 arası BBW eşiği geçti (0.0080-0.0082), bot 3 emit verdi (BTC/ETH/ADA). 2 closed: BTC -$0.14, ADA ~-$0.25. **Realized -$0.39** (eşik -$0.30 az geçti). BBW gate açıldıktan sonra yine entry kalitesi problem.

## KMS Emit Detay (log)
| Symbol | Skor | BBW | RSI/Prev | Decision |
|---|---|---|---|---|
| **BTCUSDT 10532** | **5/7** | 0.0082 ✓ | 41.9/33.8 (RSI oversold çıkış) | Emit (TP %0.30, SL %0.20) |
| ETHUSDT | 4/7 | 0.0081 ✓ | 44.5/36.6 | Emit (TP %0.24, SL %0.20) |
| ADAUSDT 10533 | 4/7 | 0.0080 ✓ (eşiğe yakın) | 50/37 | Emit (TP %0.20, SL %0.20) |

→ Hard-gate'ler hepsi geçti, BBW score 1 ekledi. AMA RSI Zone sadece 1 puan (zaten oversold çıkış oldu, momentum bitiyor olabilir).

## Sayım (60dk)
| Metrik | Değer |
|---|---|
| **SignalEmitted** | **3** |
| SignalSkipped | 63 |
| **bbw_hard_gate skip log** | **52** (büyük kısmı L78 başı, sonra trend güçlendi) |
| OrderFilled | 4 |
| PositionOpened | 2 |
| **PositionClosed** | **2** |
| RiskAlert | 0 |
| **Realized PnL** | **-$0.39** |

## Trade Sonuçları (2 closed)
| # | Symbol | PnL | Tip |
|---|---|---|---|
| BTCUSDT 10532 | -$0.14 | timestop muhtemelen |
| ADAUSDT 10533 | ~-$0.25 | SL/timestop |

## Stack Durumu
- BBW hard-gate ✓ ÇALIŞTI (52 skip, sermaye korundu)
- BBW > 0.008 olduğunda emit verdi (3 emit)
- AMA emit'ler hala loss → **entry timing problem**

## Karar
| Şart | Aksiyon |
|---|---|
| Realized -$0.39 (-$0.30 az geçti) | Loop 78 devam, t90 bekle |
| 3 emit ile 2 loss (WR %33) | Trend zayıf, oversold çıkış emit'leri |
| BBW gate çalışıyor | ✓ ama yetmiyor |

## Hipotez (Loop 79 backlog)
Oversold çıkış emit'leri (RSI 33-37 → 41-50) güçlü trend olmadan SL alıyor. Çözüm:
- MinScoreThreshold 4→5 (RSI zone 2 puan zorunlu = derin oversold + güçlü çıkış)
- Veya RSI Zone formülünü daha katı: `Rsi < 35 + Rsi > RsiPrev + 5` (RSI son 5dk'da +5 puan yükselmiş olmalı)

## t90 Beklenti (21:00 TR)
- Yeni emit (BBW > 0.008 tutarsa)
- BTC veya ADA TP/SL outcome
- Realized iyileşme veya Loop 79 MinScore 5 düzeltme

## Halt Eşikleri
- Realized < -$0.80 → Loop 79 MinScore 4→5 + RSI sıkılaştır
- 5+ ardışık SL → CB reset
- 1h 0 emit + BBW < 0.008 → eşik 0.006 düzelt (zaten çoğu zaman BBW 0.008+ oldu)

## Sıradaki Wakeup
**ScheduleWakeup 1500s → t=90dk (21:00 TR)**

— PM 2026-05-01 Loop 78 check-t60 (BBW gate çalıştı, entry kalitesi devam sorun)
