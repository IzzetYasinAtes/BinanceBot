# Loop 56 Boot — EmaScalper1m Geri Dönüş (KAR KORUNDU) (2026-04-29 07:55 TR)

## Pivot Sebebi
binance-expert teşhis: **BB MeanRev `VolumeStd20 > 0m` guard kodda volume filtresini gerçekte kapatmıyor** → testnet düz hacim rejiminde kronik bloke. Backend-dev fix gerek (gelecek):
```csharp
volumeOk = p.VolumeZScoreThreshold <= 0m || (VolumeStd20 > 0m && volZ > threshold)
```

Mevcut alternatif: **EmaScalper1m geri dönüş** (mevcut evaluator, kod değişikliği yok).

## Loop 56 Parametreler (binance-expert spec)

| Parametre | Loop 46 (eski) | **Loop 56** | Değişim |
|---|---|---|---|
| RsiLowerBand | 40 | **35** | gevşek |
| RsiUpperBand | 65 | **70** | gevşek |
| VolumeMultiplier | 0.8 | 0.8 | aynı |
| MinAtrPct | 0.0003 | 0.0003 | aynı |
| **TpAtrMultiplier** | **1.5** | **1.3** | **küçültüldü (gerçekçi TP)** |
| SlAtrMultiplier | 0.8 | 0.8 | aynı |
| MaxHoldMinutes | 8 | 10 | uzatıldı |
| CooldownBarsAfterSignal | 2 | 2 | aynı |

R:R = 1.3/0.8 = 1.625:1, BE WR ~%38.

## 5 Aktif Coin
BTC, ETH, XRP, SOL, ADA. BNB/DOGE/LINK/DOT/AVAX/LTC/TRX EmaScalper'ları Activate=false. MaxOpenPositions=5.

## Boot State (KAR KORUNDU)
| Metrik | Değer |
|---|---|
| Mode | Paper |
| StartingBalance | $500.00 |
| **CurrentCash** | **$500.36** ✓ (Loop 54 ETH +$0.355) |
| **Equity** | **$500.36** |
| **Realized history** | **+$0.355** ✓ |
| Active | 5 (EmaScalper1m yeni param) |
| WR (history) | %100 (1/1) |

## Beklenti (binance-expert)
- Frekans: 5-15/saat (Loop 46'da 19/h idi, BB MeanRev'in 0.5/h'den çok daha iyi)
- WR: %35-50 (BE WR %38)
- Net günlük: BE veya hafif kar

## Halt Eşikleri (Loop 56)
- Realized < -$1.00 (kar buffer +$0.355 + $1.00 = $1.36 buffer) → halt
- 3+ ardışık SL → halt
- t30 = 0 emit → log incele
- t60 = minimum 2 emit gerekli
- t120 = 5+ toplam emit

## Sıradaki Wakeup
**ScheduleWakeup 1800s → t=30dk (08:25 TR)**

— PM 2026-04-29 Loop 56 boot
