# Loop 68 — Halt @ t=180dk (2026-05-01 03:33 TR)

## Halt Sebebi: Loop 70 Param Tune

3h KMS gevşek param (RSI 35 / TC 0.8 / Spread 0.005) sonuçları:
- ✓ ADA TimeStop **+$0.11 KAR** (ilk winning trade!)
- ✓ Realized iyileşme: -$0.619 → -$0.505 (+$0.114)
- ✓ WR %33.3 (1 win / 2 loss / 3 closed)
- ✗ Frekans 4 emit/180dk = **1.3/h** (hedef 5-15/h)
- ✗ BTC/ETH **180dk = 36 değerlendirme 0 emit** (asimetri)
- ✗ 150-180 arası 0 yeni emit

→ Param yön doğru ama **çok sıkı**, daha fazla gevşetme gerekli.

## Final Sayım (180dk)
| Metrik | Değer |
|---|---|
| SignalEmitted | 4 (BTC/ETH 0) |
| SignalSkipped | 177 |
| OrderFilled | 6 (3 entry + 3 exit) |
| **Realized PnL** | **-$0.505** |
| **WR** | **33.3% (1/3)** |
| Open Positions | 0 |
| RiskAlert | 0 |
| Commission | $0.45 |

## Trade Sonuçları
| Symbol | Side | Hold | PnL | Tip |
|---|---|---|---|---|
| SOLUSDT | Long | ~45min | ~-$0.20 | MaxHold (loss) |
| XRPUSDT | Long | ~45min | ~-$0.42 | MaxHold (loss) |
| **ADAUSDT** | Long | ~45min | **+$0.11** ✓ | **MaxHold (win)** |

## Loop 70 Param Tune

| Parametre | L68 | **L70** |
|---|---|---|
| `RsiRecoveryThreshold` | 35 | **38** (oversold pencere daha geniş) |
| `TradeCountMultiplier` | 0.8 | **0.6** (TC eşiği daha gevşek) |
| `MinAtrPct` | 0.0005 | **0.0003** (sessiz piyasa filter daha gevşek) |
| `SpreadThresholdPct` | 0.005 | **0.005** (sabit) |

Diğer aynı: RsiPeriod=14, EmaPeriod=9, TC Window=20, AtrPeriod=14, TpAtr=1.8, SlAtr=0.75, MinTp=0.005, MaxTp=0.018, MinSl=0.003, MaxSl=0.008, MaxHold=45, Cool=3.

## Sıradaki Loop 70 Boot
1. DB UPDATE Strategies WHERE Name LIKE '%-KMS' (5 row)
2. taskkill bot + DB reset (Positions/Orders/SystemEvents)
3. dotnet run --project src/Api &
4. loops/loop_70/boot.md yaz
5. ScheduleWakeup t30

— PM 2026-05-01 Loop 68 halt @ t=180
