# Loop 72 — Halt @ t=120dk (2026-05-01 09:20 TR) — Circuit Breaker

## Halt Sebebi: 5 ARDIŞIK SL → Circuit Breaker → 5 Strateji DEAKTIVE

```json
RiskAlert: circuit_breaker (consecutive_losses=5) trippedAt 06:10:19 UTC
Strategies Status: 3 (Active) → 2 (Deactive) — tüm 5 KMS
```

## Kritik Teşhis: TP UNREACHABLE
Tüm 8 PositionClosed event reason = **`order_timestop`** (MaxHold 45dk geçti). HİÇBİRİ TP veya SL hit etmedi.

Bot doğru entry yapıyor ama fiyat TP'ye ulaşamadan zamanı doluyor → küçük loss/küçük kar.

## Trade Sıralı (Loop 72)
| # | Time | Symbol | PnL | Reason |
|---|---|---|---|---|
| 1 | 05:10:18 | BTC | -$0.062 | timestop |
| 2 | 05:10:18 | ETH | +$0.030 | timestop ✓ |
| 3 | 05:10:19 | **SOL** | **+$0.211** | timestop ✓ |
| 4 | 05:20:19 | ADA | -$0.090 | timestop |
| 5 | 05:25:19 | XRP | -$0.105 | timestop |
| 6 | 05:55:19 | BTC | -$0.091 | timestop |
| 7 | 06:05:19 | **ADA** | **-$0.290** | timestop ⚠️ |
| 8 | 06:10:19 | SOL | -$0.146 | timestop |

**TOTAL: -$0.542** (2 win, 6 loss, WR %25)
**5 ardışık SL #4-#8 → circuit_breaker**

## Cumulative
- Loop 71: +$0.850
- Loop 72: -$0.542
- **Total: +$0.308** (hala pozitif ama zayıf)

## Loop 73 Plan: TP/SL/MaxHold Tune

| Parametre | L72 | **L73** |
|---|---|---|
| `TpAtrMultiplier` | 1.8 | **1.2** (mid skor) |
| `TpAtrMultiplierLow` | 1.5 | **1.0** |
| `TpAtrMultiplierHigh` | 2.2 | **1.5** |
| `SlAtrMultiplier` | 0.75 | **0.55** (sıkı SL) |
| `SlAtrMultiplierLow` | 0.85 | **0.65** |
| `SlAtrMultiplierHigh` | 0.65 | **0.5** |
| `MinTpPct` | 0.005 | **0.003** |
| `MaxTpPct` | 0.025 | **0.015** |
| `MinSlPct` | 0.003 | **0.002** |
| `MaxSlPct` | 0.008 | **0.006** |
| `MaxHoldMinutes` | 45 | **30** |
| `MaxHoldMinutesLowScore` | 30 | **20** |
| `MaxHoldMinutesHighScore` | 60 | **45** |

**Mantık**: Küçük TP (~%0.3-1.5) + sıkı SL (~%0.2-0.6) + hızlı çıkış (20-45dk) → çok küçük kar/loss, daha çok trade, scalping yaklaşımı.

## Loop 73 Boot Adımları
1. Strategies UPDATE: Status=2 → 3 (reactivate) + ParametersJson tune
2. VirtualBalance update: CurrentBalance/Equity = 500.31 (carry-over)
3. DB reset (Positions/Orders/SystemEvents ilgili boot scope) — ya da bekle, Loop 73 boot fresh
4. Bot restart (backend-dev fix deploy)
5. Loop 73 boot.md
6. ScheduleWakeup t30

— PM 2026-05-01 Loop 72 halt @ t=120 (circuit breaker)
