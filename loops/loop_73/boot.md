# Loop 73 Boot — TP/SL Daralt + MaxHold Kısalt (Scalping) (2026-05-01 09:23 TR)

## Pivot Sebebi
Loop 72 t120 circuit_breaker (consecutive_losses=5). 8 trade hepsi `order_timestop` (TP unreachable). Bot doğru entry yapıyor, fiyat MaxHold (45dk) süresinde TP'ye varmıyor.

→ **Çözüm: Scalping yaklaşımı** — küçük TP, sıkı SL, hızlı çıkış, çok trade.

## Loop 71 → 72 → 73 Yörünge
| Loop | Realized | Trade | Pattern |
|---|---|---|---|
| L71 | +$0.85 | 4 closed (2 TP, 2 SL) | TP hit, kar |
| L72 | -$0.54 | 8 closed (1 TP, 7 timestop loss/small) | TP unreachable |
| **L73** | ? | TP daralt + hızlı | Scalping |

**Cumulative carry-over: +$0.31**

## Loop 73 Param Tune (TP/SL/MaxHold)

| Parametre | L72 | **L73** | Etki |
|---|---|---|---|
| `TpAtrMultiplier` | 1.8 | **1.2** | TP ATR×1.2 (daha küçük) |
| `TpAtrMultiplierLow` | 1.5 | **1.0** | |
| `TpAtrMultiplierHigh` | 2.2 | **1.5** | |
| `SlAtrMultiplier` | 0.75 | **0.55** | SL ATR×0.55 (sıkı) |
| `SlAtrMultiplierLow` | 0.85 | **0.65** | |
| `SlAtrMultiplierHigh` | 0.65 | **0.5** | |
| `MinTpPct` | 0.005 | **0.003** | TP min %0.3 |
| `MaxTpPct` | 0.025 | **0.015** | TP max %1.5 |
| `MinSlPct` | 0.003 | **0.002** | SL min %0.2 |
| `MaxSlPct` | 0.008 | **0.006** | SL max %0.6 |
| `MaxHoldMinutes` | 45 | **30** | MaxHold 30dk (6 bar) |
| `MaxHoldMinutesLowScore` | 30 | **20** | |
| `MaxHoldMinutesHighScore` | 60 | **45** | |

**Mantık**: Scalping geometrisi:
- TP %0.3-1.5 (önceki %0.5-2.5)
- SL %0.2-0.6 (önceki %0.3-0.8)
- MaxHold 20-45dk (önceki 30-60dk)
- TP/SL ratio ~2:1 hala korunur
- Hızlı çıkış = daha çok trade fırsatı

## Boot State
| Metrik | Değer |
|---|---|
| Cash / Equity | **$500.31** / $500.31 (carry-over L71+L72) ✓ |
| StartingBalance | $500 |
| Net PnL | +$0.31 |
| Active | 5 KMS reactivated (Status=3) ✓ |
| Bot PID | 7548 |
| WS State | Streaming ✓ |
| Warmup | 5/5 symbol completed ✓ |
| backend-dev fix deployed | ✓ (diagnostic guard + regression test) |
| DB reset | OrderFills 18 / Orders 18 / Positions 10 / SystemEvents 220 |

## Beklenti
- TP hit oranı yükselsin (15-30%)
- Frekans: 8-12 emit/h (cooldown daha sık)
- Realized hedef: +$0.50 net Loop 73 sonu

## Halt Eşikleri
- Realized < -$0.50 (Loop 73) → Loop 74 binance-expert (algoritma overhaul)
- Circuit breaker 5+ ardışık SL → otomatik halt
- t60 hala 0 emit → Loop 74 RsiOversoldZone 40→45

## Sıradaki Wakeup
**ScheduleWakeup 1800s → t=30dk (09:53 TR)**

— PM 2026-05-01 Loop 73 boot
