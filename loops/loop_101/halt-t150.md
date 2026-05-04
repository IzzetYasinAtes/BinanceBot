# Loop 101 Halt — t150 BE Eşik Pazar Volatilitesinden Yüksek

Tarih: 2026-05-04 09:01 UTC | Boot 06:26 UTC | Süre: 2h35m

## Halt: Realized -$0.57, BE Eşiği +0.20% Asla Aşılmıyor

### Closed (3, %67 win rate)
| # | Symbol | Direction | Entry | Exit | RPnL |
|---|---|---|---|---|---|
| 1 | ADAUSDT | Long | $0.2517 | $0.2521 | +$0.038 |
| 2 | SOLUSDT | Long | $84.71 | $84.82 | +$0.024 |
| 3 | ADAUSDT | Long | $0.2529 | $0.2516 | -$0.635 SL hit |

Net realized: -$0.573 (eşik -$1.50, marj $0.93). R:R 1:20.

### Open (3, hold 77-146dk, hiç kapanmadı)
| Symbol | Hold | UPnL | Peak/Entry-1 |
|---|---|---|---|
| BTCUSDT | 146min | -$0.19 | +0.087% |
| SOLUSDT | 92min | -$0.11 | +0.10% |
| ADAUSDT | 77min | -$0.27 | +0.06% |

Toplam UPnL: -$0.57 → netPnl -$1.29.

## Kök Sebep: BE Eşiği +0.20% Pazar Volatilitesinden Yüksek

21 loop boyunca pos açtıktan sonra max Peak/Entry-1: **+0.087% ile +0.18% arası** (5 coin). BE TriggerPct=0.002 (=+0.20%) **asla aşılmıyor**.

Sonuç: BE arm asla olmuyor → trailing locked profit yok → win zaman trailing değil küçük TP-yakın profit (avg $0.03), loss tam SL hit (avg -$0.63).

**Fix**: TriggerPct 0.002 → **0.001** (=+0.10%). Pos peak +0.10% civarındaysa BE arm + trailing.

## Loop 102 Tune (PM Doğrudan)

- `appsettings.json` BreakEven.TriggerPct 0.0020 → **0.0010** ✓
- `appsettings.json` Strategies.Seed[].ParametersJson BeMoveTriggerPct 0.002 → **0.001** ✓ (5 strateji)
- `appsettings.json` Strategies.Seed[].ParametersJson BeMoveOffsetPct 0.002 → **0.001** ✓ (BE move SL=entry*1.001)

## Korunur (Loop 95-101 fix'leri)
- Status=3 (Active) ✓ kritik
- WeightOverrides 7 Short=0 (Long-only)
- TrailPct 0.003 (winning pencere)
- MTF threshold 0.001m strict (downtrend filter)
- RiskPerTradePct 0.01 (pos sizing)
- MaxOpenPositions 3
- RequiredScore 2

## Hipotez

Loop 102 hedef:
- BE arm peak +0.10%'da tetiklenir (Loop 101'de gözlemlenen peak aralığı)
- Trailing %0.3 ile küçük locked profit (~+$0.05-0.10)
- Win amount büyür → R:R asymmetri azalır

İlk pozitif loop hedefi (21 loop sonra).

## Cumulative

21 loop -$23.4, 0 pozitif loop. Loop 102 = BE eşiği pazar volatilitesine ayarla.

## Sonraki

Bot restart + reset + Loop 102 boot.md.
