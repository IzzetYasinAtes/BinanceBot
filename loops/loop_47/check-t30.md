# Loop 47 — Check t=30dk (2026-04-28 11:40 TR) — FİLTRE ÇOK SIKI

## Frekans 9.5x Düştü, Tek Trade

| Metrik | Loop 46 t30 | Loop 47 t30 | Δ |
|---|---|---|---|
| Cash | $298.66 | $499.87 | +$201.21 (1 pos kapandı) |
| Equity | $499.24 | $499.87 | +$0.63 |
| Realized | -$0.996 | **-$0.129** | +$0.867 |
| Net | -$0.610 | -$0.129 | +$0.481 |
| Komisyon | $0.750 | $0.150 | -$0.600 |
| Signals | **10** | **1** | **-9 (-90%)** |
| Closed Pos | 4 | 1 | -3 |
| WinRate | %0 (0/4) | %0 (0/1) | — |
| SignalSkipped | 377 | 383 | +6 (eval rate aynı, kabul oranı düşük) |

## Tek Trade

| Coin | Hold | Realized | Tip |
|---|---|---|---|
| BTC | 12dk 23s | **-$0.129** | TimeStop (MaxHold 12dk tam) |

Mark BTC entry → exit: küçük fluctuation, fee baskın.

## Halt Kriter
| Kriter | Durum | Verdict |
|---|---|---|
| Realized < -$1.50 | -$0.129 | ✓ buffer **$1.37** |
| 5+ ardışık SL/TimeStop | 1 | ✓ |
| WR < %25 (5+ trade) | 1 trade ölçüm değil | ⏳ |
| Signals = 0 | 1 sinyal var | ✓ |
| Open pos 0 + Realized<-$1.20 | -$0.129 | ✓ |

**HALT YOK** ama frekans hedef altı.

## Yorum
Filtre güçlendirme **aşırı oldu** — 5 parametre birden sıkıldı:
- RSI 40-65 → 45-60 (bant %50 daraldı)
- VolumeMultiplier 0.8 → 1.2 (%50 sıkı)
- MinAtrPct 0.0003 → 0.0005 (%67 sıkı)

Sonuç: 9.5x frekans düşüşü = AND koşullarının kombine olasılığı çok düşük.

Hedef: frekans 8-12/h, gelen 2/h. **Orta yol gerek.**

## Karar
**Loop 47 DEVAM** ama t60'ta agresif değerlendirme:
- Eğer t60'da hala 1-3 sinyal → Loop 48 boot **orta yol** (RsiLowerBand 45→42, VolumeMultiplier 1.2→1.0, MinAtrPct 0.0005→0.0004, MaxHold 12→10dk)
- Eğer t60'da 4+ sinyal + Realized > -$0.50 → Loop 47 devam normal cycle
- Realized<-$1.50 → Loop 48 radikal pivot (binance-expert)

## Sıradaki Wakeup
**ScheduleWakeup 1800s → t=60dk (12:10 TR)**

— PM 2026-04-28 Loop 47 t=30
