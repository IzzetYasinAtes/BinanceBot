# Loop 61 — Halt @ t=60dk (2026-04-30 10:18 TR) — RiskAlert WR %20

## Halt Sebebi (Otomatik)
- 30 SignalEmitted/h ✓ (frekans hedefi tutuldu)
- Ama 10 closed: 2 WIN + 8 LOSS = **WR %20** (BE WR %51 çok altı)
- Realized -$0.566
- **RiskAlert** + **StrategyDeactivated 6** (RiskProfile MaxConsecutiveLosses=5 tetiklendi)

## Trade Detay (10 closed)
| # | Coin | Realized | Tip |
|---|---|---|---|
| 1 | SOL | -$0.110 | TimeStop |
| 2 | ETH | -$0.111 | TimeStop |
| 3 | BTC | -$0.137 | TimeStop |
| 4 | **XRP** | **+$0.365** ✓ | TP |
| 5 | BTC | +$0.018 | small WIN |
| 6 | ETH | -$0.022 | small loss |
| 7 | ADA | -$0.048 | TimeStop |
| 8 | XRP | -$0.221 | TimeStop |
| 9 | BTC | -$0.156 | TimeStop |
| 10 | ETH | -$0.144 | TimeStop |

8/10 trade TimeStop = TP'ye ulaşamadı. R:R 3.33:1 (TP %0.50 / SL %0.20) **çok geniş**. Sadece XRP'nin 1 trade'i TP yaptı (+$0.365 büyük).

## Kök Sorun
TP %0.50 1m bar'da 10dk'da nadir ulaşılır. Strateji **çoğunlukla TimeStop ile küçük zarar** kapatıyor. Komisyon ($0.15 round-trip) baskın.

## Loop 62 Pivot — TP/SL Daralt (Daha Gerçekçi BE WR)

| Parametre | Loop 61 | **Loop 62** | Etki |
|---|---|---|---|
| `TpAtrMultiplier` | 2.0 | **1.2** | TP %0.30 floor |
| `SlAtrMultiplier` | 0.6 | **0.5** | SL %0.20 |
| `MinTpPct` | 0.005 | **0.003** | %0.30 |
| `MaxTpPct` | 0.012 | **0.008** | |
| `MaxSlPct` | 0.004 | **0.004** | |

R:R = 1.2/0.5 = **2.4:1** (yine pozitif)
BE WR ≈ 0.20/(0.30+0.20) = **%40** (mevcut %20 yine altta ama daha yakın)

Frekans korunur (RSI/Vol/MinAtr aynı). Hedef: TP'ye daha kolay ulaş, BE WR'i mevcut WR'a yaklaştır.

## Sıradaki: Loop 62 Boot
1. appsettings TP/SL daralt (5 EmaScalper1m)
2. dotnet kill+restart (DB reset YOK)
3. Loop 62 boot rapor
4. ScheduleWakeup t30

— PM 2026-04-30 Loop 61 halt @ t=60 (RiskAlert otomatik)
