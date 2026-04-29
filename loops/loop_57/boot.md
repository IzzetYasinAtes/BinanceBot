# Loop 57 Boot — BB MeanRev 15m + Volume Bug FIX (2026-04-29 09:08 TR)

## Pivot Sebebi
Loop 56 EmaScalper1m yeni param halt (WR %20, 5 trade). EmaScalper1m kalıcı kapatıldı (3 farklı param hepsinde başarısız → strateji konsepti rejime uymuyor).

binance-expert kararı: **C — BB MeanRev kod fix + Loop 49+ param**.

## Backend-Dev Fix (commit `e17e21d`)
`BbMeanReversionEvaluator.cs` satır 147-148 tek satır fix:
```csharp
var volumeOk = p.VolumeZScoreThreshold <= 0m
    || (snapshot.VolumeStd20 > 0m && volumeZScore > p.VolumeZScoreThreshold);
```

Önceki: `VolumeStd20 > 0m && volZ > threshold` (testnet'te kronik bloke)
Yeni: `threshold <= 0` ise filtre kapalı, threshold > 0 ise eski mantık.

Test: 296/296 pass (yeni `Threshold0_VolumeFilterOff_AllowsSignal` test dahil).

## Loop 57 Parametre

| Parametre | Değer |
|---|---|
| KlineInterval | 15m |
| BbPeriod | 20 |
| **BbStdMultiplier** | **1.8** (Loop 49: 2.0'dan düşük, daha sık lower breach) |
| RsiPeriod | 14 |
| **RsiOversoldThreshold** | **45** (Loop 49: 38'den yüksek, geniş pencere) |
| VolumeWindow | 20 |
| **VolumeZScoreThreshold** | **0.0** (volume filtresi KAPALI — fix çalışıyor) |
| AtrPeriod | 14 |
| TpAtrMultiplier | 1.8 |
| SlAtrMultiplier | 0.9 |
| MinTpPct | 0.005 |
| MaxTpPct | 0.012 |
| MinSlPct | 0.003 |
| MaxSlPct | 0.006 |
| MaxHoldMinutes | 120 |
| MinAtrPct | 0.0005 |
| **CooldownBarsAfterSignal** | **4** (Loop 49: 3'ten yüksek, whipsaw koruması) |

R:R = 2:1 (TP 1.8 / SL 0.9). BE WR %33.3.

## 5 Aktif Coin
BTC, ETH, XRP, SOL, ADA. Diğer hepsi Activate=false.

## Boot State (DB Reset YAPILDI)
| Metrik | Değer |
|---|---|
| Mode | Paper |
| StartingBalance | $500 |
| Equity | $500 (sıfırdan başla, kayıplar history'de) |
| Active | 5 (BB MeanRev15m volZ off + fix) |

## Beklenti (binance-expert)
- Frekans: 5-10 sinyal/gün (volume off ile artar)
- WR: %35-45 (Loop 49 %43 referansı)
- Net günlük: BE veya hafif kar
- Halt: Realized<-$1.50 / 4+ ardışık SL / WR<%25 (20+ trade)

## Loop 41-56 Aggregate
| Loop | Trade | Realized | WR |
|---|---|---|---|
| 41-43 | 11 | -$2.97 | %0 |
| 44-45 | 2 | +$0.011 | %50 |
| 46-48 | 12 | -$1.69 | %23 |
| 49 | 7 | -$0.576 | %43 |
| 50-53 | 0 | $0 | — |
| 54-55 | 1 | +$0.355 | %100 (ETH) |
| 56 | 5 | -$0.97 | %20 |
| **Total** | **38** | **-$5.85** | %18 |

## Sıradaki Wakeup
**ScheduleWakeup 1800s → t=30dk (09:38 TR)**

— PM 2026-04-29 Loop 57 boot
