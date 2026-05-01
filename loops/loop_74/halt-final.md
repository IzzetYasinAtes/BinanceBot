# Loop 74 — Halt @ t90 (2026-05-01 13:08 TR) — RsiCeiling 60 + Slow-Bleed Devam

## Sonuç: -$0.976 ek loss (RsiCeiling 60 emit getirdi ama slow-bleed timestop devam)

Loop 74 t60'ta RsiCeiling 50 katı (0 emit) → 60'a geri alındı. Emit geldi (10 toplam) AMA 3 closed hepsi timestop loss. Loop 75 boot 12:48 TR'de (BE SL deploy) ama Loop 74'ten kalan pozisyonlar henüz BE trigger almadı.

## Sayım (Loop 74 boot sonrası 4.5h)
| Metrik | Değer |
|---|---|
| SignalEmitted | 10 |
| SignalSkipped | 88 |
| OrderPlaced | 10 |
| OrderFilled | 9 |
| PositionOpened | 6 |
| **PositionClosed** | **3** |
| RiskAlert | **0** |
| **Realized PnL** | **-$0.976** ❌ |

## Trade Sonuçları (3 closed, hepsi timestop)
| Symbol | Hold | PnL | Tip |
|---|---|---|---|
| ETHUSDT | ~30min | -$0.23 | timestop |
| SOLUSDT | ~30min | -$0.37 | timestop |
| (3rd) | ~30min | ~-$0.38 | timestop |

→ **SLOW-BLEED TIMESTOP PATTERN DEVAM** (Loop 73 ile aynı). RsiCeiling 60 emit getirdi ama TP/SL/timestop geometrisi iyileşmedi.

## Cumulative Yörünge
- L71: +$0.850 ✓
- L72: -$0.542
- L73: -$0.394
- **L74: -$0.976** ❌ (bu loop)
- **TOTAL: -$1.062** ❌

## Açık Pozisyonlar (Status=1, Loop 75 sonrası BE bekliyor)
| Symbol | Hold | Entry | Mark | UPnl | BE Trigger? |
|---|---|---|---|---|---|
| XRPUSDT 10500 | 28min | $1.3764 | $1.3740 | -$0.181 | ❌ negatif |
| BTCUSDT 10502 | 13min | $77229 | $77236 | +$0.008 | ❌ +%0.01 (trigger %0.10) |
| ADAUSDT 10503 | 9min | $0.2481 | $0.2478 | -$0.151 | ❌ negatif |

## Loop 75 BE SL Deploy
- Bot PID 20444 ✓
- BreakEven Module Enabled ✓
- BeMoveTriggerPct 0.0010 / OffsetPct 0.0002 ✓
- Migration apply ✓
- Bekleniyor: pozisyon +%0.10 UPnl olunca BE trigger

## Loop 75 t30 Bekleniyor
- ScheduleWakeup 13:18 TR (1800s sonra)
- Yeni emit gelmesi + pozisyon UPnl pozitif olunca BE move tetiklenir
- TP hit + BE move kümülatif PnL'i toparlamalı

## Halt Eşikleri (Loop 75)
- Realized < -$0.30 (Loop 75 specific) → Loop 76 algoritma overhaul (binance-expert)
- 5+ ardışık SL → halt
- BE move tetiklenmiyor + timestop pattern devam → Loop 76 trailing stop

## Sıradaki Wakeup
**Loop 75 t30 wakeup zaten kurulu — 13:22 TR (4 dk sonra)**

— PM 2026-05-01 Loop 74 final (RsiCeiling 60 emit + timestop)
