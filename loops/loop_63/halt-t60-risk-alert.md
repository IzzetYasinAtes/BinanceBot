# Loop 63 — Halt @ t=60dk (2026-04-30 12:30 TR) — RiskAlert (5 SL)

## Halt Sebebi
5 ardışık SL → RiskAlert + StrategyDeactivated 5 (otomatik). 

**5 closed, hepsi LOSS, WR %0:**

| # | Coin | Realized |
|---|---|---|
| 1 | BTC | -$0.131 |
| 2 | ETH | -$0.084 |
| 3 | XRP | -$0.206 |
| 4 | ADA | -$0.251 |
| 5 | XRP | -$0.148 |
| **Total** | — | **-$0.820** realized |

EmaScalper bu BTC downtrend rejiminde **3. defa başarısız** (Loop 56, 61, 63). Pattern net.

## Loop 64 Çözüm: BB MeanRev v2 + EMA200 + 5 Coin

binance-expert (önceki) kararı A: EMA200 trend filter geri (Loop 59 fix) + 5 coin (kullanıcı kuralı) + MaxOpenPos=3.

**Mevcut altyapı:** BB MeanRev v2 EMA200 trend filter zaten kodda (commit `e5fb921`). Sadece appsettings'te BTC seed var, 4 yeni seed eklenecek.

### Param (Loop 60'tan)
- BBstd 2.0, RSI 35, volZ 0.3, EMA200 trend filter
- TpAtr 2.5, SlAtr 0.7 (R:R 3.57:1)
- MaxHold 90dk, Cooldown 8 bar
- 5 coin: BTC, ETH, XRP, SOL, ADA

### RiskProfile
- MaxOpenPositions: 5 → **3** (sıkı, korelasyon riski)
- MaxConsecutiveLosses: 5 → **3** (erken halt)

## Loop 41-63 Aggregate
| Cumulative | 62 trade, ~$11.18 net loss, %15 WR |
|---|---|

Tatil 5. günü, sermaye %2.2 kayıp.

## Sıradaki: Loop 64 Boot
1. appsettings: 5 EmaScalper Activate=false, 4 yeni BB MeanRev v2 seed ekle (ETH, XRP, SOL, ADA — BTC zaten var), RiskProfile sıkı
2. dotnet kill+DB reset+reseed+restart
3. Loop 64 boot rapor + ScheduleWakeup t30

— PM 2026-04-30 Loop 63 halt @ t=60 (RiskAlert)
