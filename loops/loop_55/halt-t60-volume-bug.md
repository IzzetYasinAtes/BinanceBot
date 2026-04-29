# Loop 55 — Halt @ t=60dk (2026-04-29 07:50 TR) — VOLUME BUG TEŞHİSİ

## Halt Sebebi
Loop 55 BBstd 1.3 (mevcut maks gevşeme) 60dk'da 0 yeni emit. Loop 54'te 1 emit gelmişti.

binance-expert teşhis: **VolumeZScoreThreshold=0.0 kodda gerçekte volume filtresini kapatmıyor.** `BbMeanReversionEvaluator.cs` içinde `VolumeStd20 > 0m` guard hala aktif → testnet düz hacim rejiminde kronik bloke.

Bu yüzden BB MeanRev her parametreyle çalışmaz. Mimari kod fix gerekli (gelecek backend-dev iş): `volumeOk` satırı `threshold <= 0m || (VolumeStd20 > 0m && volZ > threshold)` yapısına dönmeli.

## Karar — Loop 56: EmaScalper1m Config-Only Pivot

DB reset YOK — kar +$0.355 korunsun (Loop 54 ETH TP).

Parametre (binance-expert):

| Parametre | Değer |
|---|---|
| EmaFastPeriod | 9 |
| EmaSlowPeriod | 21 |
| RsiLowerBand | 35 (Loop 46'dan daha gevşek) |
| RsiUpperBand | 70 |
| VolumeMultiplier | 0.8 |
| MinAtrPct | 0.0003 |
| TpAtrMultiplier | 1.3 (Loop 46'da 1.5 idi, gerçekçi düşüş) |
| SlAtrMultiplier | 0.8 |
| MaxHoldMinutes | 10 |
| CooldownBarsAfterSignal | 2 |

5 coin: BTC, ETH, XRP, SOL, ADA. MaxOpenPositions=5.

## Loop 41-55 Aggregate
| Loop | Strateji | Trade | Realized |
|---|---|---|---|
| 41-43 | Donchian 15m | 11 | -$2.97 |
| 44-45 | BB MeanRev sıkı | 2 | +$0.011 |
| 46-48 | EmaScalper1m (3 cfg) | 12 | -$1.69 |
| 49 | BB MeanRev gevşek | 7 | -$0.576 |
| 50-52 | HybridMomentum | 0 | $0 |
| 53-55 | BB MeanRev maks | 1 | **+$0.355** ✓ |
| **Total** | — | **33** | **-$4.87** |

## Sıradaki: Loop 56 Boot (DB reset YOK)
1. appsettings: 5 BB MeanRev Activate=false, 5 EmaScalper1m Activate=true + yeni param
2. dotnet kill + restart (DB korundu, +$0.355 history kalır)
3. Loop 56 boot rapor
4. ScheduleWakeup t30

— PM 2026-04-29 Loop 55 halt @ t=60 (volume bug)
