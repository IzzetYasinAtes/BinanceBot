# Loop 45 Boot — BB Mean Reversion 15m Filtre Gevşetme (2026-04-28 03:51 TR)

## Pivot Sebebi
Loop 44 t240: 4 saat boyunca 0 sinyal (sıkı koşul `close<bbLower AND rsi14<30 AND volZ>1.0` BTC/ETH/XRP/SOL/ADA gibi blue-chip'lerde nadir tetikleniyor). Asia gece dilimi düşük volatilite ek katalizör.

24h bozuk loop yasağı disiplinine uygun — 4h'da pivot.

## Değişen Parametreler (kod yok, sadece appsettings.json)
| Parametre | Loop 44 | Loop 45 | Etki |
|---|---|---|---|
| `BbStdMultiplier` | 2.0 | **1.8** | BB band daralır → lower band daha sık dokunulur |
| `RsiOversoldThreshold` | 30 | **35** | oversold tanımı genişler |
| `VolumeZScoreThreshold` | 1.0 | **0.8** | panik teyidi gevşer |

Diğer parametreler aynı:
- `BbPeriod=20, RsiPeriod=14, VolumeWindow=20, AtrPeriod=14`
- `TpAtrMultiplier=1.5, SlAtrMultiplier=1.0`
- `MinTpPct=0.004, MaxTpPct=0.010, MinSlPct=0.003, MaxSlPct=0.006`
- `MaxHoldMinutes=90, MinAtrPct=0.0007, CooldownBarsAfterSignal=4`
- 5 coin aktif: BTC, ETH, XRP, SOL, ADA
- MaxOpenPositions=3, RiskPerTradePct=0.02

## Beklenti
- Sinyal frekansı: 0/h → ~0.5-1/h (3 filtre kombine ~%40-80 tetikleme artışı)
- Kalite muhtemelen düşer: WR Loop 44 hipotezi %45 → %38-42
- Beklenti: 4h'da en az 1-2 sinyal, BE veya az kar

## Boot State
| Metrik | Değer |
|---|---|
| Mode | Paper |
| StartingBalance | $500.0000 |
| CurrentCash | $500.0000 |
| Equity | $500.0000 |
| Realized | $0 |
| Open Pos | 0 |
| Active Strategies | 5 (BB Mean Rev) |
| API Port | 5188 |
| Branch | development (altın kural #10 — branch açma yasak) |

## Disiplin
- 4h karar penceresi (t240 = 07:51 TR): yine 0 sinyal kalırsa daha radikal pivot (5m timeframe veya farklı evaluator)
- Halt: Realized<-$1.50, 5+ ardışık SL, zombie>270dk, signal kayıp >4h

## Sıradaki Wakeup
**ScheduleWakeup 3600s → t=60dk (04:51 TR)**

— PM 2026-04-28 Loop 45 boot
