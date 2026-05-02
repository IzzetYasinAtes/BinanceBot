# Loop 82 Boot — Trailing/BE/MinSL Kalibrasyon (2026-05-02 14:59 TR)

## Pivot Sebebi
Loop 81 4 ardışık küçük loss (peak +%0.11-0.33 vardı, hepsi BE-stop veya trailing-exit küçük loss). CB tripped (consec_loss=4). Realized -$0.38.

binance-expert spec: testnet slippage %0.02 üzerine kalibre eski param yanlış değil, **trailing buffer dar (%0.15)** + **BE offset dar (%0.02)** = mean-reversion toleransı yok.

## Parametre Değişiklikleri

### Global (appsettings.json)
| Parametre | Loop 81 | **Loop 82** |
|-----------|---------|-------------|
| BreakEven.TriggerPct | 0.0010 | **0.0020** |
| BreakEven.OffsetPct | 0.0002 | **0.0010** |
| TrailingStop.TrailPct | 0.0015 | **0.0025** |

### Per-Strategy (Strategies.ParametersJson — 5 row UPDATE)
| Parametre | Loop 81 | **Loop 82** |
|-----------|---------|-------------|
| MinSlPct | 0.006 | **0.004** |
| MaxSlPct | 0.012 | **0.008** |
| BeMoveTriggerPct | 0.001 | **0.002** |
| BeMoveOffsetPct | 0.0002 | **0.001** |

### Sabit (Değişmedi)
- RequiredScore: 5
- TpRiskRewardRatio: 2.0 (R:R 1:2 korundu — binance-expert R:R değiştirme gereksiz dedi)
- MaxHoldMinutes: 60
- CooldownBarsAfterSignal: 2
- MaxOpenPositions: 3, MaxConsecutiveLosses: 4

## Beklenti
- SOL +%0.33 → net **+** (trailing 0.25 + slippage 0.02 = breakeven %0.27, peak %0.33 üstünde ✓)
- ETH +%0.26 → marginal (breakeven %0.27, marjin yok)
- XRP küçük peak (+%0.11-0.16) → BE armed olmadan SL hit veya TP

→ **Mean-reversion buffer artık yeterli**. WR hedef ≥%30 (1/4 win minimum).

## Boot State
| Metrik | Değer |
|---|---|
| Bot PID | 10716 |
| Port | 5188 |
| 5 Strateji | Active (Type=3 PatternComposite) |
| **CB Reset** | ✓ (Counter 0/4, Healthy) |
| Açık (L81 carryover) | 2 (ETH -$0.151, BTC -$0.119 — eski param ile entry, SL'ye kadar bırak) |
| VirtualBalance | $299.26 (L81 sonu) |

**Not**: Açık 2 pozisyon eski param ile entry alındı. SL=eski (0.6%) çalışır, kapandığında Loop 82 saymaya başlar.

## Loop 82 Sayım Başlangıcı
Boot zaman: 2026-05-02 11:59 UTC. Bu andan sonraki SignalEmitted/PositionClosed/Realized → Loop 82 metrik.

## L80 vs L81 vs L82 Hedef
| Metrik | L80 4h | L81 t210 | **L82 hedef 4h** |
|--------|--------|----------|------------------|
| Realized | -$0.51 | -$0.38 | **+$0.10 ila -$0.30** |
| WR | 0/3 | 0/4 | ≥1/4 (%25+) |
| Avg/Trade | -$0.17 | -$0.10 | **+$0.05 hedef** |

## Halt Eşikleri
- Realized < -$1.50 → halt + Loop 83
- 4+ ardışık küçük loss tekrar → trailing buffer hâlâ dar, daha radikal değişim
- 5+ ardışık SL → CB tripped (auto)

## Sıradaki Wakeup
**ScheduleWakeup 1800s → t=30dk (15:30 TR)**

— PM 2026-05-02 Loop 82 boot (trailing 0.0015→0.0025 + BE +%0.10→+%0.20 + slippage tampon, binance-expert spec deploy)
